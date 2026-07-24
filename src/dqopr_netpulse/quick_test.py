"""One-cycle quick-test orchestration for GUI and future CLI use."""

from __future__ import annotations

import asyncio
import subprocess
import time
from collections.abc import Callable
from dataclasses import dataclass, replace

from dqopr_netpulse.configuration import AppConfig
from dqopr_netpulse.incidents import classify_measurements
from dqopr_netpulse.models import Measurement, ProbeMethod, SpeedTestResult
from dqopr_netpulse.monitoring.engine import InterfaceProvider, MonitoringSession
from dqopr_netpulse.networking import detect_active_interface
from dqopr_netpulse.probes import ProbeRunner, calculate_jitter
from dqopr_netpulse.speedtest import run_speedtest_cli, speed_percentage
from dqopr_netpulse.storage import NetPulseStore

QuickActivityCallback = Callable[[str], None]
QuickProgressCallback = Callable[[int, int, str], None]
QuickMeasurementCallback = Callable[[Measurement], None]

QUICK_TEST_STAGES: tuple[str, ...] = (
    "Detecting connection",
    "Testing local router",
    "Testing internet latency",
    "Measuring packet loss",
    "Testing DNS",
    "Testing website connectivity",
    "Testing download speed",
    "Testing upload speed",
    "Analyzing results",
    "Saving report",
)


@dataclass(frozen=True)
class QuickTestSummary:
    session_id: str
    completed: bool
    cancelled: bool
    duration_seconds: float
    overall: str
    local_router_status: str
    average_latency_ms: float | None
    peak_latency_ms: float | None
    jitter_ms: float | None
    packet_loss_percent: float
    dns_result: str
    https_result: str
    download_mbps: float | None
    upload_mbps: float | None
    download_percent: float | None
    upload_percent: float | None
    detected_problems: tuple[str, ...]


class QuickTestRunner:
    """Runs exactly one diagnostic cycle plus a one-shot speed test."""

    def __init__(
        self,
        app_config: AppConfig,
        store: NetPulseStore,
        *,
        activity_callback: QuickActivityCallback | None = None,
        progress_callback: QuickProgressCallback | None = None,
        measurement_callback: QuickMeasurementCallback | None = None,
        speedtest_runner: Callable[[str], SpeedTestResult] = run_speedtest_cli,
        probe_runner: ProbeRunner | None = None,
        interface_provider: InterfaceProvider = detect_active_interface,
    ) -> None:
        quick_session = replace(
            app_config.session,
            cycle_count=1,
            duration_seconds=None,
            latency_interval_seconds=min(app_config.session.latency_interval_seconds, 0.1),
            speedtest_enabled=True,
        )
        self.app_config = replace(app_config, session=quick_session)
        self.store = store
        self.activity_callback = activity_callback
        self.progress_callback = progress_callback
        self.measurement_callback = measurement_callback
        self.speedtest_runner = speedtest_runner
        self.probe_runner = probe_runner
        self.interface_provider = interface_provider
        self._session: MonitoringSession | None = None
        self._measurements: list[Measurement] = []
        self._speed_test: SpeedTestResult | None = None
        self._cancelled = False

    async def run(self) -> QuickTestSummary:
        started = time.monotonic()
        self._stage(1, "Detecting connection")
        self._activity("Detecting active network adapter...")
        self._activity(_route_inspection_message())
        self._session = MonitoringSession(
            self.app_config,
            self.store,
            probe_runner=self.probe_runner,
            interface_provider=self.interface_provider,
            activity_callback=self._activity,
            measurement_callback=self._measurement,
            state_callback=self._state,
        )
        session_id = await self._session.run()
        if not self._cancelled:
            self._stage(7, "Testing download speed")
            self._activity("Selecting speed-test server...")
            self._activity("Measuring download speed...")
            self._stage(8, "Testing upload speed")
            self._activity("Measuring upload speed...")
            self._speed_test = await asyncio.to_thread(self.speedtest_runner, session_id)
            self.store.add_speed_test(self._speed_test)
            self._activity(_speed_activity(self._speed_test))

        self._stage(9, "Analyzing results")
        self._activity("Analyzing results...")
        for incident in classify_measurements(
            self._measurements,
            session_id=session_id,
            speed_tests=[self._speed_test] if self._speed_test is not None else [],
            config=self.app_config.session,
            thresholds=self.app_config.thresholds,
        ):
            self.store.add_incident(incident)
        self._stage(10, "Saving report")
        self._activity("Saving measurements...")
        summary = summarize_quick_test(
            session_id,
            self._measurements,
            self._speed_test,
            self.app_config,
            duration_seconds=time.monotonic() - started,
            cancelled=self._cancelled,
        )
        self._activity("Quick test completed." if summary.completed else "Quick test stopped.")
        return summary

    def cancel(self) -> None:
        self._cancelled = True
        if self._session is not None:
            self._session.stop()
        self._activity("Quick test cancellation requested. Partial results will be saved.")

    def _measurement(self, measurement: Measurement) -> None:
        self._measurements.append(measurement)
        if measurement.method == ProbeMethod.ICMP and measurement.target_name == "Local Gateway":
            self._stage(2, "Testing local router")
            self._activity("Testing connection to your router...")
        elif measurement.method == ProbeMethod.ICMP:
            self._stage(3, "Testing internet latency")
            self._activity(f"Pinging {measurement.target_name}...")
        elif measurement.method == ProbeMethod.TCP:
            self._stage(4, "Measuring packet loss")
            self._activity("Calculating jitter...")
            self._activity("Measuring packet loss...")
        elif measurement.method == ProbeMethod.DNS:
            self._stage(5, "Testing DNS")
            self._activity("Resolving DNS...")
        elif measurement.method == ProbeMethod.HTTPS:
            self._stage(6, "Testing website connectivity")
            self._activity("Testing HTTPS connectivity...")
        if self.measurement_callback is not None:
            self.measurement_callback(measurement)

    def _state(self, state: str) -> None:
        if state == "completed" and self._cancelled:
            self._activity("One-cycle probe stage stopped before every requested step completed.")

    def _activity(self, message: str) -> None:
        if self.activity_callback is not None:
            self.activity_callback(message)

    def _stage(self, step: int, label: str) -> None:
        if self.progress_callback is not None:
            self.progress_callback(step, len(QUICK_TEST_STAGES), label)


