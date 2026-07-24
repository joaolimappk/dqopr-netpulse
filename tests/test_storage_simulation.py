from __future__ import annotations

import csv
import io
import json
from datetime import UTC, datetime, timedelta
from pathlib import Path

from dqopr_netpulse.models import (
    Confidence,
    FaultDomain,
    Incident,
    IncidentType,
    ManualMarker,
    Measurement,
    ProbeMethod,
    Severity,
    SpeedTestResult,
)
from dqopr_netpulse.storage import NetPulseStore

BASE_TIME = datetime(2026, 1, 2, 3, 4, 5, tzinfo=UTC)


def _measurement(
    session_id: str,
    sequence: int,
    target_name: str = "Cloudflare",
    target_address: str = "1.1.1.1",
    method: ProbeMethod = ProbeMethod.ICMP,
    success: bool = True,
) -> Measurement:
    return Measurement(
        session_id=session_id,
        target_name=target_name,
        target_address=target_address,
        method=method,
        sequence=sequence,
        success=success,
        timestamp_utc=BASE_TIME + timedelta(seconds=sequence),
        rtt_ms=17.5 if success else None,
        jitter_ms=2.0 if success else None,
        packet_loss_percent=0.0 if success else 100.0,
        consecutive_loss_count=0 if success else 1,
        gateway_address="192.0.2.1",
        public_ip_masked="198.51.100.x",
        error_type=None if success else "timeout",
        error_message=None if success else 'timeout, quoted "detail"\nnext line',
    )


def test_storage_reopen_preserves_simulated_session_data(tmp_path: Path) -> None:
    db_path = tmp_path / "netpulse.sqlite3"
    session_id = "simulation-session"

    store = NetPulseStore(db_path)
    store.create_session(session_id, json.dumps({"mode": "simulation"}))
    first_measurement_id = store.add_measurement(
        _measurement(
            session_id=session_id,
            sequence=1,
            target_name='Cloudflare, "edge"\nresolver',
        )
    )
    failed_measurement_id = store.add_measurement(
        _measurement(session_id=session_id, sequence=2, success=False)
    )
    store.add_speed_test(
        SpeedTestResult(
            session_id=session_id,
            timestamp_utc=BASE_TIME + timedelta(minutes=1),
            download_mbps=312.4,
            upload_mbps=19.8,
            latency_ms=21.0,
            server_name='Speed Server, "North"',
            server_location="Test City\nRegion",
        )
    )
    incident = Incident(
        session_id=session_id,
        incident_type=IncidentType.EXTERNAL_PACKET_LOSS,
        start_time_utc=BASE_TIME + timedelta(seconds=2),
        end_time_utc=BASE_TIME + timedelta(seconds=5),
        severity=Severity.MEDIUM,
        affected_tests=(ProbeMethod.ICMP.value,),
        affected_targets=("Cloudflare", "Google"),
        probable_fault_domain=FaultDomain.ISP_OR_UPSTREAM,
        confidence=Confidence.MODERATE,
        explanation="Gateway stayed healthy while external targets lost packets.",
        packet_loss_percent=42.0,
        consecutive_failures=3,
        local_gateway_status="healthy",
        external_target_status="degraded",
        supporting_measurement_ids=(first_measurement_id, failed_measurement_id),
    )
    incident_id = store.add_incident(incident)
    store.add_marker(
        ManualMarker(
            session_id=session_id,
            timestamp_utc=BASE_TIME + timedelta(seconds=3),
            note='User note with comma, quote " and newline\nkept intact',
            metrics_snapshot={"latency_ms": 1200.5, "status": "bad"},
            active_interface_name="Wi-Fi",
            wifi_signal_percent=71,
            gateway_status="healthy",
            public_target_status="degraded",
        )
    )
    store.finish_session(session_id)
    store.close()

    reopened = NetPulseStore(db_path)
    measurements = reopened.list_measurements(session_id)
    speed_tests = reopened.list_speed_tests(session_id)
    incidents = reopened.list_incidents(session_id)
    markers = reopened.list_markers(session_id)
    reopened.close()

    assert [row["sequence"] for row in measurements] == [1, 2]
    assert measurements[0]["target_name"] == 'Cloudflare, "edge"\nresolver'
    assert measurements[1]["success"] == 0
    assert measurements[1]["error_message"] == 'timeout, quoted "detail"\nnext line'
    assert speed_tests[0]["server_name"] == 'Speed Server, "North"'
    assert speed_tests[0]["server_location"] == "Test City\nRegion"
    assert incidents[0]["id"] == incident_id
    assert incidents[0]["affected_targets_json"] == '["Cloudflare","Google"]'
    expected_supporting_ids = f"[{first_measurement_id},{failed_measurement_id}]"
    assert incidents[0]["supporting_measurement_ids_json"] == expected_supporting_ids
    assert markers[0]["note"] == 'User note with comma, quote " and newline\nkept intact'
    assert markers[0]["metrics_snapshot_json"] == '{"latency_ms":1200.5,"status":"bad"}'


def test_csv_module_escapes_stored_rows_with_stable_headers(tmp_path: Path) -> None:
    db_path = tmp_path / "netpulse.sqlite3"
    session_id = "csv-session"
    store = NetPulseStore(db_path)
    store.create_session(session_id, "{}")
    store.add_measurement(
        _measurement(
            session_id=session_id,
            sequence=1,
            target_name='Target, with "quotes"',
            success=False,
        )
    )

    row = store.list_measurements(session_id)[0]
    store.close()
    output = io.StringIO()
    writer = csv.DictWriter(output, fieldnames=["target_name", "error_message"])
    writer.writeheader()
    writer.writerow({field: row[field] for field in writer.fieldnames})

    csv_text = output.getvalue()
    parsed = list(csv.DictReader(io.StringIO(csv_text)))

    assert '"Target, with ""quotes"""' in csv_text
    assert '"timeout, quoted ""detail""' in csv_text
    assert parsed == [
        {
            "target_name": 'Target, with "quotes"',
            "error_message": 'timeout, quoted "detail"\nnext line',
        }
    ]
