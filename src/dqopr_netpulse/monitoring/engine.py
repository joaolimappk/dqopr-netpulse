"""Asynchronous monitoring session engine."""

from __future__ import annotations

import asyncio
import json
import logging
import time
from collections.abc import Awaitable, Callable
from dataclasses import asdict
from uuid import uuid4

from dqopr_netpulse.configuration import AppConfig, validate_session_config
from dqopr_netpulse.incidents import classify_measurements
from dqopr_netpulse.models import (
    ManualMarker,
    Measurement,
    NetworkInterfaceSnapshot,
    ProbeMethod,
    SpeedTestResult,
    Target,
)
from dqopr_netpulse.networking import default_targets_with_gateway, detect_active_interface
from dqopr_netpulse.probes import ProbeRunner
from dqopr_netpulse.speedtest import run_speedtest_cli
from dqopr_netpulse.storage import NetPulseStore

LOGGER = logging.getLogger(__name__)
InterfaceProvider = Callable[[], NetworkInterfaceSnapshot]
ActivityCallback = Callable[[str], None]
MeasurementCallback = Callable[[Measurement], None]
SpeedTestCallback = Callable[[SpeedTestResult], None]
StateCallback = Callable[[str], None]
SpeedTestRunner = Callable[[str], SpeedTestResult]


