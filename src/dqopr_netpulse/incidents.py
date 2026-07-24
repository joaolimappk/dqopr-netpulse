"""Conservative incident classification from measurements."""

from __future__ import annotations

import statistics
from collections import defaultdict

from dqopr_netpulse.configuration import Thresholds
from dqopr_netpulse.models import (
    Confidence,
    FaultDomain,
    Incident,
    IncidentType,
    Measurement,
    ProbeMethod,
    SessionConfig,
    Severity,
    SpeedTestResult,
)


def classify_measurements(
    session_id_or_measurements: str | tuple[Measurement, ...] | list[Measurement],
    measurements: tuple[Measurement, ...] | list[Measurement] | None = None,
    *,
    session_id: str | None = None,
    speed_tests: tuple[SpeedTestResult, ...] | list[SpeedTestResult] = (),
    config: SessionConfig | None = None,
    thresholds: Thresholds | None = None,
) -> list[Incident]:
    """Classify a batch of measurements into conservative incident records."""
    thresholds = thresholds or Thresholds()
    resolved_session_id: str | None
    if isinstance(session_id_or_measurements, str):
        resolved_session_id = session_id_or_measurements
        samples = list(measurements or [])
    else:
        samples = list(session_id_or_measurements)
        resolved_session_id = session_id or (samples[0].session_id if samples else None)
    if not samples or resolved_session_id is None:
        return []

    incidents: list[Incident] = []
    by_method: dict[ProbeMethod, list[Measurement]] = defaultdict(list)
    for measurement in samples:
        by_method[measurement.method].append(measurement)

    gateway = [m for m in samples if _is_gateway(m)]
    external = [
        m for m in samples if not _is_gateway(m) and m.method in {ProbeMethod.ICMP, ProbeMethod.TCP}
    ]
    dns = by_method.get(ProbeMethod.DNS, [])
    https = by_method.get(ProbeMethod.HTTPS, [])

    gateway_loss = _loss_percent(gateway)
    external_loss = _loss_percent(external)
    failed_external_targets = {m.target_name for m in external if not m.success}
    successful_external_targets = {m.target_name for m in external if m.success}
    external_target_count = len({m.target_name for m in external})

    if gateway and gateway_loss >= thresholds.packet_loss_warning_percent:
        incidents.append(
            _incident(
                resolved_session_id,
                IncidentType.LOCAL_GATEWAY_PACKET_LOSS,
                gateway,
                Severity.HIGH,
                FaultDomain.LOCAL_NETWORK,
                Confidence.HIGH,
                "Instability was detected between this computer and the local gateway. "
                "This suggests a possible Wi-Fi, Ethernet cable, network adapter, router, "
                "or local-network issue.",
            )
        )

    if external and external_loss >= thresholds.packet_loss_warning_percent and gateway_loss == 0:
        if (
            len(failed_external_targets) >= 2
            or external_target_count >= 2
            and not successful_external_targets
        ):
            incidents.append(
                _incident(
                    resolved_session_id,
                    IncidentType.EXTERNAL_PACKET_LOSS,
                    [m for m in external if not m.success],
                    Severity.HIGH,
                    FaultDomain.ISP_OR_UPSTREAM,
                    Confidence.HIGH,
                    "The local gateway remained responsive while multiple independent internet "
                    "targets experienced failures. This suggests that the issue may be beyond "
                    "the local network, such as the modem, ISP connection, or upstream network.",
                )
            )
        elif len(failed_external_targets) == 1 and successful_external_targets:
            incidents.append(
                _incident(
                    resolved_session_id,
                    IncidentType.SINGLE_TARGET_FAILURE,
                    [m for m in external if not m.success],
                    Severity.LOW,
                    FaultDomain.TARGET_OR_ROUTE,
                    Confidence.LOW,
                    "One monitored target failed while other targets remained responsive. This "
                    "may be a target-specific or route-specific issue and does not by itself "
                    "prove a general internet outage.",
                )
            )

    dns_failures = [m for m in dns if not m.success]
    if dns_failures and _has_successful_direct_connectivity(samples):
        incidents.append(
            _incident(
                resolved_session_id,
                IncidentType.DNS_FAILURE,
                dns_failures,
                Severity.MEDIUM,
                FaultDomain.DNS,
                Confidence.MODERATE,
                "Internet connectivity remained available, but DNS queries failed or became "
                "unusually slow. This suggests a DNS-related issue.",
            )
        )

    https_failures = [m for m in https if not m.success]
    if https_failures and _has_successful_direct_connectivity(samples):
        incidents.append(
            _incident(
                resolved_session_id,
                IncidentType.HTTPS_FAILURE,
                https_failures,
                Severity.MEDIUM,
                FaultDomain.INCONCLUSIVE,
                Confidence.LOW,
                "HTTPS checks failed while some direct connectivity remained available. This "
                "can indicate a service, TLS, firewall, captive-portal, or route-specific problem.",
            )
        )

    latency_incident = _latency_incident(resolved_session_id, samples, thresholds)
    if latency_incident:
        incidents.append(latency_incident)

    jitter_incident = _jitter_incident(resolved_session_id, samples, thresholds)
    if jitter_incident:
        incidents.append(jitter_incident)

    outage_incident = _outage_incident(resolved_session_id, samples, thresholds)
    if outage_incident:
        incidents.append(outage_incident)

    incidents.extend(_speed_incidents(resolved_session_id, speed_tests, config, thresholds))
    return incidents


