from __future__ import annotations

import inspect
from collections.abc import Iterable
from datetime import UTC, datetime, timedelta
from typing import Any

import pytest

from dqopr_netpulse.configuration import Thresholds
from dqopr_netpulse.models import (
    FaultDomain,
    Incident,
    IncidentType,
    Measurement,
    ProbeMethod,
    SessionConfig,
    SpeedTestResult,
)

diagnostics = pytest.importorskip(
    "dqopr_netpulse.diagnostics",
    reason=(
        "diagnostics.py is not implemented yet; these tests define the conservative API contract."
    ),
)


BASE_TIME = datetime(2026, 1, 2, 3, 4, 5, tzinfo=UTC)
SESSION_ID = "diagnostics-simulation"


def _sample(
    sequence: int,
    target_name: str,
    target_address: str,
    method: ProbeMethod = ProbeMethod.ICMP,
    success: bool = True,
    rtt_ms: float | None = 20.0,
    jitter_ms: float | None = 2.0,
    packet_loss_percent: float = 0.0,
    consecutive_loss_count: int = 0,
    error_type: str | None = None,
    is_gateway: bool = False,
) -> Measurement:
    return Measurement(
        session_id=SESSION_ID,
        target_name=target_name,
        target_address=target_address,
        method=method,
        sequence=sequence,
        success=success,
        timestamp_utc=BASE_TIME + timedelta(seconds=sequence),
        rtt_ms=rtt_ms,
        jitter_ms=jitter_ms,
        packet_loss_percent=packet_loss_percent,
        consecutive_loss_count=consecutive_loss_count,
        gateway_address="192.0.2.1" if not is_gateway else None,
        error_type=error_type,
    )


def _gateway(sequence: int, success: bool = True, rtt_ms: float | None = 2.0) -> Measurement:
    return _sample(
        sequence=sequence,
        target_name="Local Gateway",
        target_address="192.0.2.1",
        success=success,
        rtt_ms=rtt_ms,
        packet_loss_percent=0.0 if success else 100.0,
        consecutive_loss_count=0 if success else 1,
        error_type=None if success else "timeout",
        is_gateway=True,
    )


def _external(
    sequence: int,
    target_name: str = "Cloudflare",
    success: bool = True,
    rtt_ms: float | None = 20.0,
    jitter_ms: float | None = 2.0,
    packet_loss_percent: float = 0.0,
    consecutive_loss_count: int = 0,
) -> Measurement:
    hosts = {"Cloudflare": "1.1.1.1", "Google": "8.8.8.8", "Quad9": "9.9.9.9"}
    return _sample(
        sequence=sequence,
        target_name=target_name,
        target_address=hosts.get(target_name, "203.0.113.10"),
        success=success,
        rtt_ms=rtt_ms,
        jitter_ms=jitter_ms,
        packet_loss_percent=packet_loss_percent,
        consecutive_loss_count=consecutive_loss_count,
        error_type=None if success else "timeout",
    )


def _dns(sequence: int, success: bool = True, duration_ms: float | None = 14.0) -> Measurement:
    measurement = _sample(
        sequence=sequence,
        target_name="DNS Lookup",
        target_address="example.com",
        method=ProbeMethod.DNS,
        success=success,
        rtt_ms=None,
        jitter_ms=None,
        packet_loss_percent=0.0 if success else 100.0,
        consecutive_loss_count=0 if success else 1,
        error_type=None if success else "dns_timeout",
    )
    return Measurement(**{**measurement.__dict__, "dns_duration_ms": duration_ms})


def _speed(
    download_mbps: float | None,
    upload_mbps: float | None,
    sequence: int,
    success: bool = True,
) -> SpeedTestResult:
    return SpeedTestResult(
        session_id=SESSION_ID,
        timestamp_utc=BASE_TIME + timedelta(minutes=sequence),
        download_mbps=download_mbps,
        upload_mbps=upload_mbps,
        latency_ms=25.0 if success else None,
        success=success,
    )


def _classify(
    measurements: Iterable[Measurement],
    *,
    speed_tests: Iterable[SpeedTestResult] = (),
    config: SessionConfig | None = None,
    thresholds: Thresholds | None = None,
) -> list[Incident]:
    classify_measurements = diagnostics.classify_measurements
    signature = inspect.signature(classify_measurements)
    kwargs: dict[str, Any] = {}
    args: list[Any] = []
    session_id_parameter = signature.parameters.get("session_id")
    if session_id_parameter is not None and (
        session_id_parameter.kind is inspect.Parameter.KEYWORD_ONLY
    ):
        kwargs["session_id"] = SESSION_ID
    elif session_id_parameter is not None:
        args.append(SESSION_ID)
    if "speed_tests" in signature.parameters:
        kwargs["speed_tests"] = tuple(speed_tests)
    elif tuple(speed_tests):
        pytest.xfail("classify_measurements does not accept speed_tests yet.")
    if "config" in signature.parameters:
        kwargs["config"] = config or SessionConfig()
    if "thresholds" in signature.parameters:
        kwargs["thresholds"] = thresholds or Thresholds()
    args.append(list(measurements))
    incidents = classify_measurements(*args, **kwargs)
    assert isinstance(incidents, list)
    assert all(isinstance(incident, Incident) for incident in incidents)
    return incidents


def _types(incidents: list[Incident]) -> set[IncidentType]:
    return {incident.incident_type for incident in incidents}


def _first(incidents: list[Incident], incident_type: IncidentType) -> Incident:
    return next(incident for incident in incidents if incident.incident_type == incident_type)


def test_healthy_connection_has_no_incidents() -> None:
    measurements = [
        _gateway(1),
        _external(2, "Cloudflare"),
        _external(3, "Google"),
        _dns(4),
    ]

    assert _classify(measurements) == []