class MonitoringSession:
    """Coordinates probes, persistence, marker capture, and basic incident detection."""

    def __init__(
        self,
        app_config: AppConfig,
        store: NetPulseStore,
        probe_runner: ProbeRunner | None = None,
        interface_provider: InterfaceProvider = detect_active_interface,
        session_id: str | None = None,
        activity_callback: ActivityCallback | None = None,
        measurement_callback: MeasurementCallback | None = None,
        speedtest_callback: SpeedTestCallback | None = None,
        state_callback: StateCallback | None = None,
        speedtest_runner: SpeedTestRunner = run_speedtest_cli,
    ) -> None:
        validate_session_config(app_config.session)
        self.app_config = app_config
        self.store = store
        self.probe_runner = probe_runner or ProbeRunner()
        self.interface_provider = interface_provider
        self.session_id = session_id or str(uuid4())
        self.activity_callback = activity_callback
        self.measurement_callback = measurement_callback
        self.speedtest_callback = speedtest_callback
        self.state_callback = state_callback
        self.speedtest_runner = speedtest_runner
        self._stop_event = asyncio.Event()
        self._pause_event = asyncio.Event()
        self._pause_event.set()
        self._sequence = 0
        self._recent_measurements: list[Measurement] = []
        self._recent_speed_tests: list[SpeedTestResult] = []

    async def run(self) -> str:
        """Run the monitoring session until duration, cycle count, or stop."""
        config = self.app_config.session
        config_json = json.dumps(asdict(config), default=str, sort_keys=True)
        self._state("starting")
        self._activity("Starting monitoring engine.")
        self.store.create_session(self.session_id, config_json)
        self._activity("Session created. Measurements will be saved continuously.")
        started_monotonic = time.monotonic()
        cycles = 0
        self._activity("Detecting active network adapter and default gateway.")
        interface = self.interface_provider()
        targets = default_targets_with_gateway(config.targets, interface)
        if targets and targets[0].is_gateway:
            self._activity(f"Default gateway detected: {targets[0].host}.")
        enabled_count = len([target for target in targets if target.enabled])
        self._activity(f"Initialized {enabled_count} monitoring target(s).")
        self._state("monitoring")
        LOGGER.info("Starting monitoring session %s with %s targets", self.session_id, len(targets))
        next_speedtest_due = started_monotonic if config.speedtest_enabled else None
        try:
            while not self._stop_event.is_set():
                await self._pause_event.wait()
                now = time.monotonic()
                if (
                    config.duration_seconds is not None
                    and now - started_monotonic >= config.duration_seconds
                ):
                    break
                if config.cycle_count is not None and cycles >= config.cycle_count:
                    break
                if next_speedtest_due is not None and now >= next_speedtest_due:
                    await self._run_speedtest()
                    next_speedtest_due = _next_speedtest_due(
                        next_speedtest_due,
                        time.monotonic(),
                        config.speedtest_interval_seconds,
                    )
                await self._run_cycle(targets)
                cycles += 1
                self._activity(
                    "Waiting "
                    f"{config.latency_interval_seconds:g} seconds before the next probe cycle."
                )
                await asyncio.sleep(config.latency_interval_seconds)
        except Exception:
            self.store.finish_session(self.session_id, "failed")
            self._state("error")
            self._activity("Monitoring stopped unexpectedly because the worker failed.")
            LOGGER.exception("Monitoring session failed")
            raise
        else:
            self.store.finish_session(self.session_id, "completed")
            self._state("completed")
            self._activity("Monitoring session completed.")
        return self.session_id

    def stop(self) -> None:
        self._activity("Stopping monitoring session.")
        self._stop_event.set()
        self._pause_event.set()

    def pause(self) -> None:
        self._state("paused")
        self._activity("Monitoring paused. No new probe cycles will start until resumed.")
        self._pause_event.clear()

    def resume(self) -> None:
        self._state("monitoring")
        self._activity("Monitoring resumed.")
        self._pause_event.set()

    def add_manual_marker(self, note: str | None = None) -> int:
        snapshot = self.interface_provider()
        current = self.current_summary()
        marker = ManualMarker(
            session_id=self.session_id,
            note=note,
            metrics_snapshot=current,
            active_interface_name=snapshot.name,
            wifi_signal_percent=snapshot.wifi_signal_percent,
            gateway_status=str(current.get("gateway_status", "unknown")),
            public_target_status=str(current.get("internet_status", "unknown")),
        )
        return self.store.add_marker(marker)

    def current_summary(self) -> dict[str, object]:
        recent = self._recent_measurements[-50:]
        successful_latencies = [
            float(m.rtt_ms) for m in recent if m.success and m.rtt_ms is not None
        ]
        failures = [m for m in recent if not m.success]
        external_failures = [m for m in failures if m.target_name != "Local Gateway"]
        gateway_failures = [m for m in failures if m.target_name == "Local Gateway"]
        avg_latency = (
            sum(successful_latencies) / len(successful_latencies) if successful_latencies else None
        )
        jitter_values = [float(m.jitter_ms) for m in recent if m.jitter_ms is not None]
        avg_jitter = sum(jitter_values) / len(jitter_values) if jitter_values else None
        latest_speed = self._recent_speed_tests[-1] if self._recent_speed_tests else None
        return {
            "status": _status_label(recent),
            "current_latency_ms": avg_latency,
            "current_jitter_ms": avg_jitter,
            "recent_packet_loss_percent": len(failures) / len(recent) * 100.0 if recent else 0.0,
            "gateway_status": "degraded" if gateway_failures else "healthy",
            "internet_status": "degraded" if external_failures else "healthy",
            "dns_status": _method_status(recent, ProbeMethod.DNS),
            "https_status": _method_status(recent, ProbeMethod.HTTPS),
            "download_mbps": latest_speed.download_mbps if latest_speed is not None else None,
            "upload_mbps": latest_speed.upload_mbps if latest_speed is not None else None,
        }

    async def _run_cycle(self, targets: tuple[Target, ...]) -> None:
        self._sequence += 1
        interface = self.interface_provider()
        enabled_targets = [target for target in targets if target.enabled]
        if interface.name:
            self._activity(f"Using active adapter: {interface.name}.")
        tasks: list[Awaitable[Measurement]] = [
            self._probe_icmp(target, interface) for target in enabled_targets
        ]
        tasks.extend(
            self._probe_tcp(target, interface)
            for target in enabled_targets
            if not target.is_gateway
        )
        public_targets = [target for target in enabled_targets if not target.is_gateway]
        if enabled_targets:
            dns_https_target = public_targets[0] if public_targets else enabled_targets[0]
            tasks.append(self._probe_dns(dns_https_target, interface))
            tasks.append(self._probe_https(dns_https_target, interface))
        measurements = await asyncio.gather(*tasks)
        for measurement in measurements:
            self.store.add_measurement(measurement)
            self._recent_measurements.append(measurement)
            self._measurement(measurement)
            self._activity(_measurement_activity(measurement))
        self._recent_measurements = self._recent_measurements[-500:]
        self._activity("Measurements saved.")
        for incident in classify_measurements(
            list(measurements),
            session_id=self.session_id,
            thresholds=self.app_config.thresholds,
        ):
            self.store.add_incident(incident)
            self._activity(f"Incident detected: {incident.incident_type.value}.")

    async def _run_speedtest(self) -> None:
        self._activity("Starting scheduled speed test.")
        result = await asyncio.to_thread(self.speedtest_runner, self.session_id)
        self.store.add_speed_test(result)
        self._recent_speed_tests.append(result)
        self._recent_speed_tests = self._recent_speed_tests[-100:]
        self._speedtest(result)
        for incident in classify_measurements(
            self._recent_measurements[-50:],
            session_id=self.session_id,
            speed_tests=[result],
            config=self.app_config.session,
            thresholds=self.app_config.thresholds,
        ):
            self.store.add_incident(incident)
            self._activity(f"Incident detected: {incident.incident_type.value}.")
        if result.success:
            download = (
                f"{result.download_mbps:.1f} Mbps"
                if result.download_mbps is not None
                else "unknown"
            )
            upload = (
                f"{result.upload_mbps:.1f} Mbps" if result.upload_mbps is not None else "unknown"
            )
            self._activity(f"Scheduled speed test saved: {download} down / {upload} up.")
        else:
            self._activity(f"Scheduled speed test unavailable: {result.error_message or 'failed'}.")

    async def _probe_icmp(self, target: Target, interface: NetworkInterfaceSnapshot) -> Measurement:
        label = "local gateway" if target.is_gateway else f"{target.name} {target.host}"
        self._activity(f"Pinging {label}.")
        return await self.probe_runner.icmp(self.session_id, target, self._sequence, interface)

    async def _probe_tcp(self, target: Target, interface: NetworkInterfaceSnapshot) -> Measurement:
        self._activity(f"Checking website port {target.tcp_port} on {target.name}.")
        return await self.probe_runner.tcp(self.session_id, target, self._sequence, interface)

    async def _probe_dns(self, target: Target, interface: NetworkInterfaceSnapshot) -> Measurement:
        self._activity("Resolving example.com through configured DNS.")
        return await self.probe_runner.dns(self.session_id, target, self._sequence, interface)

    async def _probe_https(
        self, target: Target, interface: NetworkInterfaceSnapshot
    ) -> Measurement:
        self._activity(f"Testing HTTPS connectivity to {target.name}.")
        return await self.probe_runner.https(self.session_id, target, self._sequence, interface)

    def _activity(self, message: str) -> None:
        if self.activity_callback is not None:
            self.activity_callback(message)

    def _measurement(self, measurement: Measurement) -> None:
        if self.measurement_callback is not None:
            self.measurement_callback(measurement)

    def _speedtest(self, result: SpeedTestResult) -> None:
        if self.speedtest_callback is not None:
            self.speedtest_callback(result)

    def _state(self, state: str) -> None:
        if self.state_callback is not None:
            self.state_callback(state)