def _latency_incident(
    session_id: str,
    measurements: list[Measurement],
    thresholds: Thresholds,
) -> Incident | None:
    successful = [m for m in measurements if m.success and m.rtt_ms is not None]
    if not successful:
        return None
    latencies = [float(m.rtt_ms) for m in successful if m.rtt_ms is not None]
    if len(latencies) >= 5:
        median = statistics.median(latencies)
        spike_limit = max(
            thresholds.high_latency_ms,
            median * thresholds.latency_spike_multiplier,
            median + thresholds.latency_spike_min_delta_ms,
        )
    else:
        median = statistics.median(latencies)
        spike_limit = thresholds.high_latency_ms
    spike_samples = [m for m in successful if m.rtt_ms is not None and m.rtt_ms >= spike_limit]
    if spike_samples:
        return _incident(
            session_id,
            IncidentType.LATENCY_SPIKE
            if median < thresholds.high_latency_ms
            else IncidentType.HIGH_LATENCY,
            spike_samples,
            Severity.MEDIUM,
            FaultDomain.ISP_OR_UPSTREAM
            if not any(_is_gateway(m) for m in spike_samples)
            else FaultDomain.LOCAL_NETWORK,
            Confidence.MODERATE,
            "Latency exceeded the configured threshold using an absolute limit and a rolling "
            "baseline-relative spike rule. This indicates a meaningful degradation, not a minor "
            "normal fluctuation.",
        )
    return None


def _jitter_incident(
    session_id: str,
    measurements: list[Measurement],
    thresholds: Thresholds,
) -> Incident | None:
    jitter_samples = [
        m
        for m in measurements
        if m.success and m.jitter_ms is not None and m.jitter_ms >= thresholds.high_jitter_ms
    ]
    if not jitter_samples:
        return None
    return _incident(
        session_id,
        IncidentType.HIGH_JITTER,
        jitter_samples,
        Severity.MEDIUM,
        FaultDomain.INCONCLUSIVE,
        Confidence.MODERATE,
        "Jitter exceeded the configured mean consecutive-latency-change threshold. This can "
        "cause real-time applications to feel unstable even when average latency looks acceptable.",
    )


def _outage_incident(
    session_id: str,
    measurements: list[Measurement],
    thresholds: Thresholds,
) -> Incident | None:
    losses = [m for m in measurements if not m.success]
    if not losses:
        return None
    max_consecutive = max((m.consecutive_loss_count for m in losses), default=0)
    if max_consecutive < thresholds.consecutive_loss_outage_count:
        return None
    incident_type = (
        IncidentType.COMPLETE_OUTAGE
        if _loss_percent(measurements) >= 95
        else IncidentType.INTERMITTENT_OUTAGE
    )
    return _incident(
        session_id,
        incident_type,
        losses,
        Severity.CRITICAL if incident_type == IncidentType.COMPLETE_OUTAGE else Severity.HIGH,
        FaultDomain.INCONCLUSIVE,
        Confidence.MODERATE,
        "Multiple consecutive probes failed. The evidence supports an outage or intermittent "
        "outage, but the fault domain depends on whether local gateway and external failures "
        "occurred together.",
    )