def summarize_quick_test(
    session_id: str,
    measurements: list[Measurement],
    speed_test: SpeedTestResult | None,
    app_config: AppConfig,
    *,
    duration_seconds: float,
    cancelled: bool = False,
) -> QuickTestSummary:
    successful_latencies = [
        float(m.rtt_ms) for m in measurements if m.success and m.rtt_ms is not None
    ]
    failures = [m for m in measurements if not m.success]
    jitter_values = [float(m.jitter_ms) for m in measurements if m.jitter_ms is not None]
    icmp_latencies = [
        float(m.rtt_ms)
        for m in measurements
        if m.method == ProbeMethod.ICMP and m.success and m.rtt_ms is not None
    ]
    jitter_ms = (
        sum(jitter_values) / len(jitter_values)
        if jitter_values
        else calculate_jitter(icmp_latencies)
    )
    dns_samples = [m for m in measurements if m.method == ProbeMethod.DNS]
    https_samples = [m for m in measurements if m.method == ProbeMethod.HTTPS]
    gateway_samples = [m for m in measurements if m.target_name == "Local Gateway"]
    packet_loss_percent = len(failures) / len(measurements) * 100.0 if measurements else 0.0
    incidents = classify_measurements(
        measurements,
        session_id=session_id,
        speed_tests=[speed_test] if speed_test is not None else [],
        config=app_config.session,
        thresholds=app_config.thresholds,
    )
    problems = tuple(incident.explanation for incident in incidents)
    return QuickTestSummary(
        session_id=session_id,
        completed=not cancelled,
        cancelled=cancelled,
        duration_seconds=duration_seconds,
        overall=_overall(packet_loss_percent, problems, cancelled),
        local_router_status=_sample_status(gateway_samples),
        average_latency_ms=sum(successful_latencies) / len(successful_latencies)
        if successful_latencies
        else None,
        peak_latency_ms=max(successful_latencies) if successful_latencies else None,
        jitter_ms=jitter_ms,
        packet_loss_percent=packet_loss_percent,
        dns_result=_sample_status(dns_samples),
        https_result=_sample_status(https_samples),
        download_mbps=speed_test.download_mbps if speed_test is not None else None,
        upload_mbps=speed_test.upload_mbps if speed_test is not None else None,
        download_percent=speed_percentage(
            speed_test.download_mbps if speed_test is not None else None,
            app_config.session.contracted_download_mbps,
        ),
        upload_percent=speed_percentage(
            speed_test.upload_mbps if speed_test is not None else None,
            app_config.session.contracted_upload_mbps,
        ),
        detected_problems=problems,
    )


def _sample_status(samples: list[Measurement]) -> str:
    if not samples:
        return "Unavailable"
    if all(sample.success for sample in samples):
        return "Healthy"
    if any(sample.success for sample in samples):
        return "Degraded"
    return "Failed"


def _overall(packet_loss_percent: float, problems: tuple[str, ...], cancelled: bool) -> str:
    if cancelled:
        return "Incomplete snapshot"
    if problems or packet_loss_percent > 0:
        return "Connection degraded"
    return "Healthy snapshot"


def _speed_activity(result: SpeedTestResult) -> str:
    if not result.success:
        return f"Speed test unavailable: {result.error_message or 'not completed'}"
    download = f"{result.download_mbps:.1f} Mbps" if result.download_mbps is not None else "unknown"
    upload = f"{result.upload_mbps:.1f} Mbps" if result.upload_mbps is not None else "unknown"
    return f"Speed test saved: {download} down / {upload} up."


def _route_inspection_message() -> str:
    args = ["route", "print", "-4"] if _is_windows() else ["ip", "route"]
    try:
        completed = subprocess.run(
            args,
            check=False,
            capture_output=True,
            text=True,
            timeout=5,
        )
    except (OSError, subprocess.TimeoutExpired):
        return "Route inspection was unavailable on this system."
    if completed.returncode != 0:
        return "Route inspection did not return a usable route snapshot."
    return "Route inspection completed."


def _is_windows() -> bool:
    import platform

    return platform.system().lower() == "windows"