def test_local_gateway_loss_identifies_local_network_fault() -> None:
    measurements = [
        _gateway(1, success=False, rtt_ms=None),
        _gateway(2, success=False, rtt_ms=None),
        _external(3, "Cloudflare", success=False, rtt_ms=None),
        _external(4, "Google", success=False, rtt_ms=None),
    ]

    incidents = _classify(measurements)

    assert IncidentType.LOCAL_GATEWAY_PACKET_LOSS in _types(incidents)
    incident = _first(incidents, IncidentType.LOCAL_GATEWAY_PACKET_LOSS)
    assert incident.probable_fault_domain == FaultDomain.LOCAL_NETWORK
    assert "gateway" in incident.explanation.lower()


def test_external_loss_with_healthy_gateway_identifies_probable_upstream_fault() -> None:
    measurements = [
        _gateway(1, success=True, rtt_ms=2.0),
        _gateway(2, success=True, rtt_ms=3.0),
        _external(3, "Cloudflare", success=False, rtt_ms=None),
        _external(4, "Google", success=False, rtt_ms=None),
        _external(5, "Quad9", success=False, rtt_ms=None),
    ]

    incidents = _classify(measurements)

    assert IncidentType.EXTERNAL_PACKET_LOSS in _types(incidents)
    incident = _first(incidents, IncidentType.EXTERNAL_PACKET_LOSS)
    assert incident.probable_fault_domain == FaultDomain.ISP_OR_UPSTREAM
    assert set(incident.affected_targets) >= {"Cloudflare", "Google"}


def test_single_target_failure_is_inconclusive_not_general_outage() -> None:
    measurements = [
        _gateway(1),
        _external(2, "Cloudflare", success=False, rtt_ms=None),
        _external(3, "Google", success=True),
        _external(4, "Quad9", success=True),
    ]

    incidents = _classify(measurements)

    assert IncidentType.SINGLE_TARGET_FAILURE in _types(incidents)
    incident = _first(incidents, IncidentType.SINGLE_TARGET_FAILURE)
    assert incident.probable_fault_domain in {FaultDomain.TARGET_OR_ROUTE, FaultDomain.INCONCLUSIVE}
    assert IncidentType.COMPLETE_OUTAGE not in _types(incidents)


def test_dns_failure_with_ip_connectivity_identifies_dns_fault() -> None:
    measurements = [
        _gateway(1),
        _external(2, "Cloudflare", success=True),
        _external(3, "Google", success=True),
        _dns(4, success=False, duration_ms=None),
        _dns(5, success=False, duration_ms=None),
    ]

    incidents = _classify(measurements)

    assert IncidentType.DNS_FAILURE in _types(incidents)
    incident = _first(incidents, IncidentType.DNS_FAILURE)
    assert incident.probable_fault_domain == FaultDomain.DNS
    assert "dns" in incident.explanation.lower()


def test_high_latency_and_jitter_burst_are_classified() -> None:
    measurements = [
        _gateway(1, rtt_ms=2.0),
        _external(2, "Cloudflare", rtt_ms=24.0, jitter_ms=3.0),
        _external(3, "Cloudflare", rtt_ms=420.0, jitter_ms=88.0),
        _external(4, "Google", rtt_ms=390.0, jitter_ms=95.0),
    ]

    incidents = _classify(
        measurements,
        thresholds=Thresholds(high_latency_ms=150.0, high_jitter_ms=40.0),
    )

    assert IncidentType.HIGH_LATENCY in _types(incidents)
    assert IncidentType.HIGH_JITTER in _types(incidents)
    assert _first(incidents, IncidentType.HIGH_LATENCY).worst_latency_ms == 420.0


def test_consecutive_packet_loss_becomes_intermittent_or_external_outage() -> None:
    measurements = [_gateway(1), _gateway(2), _gateway(3)]
    measurements.extend(
        _external(
            sequence=10 + index,
            target_name="Cloudflare",
            success=False,
            rtt_ms=None,
            packet_loss_percent=100.0,
            consecutive_loss_count=index,
        )
        for index in range(1, 4)
    )

    incidents = _classify(measurements, thresholds=Thresholds(consecutive_loss_outage_count=3))

    assert _types(incidents) & {
        IncidentType.INTERMITTENT_OUTAGE,
        IncidentType.EXTERNAL_PACKET_LOSS,
        IncidentType.SINGLE_TARGET_FAILURE,
    }
    assert max(incident.consecutive_failures for incident in incidents) >= 3


def test_unknown_contracted_speed_does_not_create_speed_degradation_incident() -> None:
    incidents = _classify(
        [_gateway(1), _external(2, "Cloudflare")],
        speed_tests=[_speed(download_mbps=10.0, upload_mbps=2.0, sequence=1)],
        config=SessionConfig(contracted_download_mbps=None, contracted_upload_mbps=None),
    )

    assert IncidentType.DOWNLOAD_DEGRADATION not in _types(incidents)
    assert IncidentType.UPLOAD_DEGRADATION not in _types(incidents)


def test_speed_thresholds_classify_download_and_upload_degradation() -> None:
    incidents = _classify(
        [_gateway(1), _external(2, "Cloudflare")],
        speed_tests=[_speed(download_mbps=60.0, upload_mbps=8.0, sequence=1)],
        config=SessionConfig(contracted_download_mbps=100.0, contracted_upload_mbps=20.0),
        thresholds=Thresholds(speed_below_plan_warning_percent=90.0),
    )

    assert IncidentType.DOWNLOAD_DEGRADATION in _types(incidents)
    assert IncidentType.UPLOAD_DEGRADATION in _types(incidents)
