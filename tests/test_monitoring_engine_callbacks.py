from __future__ import annotations

import asyncio
from pathlib import Path

from dqopr_netpulse.configuration import AppConfig
from dqopr_netpulse.models import (
    Measurement,
    NetworkInterfaceSnapshot,
    ProbeMethod,
    SessionConfig,
    SpeedTestResult,
    Target,
)
from dqopr_netpulse.monitoring.engine import MonitoringSession, scheduled_speedtest_offsets
from dqopr_netpulse.storage import NetPulseStore


class FakeProbeRunner:
    async def icmp(
        self,
        session_id: str,
        target: Target,
        sequence: int,
        interface: NetworkInterfaceSnapshot,
    ) -> Measurement:
        return Measurement(
            session_id=session_id,
            target_name=target.name,
            target_address=target.host,
            method=ProbeMethod.ICMP,
            sequence=sequence,
            success=True,
            rtt_ms=2.0 if target.is_gateway else 32.0,
            jitter_ms=1.0 if target.is_gateway else 3.0,
            packet_loss_percent=0.0,
            active_interface_name=interface.name,
            interface_type=interface.interface_type,
            gateway_address=interface.gateway_ip,
        )

    async def tcp(
        self,
        session_id: str,
        target: Target,
        sequence: int,
        interface: NetworkInterfaceSnapshot,
    ) -> Measurement:
        return Measurement(
            session_id=session_id,
            target_name=target.name,
            target_address=target.host,
            method=ProbeMethod.TCP,
            sequence=sequence,
            success=True,
            tcp_connect_duration_ms=14.0,
            active_interface_name=interface.name,
            interface_type=interface.interface_type,
            gateway_address=interface.gateway_ip,
        )

    async def dns(
        self,
        session_id: str,
        target: Target,
        sequence: int,
        interface: NetworkInterfaceSnapshot,
    ) -> Measurement:
        return Measurement(
            session_id=session_id,
            target_name=target.name,
            target_address=target.host,
            method=ProbeMethod.DNS,
            sequence=sequence,
            success=True,
            dns_duration_ms=18.0,
            active_interface_name=interface.name,
            interface_type=interface.interface_type,
            gateway_address=interface.gateway_ip,
        )

    async def https(
        self,
        session_id: str,
        target: Target,
        sequence: int,
        interface: NetworkInterfaceSnapshot,
    ) -> Measurement:
        return Measurement(
            session_id=session_id,
            target_name=target.name,
            target_address=target.host,
            method=ProbeMethod.HTTPS,
            sequence=sequence,
            success=True,
            https_response_duration_ms=44.0,
            http_status_code=204,
            active_interface_name=interface.name,
            interface_type=interface.interface_type,
            gateway_address=interface.gateway_ip,
        )


def test_monitoring_session_emits_live_callbacks_and_persists_measurements(
    tmp_path: Path,
) -> None:
    session_config = SessionConfig(
        cycle_count=1,
        duration_seconds=None,
        latency_interval_seconds=0.01,
        targets=(Target(name="Public Target", host="203.0.113.10"),),
    )
    interface = NetworkInterfaceSnapshot(
        name="Ethernet",
        interface_type="ethernet",
        local_ip="192.0.2.20",
        gateway_ip="192.0.2.1",
        dns_servers=("192.0.2.1",),
    )
    app_config = AppConfig(data_dir=tmp_path, session=session_config)
    store = NetPulseStore(tmp_path / "netpulse.sqlite3")
    activities: list[str] = []
    states: list[str] = []
    measurements: list[Measurement] = []

    engine = MonitoringSession(
        app_config,
        store,
        probe_runner=FakeProbeRunner(),  # type: ignore[arg-type]
        interface_provider=lambda: interface,
        session_id="callback-session",
        activity_callback=activities.append,
        measurement_callback=measurements.append,
        state_callback=states.append,
    )

    session_id = asyncio.run(engine.run())

    stored = store.list_measurements(session_id)
    store.close()
    assert session_id == "callback-session"
    assert states == ["starting", "monitoring", "completed"]
    assert len(measurements) == 5
    assert len(stored) == len(measurements)
    assert stored[0]["target_name"] == "Local Gateway"
    assert any(row["target_name"] == "Public Target" for row in stored)
    assert any(row["method"] == ProbeMethod.DNS for row in stored)
    assert any(row["method"] == ProbeMethod.HTTPS for row in stored)
    assert any("Pinging local gateway" in activity for activity in activities)
    assert any("Measurements saved" in activity for activity in activities)


def test_ten_minute_monitoring_schedules_two_five_minute_speed_tests() -> None:
    assert scheduled_speedtest_offsets(10 * 60, 5 * 60) == (0.0, 300.0)


def test_monitoring_session_runs_and_persists_scheduled_speed_test(tmp_path: Path) -> None:
    session_config = SessionConfig(
        cycle_count=1,
        duration_seconds=None,
        latency_interval_seconds=0.01,
        speedtest_enabled=True,
        speedtest_interval_seconds=300.0,
        targets=(Target(name="Public Target", host="203.0.113.10"),),
    )
    interface = NetworkInterfaceSnapshot(
        name="Ethernet",
        interface_type="ethernet",
        local_ip="192.0.2.20",
        gateway_ip="192.0.2.1",
        dns_servers=("192.0.2.1",),
    )
    app_config = AppConfig(data_dir=tmp_path, session=session_config)
    store = NetPulseStore(tmp_path / "netpulse.sqlite3")
    speed_results: list[SpeedTestResult] = []

    engine = MonitoringSession(
        app_config,
        store,
        probe_runner=FakeProbeRunner(),  # type: ignore[arg-type]
        interface_provider=lambda: interface,
        session_id="scheduled-speed-session",
        speedtest_runner=lambda session_id: SpeedTestResult(
            session_id=session_id,
            download_mbps=120.0,
            upload_mbps=30.0,
            methodology="test speed runner",
        ),
        speedtest_callback=speed_results.append,
    )

    session_id = asyncio.run(engine.run())

    stored_speed_tests = store.list_speed_tests(session_id)
    store.close()
    assert len(speed_results) == 1
    assert len(stored_speed_tests) == 1
    assert stored_speed_tests[0]["download_mbps"] == 120.0
    assert stored_speed_tests[0]["upload_mbps"] == 30.0