def _speed_incidents(
    session_id: str,
    speed_tests: tuple[SpeedTestResult, ...] | list[SpeedTestResult],
    config: SessionConfig | None,
    thresholds: Thresholds,
) -> list[Incident]:
    if not speed_tests or config is None:
        return []
    incidents: list[Incident] = []
    latest = speed_tests[-1]
    if latest.download_mbps is not None and config.contracted_download_mbps:
        percent = latest.download_mbps / config.contracted_download_mbps * 100.0
        if percent < thresholds.speed_below_plan_warning_percent:
            incidents.append(
                Incident(
                    session_id=session_id,
                    incident_type=IncidentType.DOWNLOAD_DEGRADATION,
                    start_time_utc=latest.timestamp_utc,
                    end_time_utc=latest.timestamp_utc,
                    severity=_speed_severity(percent, thresholds),
                    affected_tests=(ProbeMethod.SPEED.value,),
                    affected_targets=("speed-test-server",),
                    worst_latency_ms=latest.latency_ms,
                    packet_loss_percent=None,
                    consecutive_failures=0,
                    probable_fault_domain=FaultDomain.INCONCLUSIVE,
                    confidence=Confidence.MODERATE,
                    explanation=(
                        f"Measured download speed was {percent:.1f}% of the contracted rate. "
                        "A single speed test is not proof of a persistent service problem; "
                        "NetPulse analyzes repeated results over time."
                    ),
                )
            )
    if latest.upload_mbps is not None and config.contracted_upload_mbps:
        percent = latest.upload_mbps / config.contracted_upload_mbps * 100.0
        if percent < thresholds.speed_below_plan_warning_percent:
            incidents.append(
                Incident(
                    session_id=session_id,
                    incident_type=IncidentType.UPLOAD_DEGRADATION,
                    start_time_utc=latest.timestamp_utc,
                    end_time_utc=latest.timestamp_utc,
                    severity=_speed_severity(percent, thresholds),
                    affected_tests=(ProbeMethod.SPEED.value,),
                    affected_targets=("speed-test-server",),
                    worst_latency_ms=latest.latency_ms,
                    packet_loss_percent=None,
                    consecutive_failures=0,
                    probable_fault_domain=FaultDomain.INCONCLUSIVE,
                    confidence=Confidence.MODERATE,
                    explanation=(
                        f"Measured upload speed was {percent:.1f}% of the contracted rate. "
                        "This should be interpreted alongside repeated tests, latency, and "
                        "packet-loss evidence."
                    ),
                )
            )
    return incidents


def _speed_severity(percent: float, thresholds: Thresholds) -> Severity:
    if percent < thresholds.speed_below_plan_critical_percent:
        return Severity.CRITICAL
    if percent < thresholds.speed_below_plan_major_percent:
        return Severity.HIGH
    return Severity.MEDIUM


def _incident(
    session_id: str,
    incident_type: IncidentType,
    samples: list[Measurement],
    severity: Severity,
    fault_domain: FaultDomain,
    confidence: Confidence,
    explanation: str,
) -> Incident:
    sorted_samples = sorted(samples, key=lambda m: m.timestamp_utc)
    return Incident(
        session_id=session_id,
        incident_type=incident_type,
        start_time_utc=sorted_samples[0].timestamp_utc,
        end_time_utc=sorted_samples[-1].timestamp_utc,
        severity=severity,
        affected_tests=tuple(sorted({m.method.value for m in sorted_samples})),
        affected_targets=tuple(sorted({m.target_name for m in sorted_samples})),
        worst_latency_ms=max((m.rtt_ms or 0.0 for m in sorted_samples), default=0.0),
        packet_loss_percent=_loss_percent(sorted_samples),
        consecutive_failures=max((m.consecutive_loss_count for m in sorted_samples), default=0),
        local_gateway_status="degraded"
        if any(_is_gateway(m) and not m.success for m in sorted_samples)
        else "healthy",
        external_target_status="degraded"
        if any(not _is_gateway(m) and not m.success for m in sorted_samples)
        else "healthy",
        dns_status="degraded"
        if any(m.method == ProbeMethod.DNS and not m.success for m in sorted_samples)
        else "not affected",
        https_status="degraded"
        if any(m.method == ProbeMethod.HTTPS and not m.success for m in sorted_samples)
        else "not affected",
        probable_fault_domain=fault_domain,
        confidence=confidence,
        explanation=explanation,
    )


def _loss_percent(measurements: list[Measurement]) -> float:
    if not measurements:
        return 0.0
    failed = sum(1 for m in measurements if not m.success)
    return failed / len(measurements) * 100.0


def _has_successful_direct_connectivity(measurements: list[Measurement]) -> bool:
    return any(m.success and m.method in {ProbeMethod.ICMP, ProbeMethod.TCP} for m in measurements)


def _is_gateway(measurement: Measurement) -> bool:
    return measurement.target_name.lower() in {"local gateway", "gateway", "router"} or (
        measurement.gateway_address is not None
        and measurement.target_address == measurement.gateway_address
    )
