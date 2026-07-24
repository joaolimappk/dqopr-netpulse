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
from dqopr_netpulse.quick_test import QuickTestRunner, summarize_quick_test
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
            rtt_ms=4.0 if target.is_gateway else 40.0,
            jitter_ms=1.0 if target.is_gateway else 5.0,
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
            tcp_connect_duration_ms=12.0,
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
            dns_duration_ms=17.0,
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
            https_response_duration_ms=42.0,
            http_status_code=204,
            active_interface_name=interface.name,
            interface_type=interface.interface_type,
            gateway_address=interface.gateway_ip,
        )


def test_quick_test_runs_one_cycle_speed_test_and_persists_results(tmp_path: Path) -> None:
    store = NetPulseStore(tmp_path / "netpulse.sqlite3")
    config = AppConfig(
        data_dir=tmp_path,
        session=SessionConfig(
            contracted_download_mbps=200.0,
            contracted_upload_mbps=40.0,
            targets=(Target(name="Public Target", host="203.0.113.20"),),
        ),
    )
    interface = NetworkInterfaceSnapshot(
        name="Ethernet",
        interface_type="ethernet",
        local_ip="192.0.2.22",
        gateway_ip="192.0.2.1",
    )
    activities: list[str] = []
    progress: list[tuple[int, int, str]] = []
    measurements: list[Measurement] = []

    runner = QuickTestRunner(
        config,
        store,
        activity_callback=activities.append,
        progress_callback=lambda step, total, label: progress.append((step, total, label)),
        measurement_callback=measurements.append,
        speedtest_runner=lambda session_id: SpeedTestResult(
            session_id=session_id,
            download_mbps=150.0,
            upload_mbps=20.0,
            latency_ms=25.0,
            server_name="Test Server",
            server_location="Test City",
        ),
        probe_runner=FakeProbeRunner(),  # type: ignore[arg-type]
        interface_provider=lambda: interface,
    )

    summary = asyncio.run(runner.run())

    stored_measurements = store.list_measurements(summary.session_id)
    stored_speeds = store.list_speed_tests(summary.session_id)
    store.close()
    assert summary.completed
    assert not summary.cancelled
    assert summary.download_mbps == 150.0
    assert summary.upload_mbps == 20.0
    assert summary.download_percent == 75.0
    assert summary.upload_percent == 50.0
    assert len(measurements) == 5
    assert len(stored_measurements) == 5
    assert len(stored_speeds) == 1
    assert progress[0] == (1, 10, "Detecting connection")
    assert progress[-1] == (10, 10, "Saving report")
    assert any("Measuring download speed" in activity for activity in activities)
    assert any("Quick test completed" in activity for activity in activities)


def test_quick_test_summary_handles_unknown_contracted_speeds() -> None:
    config = AppConfig(
        data_dir=Path("."),
        session=SessionConfig(
            contracted_download_mbps=None,
            contracted_upload_mbps=None,
        ),
    )
    speed = SpeedTestResult(session_id="summary", download_mbps=80.0, upload_mbps=12.0)

    summary = summarize_quick_test(
        "summary",
        [],
        speed,
        config,
        duration_seconds=1.0,
    )

    assert summary.download_mbps == 80.0
    assert summary.upload_mbps == 12.0
    assert summary.download_percent is None
    assert summary.upload_percent is None
