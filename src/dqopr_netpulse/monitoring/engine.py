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
    Target,
)
from dqopr_netpulse.networking import default_targets_with_gateway, detect_active_interface
from dqopr_netpulse.probes import ProbeRunner
from dqopr_netpulse.storage import NetPulseStore

LOGGER = logging.getLogger(__name__)
InterfaceProvider = Callable[[], NetworkInterfaceSnapshot]


class MonitoringSession:
    """Coordinates probes, persistence, marker capture, and basic incident detection."""

    def __init__(
        self,
        app_config: AppConfig,
        store: NetPulseStore,
        probe_runner: ProbeRunner | None = None,
        interface_provider: InterfaceProvider = detect_active_interface,
        session_id: str | None = None,
    ) -> None:
        validate_session_config(app_config.session)
        self.app_config = app_config
        self.store = store
        self.probe_runner = probe_runner or ProbeRunner()
        self.interface_provider = interface_provider
        self.session_id = session_id or str(uuid4())
        self._stop_event = asyncio.Event()
        self._pause_event = asyncio.Event()
        self._pause_event.set()
        self._sequence = 0
        self._recent_measurements: list[Measurement] = []

    async def run(self) -> str:
        """Run the monitoring session until duration, cycle count, or stop."""
        config = self.app_config.session
        config_json = json.dumps(asdict(config), default=str, sort_keys=True)
        self.store.create_session(self.session_id, config_json)
        started_monotonic = time.monotonic()
        cycles = 0
        targets = default_targets_with_gateway(config.targets)
        LOGGER.info("Starting monitoring session %s with %s targets", self.session_id, len(targets))
        try:
            while not self._stop_event.is_set():
                await self._pause_event.wait()
                if (
                    config.duration_seconds is not None
                    and time.monotonic() - started_monotonic >= config.duration_seconds
                ):
                    break
                if config.cycle_count is not None and cycles >= config.cycle_count:
                    break
                await self._run_cycle(targets)
                cycles += 1
                await asyncio.sleep(config.latency_interval_seconds)
        except Exception:
            self.store.finish_session(self.session_id, "failed")
            LOGGER.exception("Monitoring session failed")
            raise
        else:
            self.store.finish_session(self.session_id, "completed")
        return self.session_id

    def stop(self) -> None:
        self._stop_event.set()
        self._pause_event.set()

    def pause(self) -> None:
        self._pause_event.clear()

    def resume(self) -> None:
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
        return {
            "status": _status_label(recent),
            "current_latency_ms": avg_latency,
            "current_jitter_ms": avg_jitter,
            "recent_packet_loss_percent": len(failures) / len(recent) * 100.0 if recent else 0.0,
            "gateway_status": "degraded" if gateway_failures else "healthy",
            "internet_status": "degraded" if external_failures else "healthy",
            "dns_status": _method_status(recent, ProbeMethod.DNS),
            "https_status": _method_status(recent, ProbeMethod.HTTPS),
        }

    async def _run_cycle(self, targets: tuple[Target, ...]) -> None:
        self._sequence += 1
        interface = self.interface_provider()
        enabled_targets = [target for target in targets if target.enabled]
        tasks: list[Awaitable[Measurement]] = [
            self.probe_runner.icmp(self.session_id, target, self._sequence, interface)
            for target in enabled_targets
        ]
        tasks.extend(
            self.probe_runner.tcp(self.session_id, target, self._sequence, interface)
            for target in enabled_targets
            if not target.is_gateway
        )
        if enabled_targets:
            tasks.append(
                self.probe_runner.dns(
                    self.session_id, enabled_targets[0], self._sequence, interface
                )
            )
            tasks.append(
                self.probe_runner.https(
                    self.session_id, enabled_targets[0], self._sequence, interface
                )
            )
        measurements = await asyncio.gather(*tasks)
        for measurement in measurements:
            self.store.add_measurement(measurement)
            self._recent_measurements.append(measurement)
        self._recent_measurements = self._recent_measurements[-500:]
        for incident in classify_measurements(
            list(measurements),
            session_id=self.session_id,
            thresholds=self.app_config.thresholds,
        ):
            self.store.add_incident(incident)


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