def _method_status(measurements: list[Measurement], method: ProbeMethod) -> str:
    relevant = [m for m in measurements if m.method == method]
    if not relevant:
        return "unknown"
    return "degraded" if any(not m.success for m in relevant[-5:]) else "healthy"


def _status_label(measurements: list[Measurement]) -> str:
    if not measurements:
        return "Waiting for data"
    recent = measurements[-20:]
    if all(not m.success for m in recent):
        return "Internet unavailable"
    if any(m.method == ProbeMethod.DNS and not m.success for m in recent):
        return "DNS failure"
    if any(not m.success for m in recent):
        return "Packet loss detected"
    if any(m.rtt_ms is not None and m.rtt_ms >= 150 for m in recent):
        return "High latency"
    return "Healthy"


def _measurement_activity(measurement: Measurement) -> str:
    target = measurement.target_name
    if measurement.success:
        if measurement.method == ProbeMethod.DNS and measurement.dns_duration_ms is not None:
            return f"DNS resolution completed in {measurement.dns_duration_ms:.0f} ms."
        if (
            measurement.method == ProbeMethod.HTTPS
            and measurement.https_response_duration_ms is not None
        ):
            return (
                f"Website connection test completed in "
                f"{measurement.https_response_duration_ms:.0f} ms."
            )
        if measurement.rtt_ms is not None:
            return f"{target} responded in {measurement.rtt_ms:.0f} ms."
        return f"{target} responded."
    reason = measurement.error_type or "no response"
    return f"{target} did not respond ({reason})."


def scheduled_speedtest_offsets(
    duration_seconds: float, interval_seconds: float
) -> tuple[float, ...]:
    """Return speed-test offsets for bounded sessions, including t=0 and excluding the end."""
    if duration_seconds <= 0 or interval_seconds <= 0:
        return ()
    offsets: list[float] = []
    next_due = 0.0
    while next_due < duration_seconds:
        offsets.append(next_due)
        next_due += interval_seconds
    return tuple(offsets)


def _next_speedtest_due(
    previous_due: float, current_monotonic: float, interval_seconds: float
) -> float:
    next_due = previous_due + interval_seconds
    while next_due <= current_monotonic:
        next_due += interval_seconds
    return next_due
