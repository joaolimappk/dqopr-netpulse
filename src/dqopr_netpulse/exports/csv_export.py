"""CSV and ZIP exports with stable column names."""

from __future__ import annotations

import csv
import json
from pathlib import Path
from sqlite3 import Row
from zipfile import ZIP_DEFLATED, ZipFile

from dqopr_netpulse.storage import NetPulseStore

DATA_DICTIONARY: dict[str, str] = {
    "timestamp_utc": "ISO 8601 UTC timestamp for the sample.",
    "target_name": "Human-readable target label.",
    "target_address": "Hostname or IP address tested.",
    "method": "Probe method: icmp, tcp, dns, https, speed, or route.",
    "success": "1 when the probe succeeded; 0 when it failed.",
    "rtt_ms": "Round-trip or probe duration in milliseconds.",
    "jitter_ms": "Absolute change from previous RTT for the same target and method.",
    "packet_loss_percent": "Loss percentage represented by this sample or rollup.",
    "consecutive_loss_count": "Consecutive failed probes for the same target and method.",
    "error_type": "Short technical error category, when available.",
    "error_message": "Probe error text, truncated to avoid excessive detail.",
}


def export_session_csv(store: NetPulseStore, session_id: str, output_dir: Path) -> list[Path]:
    output_dir.mkdir(parents=True, exist_ok=True)
    files = [
        _write_rows(output_dir / "raw_measurements.csv", store.list_measurements(session_id)),
        _write_filtered(
            output_dir / "packet_loss_samples.csv",
            store.list_measurements(session_id),
            lambda row: int(row["success"]) == 0 or (row["packet_loss_percent"] or 0) > 0,
        ),
        _write_filtered(
            output_dir / "dns_tests.csv",
            store.list_measurements(session_id),
            lambda row: row["method"] == "dns",
        ),
        _write_filtered(
            output_dir / "https_tests.csv",
            store.list_measurements(session_id),
            lambda row: row["method"] == "https",
        ),
        _write_rows(output_dir / "speed_tests.csv", store.list_speed_tests(session_id)),
        _write_rows(output_dir / "detected_incidents.csv", store.list_incidents(session_id)),
        _write_rows(output_dir / "manual_markers.csv", store.list_markers(session_id)),
        _write_summary(output_dir / "session_summary.csv", store, session_id),
        _write_dictionary(output_dir / "data_dictionary.csv"),
    ]
    return files


def export_session_zip(store: NetPulseStore, session_id: str, output_path: Path) -> Path:
    temp_dir = output_path.parent / f"{output_path.stem}-csv"
    files = export_session_csv(store, session_id, temp_dir)
    with ZipFile(output_path, "w", compression=ZIP_DEFLATED) as archive:
        for file in files:
            archive.write(file, arcname=file.name)
    return output_path


def _write_rows(path: Path, rows: list[Row]) -> Path:
    with path.open("w", newline="", encoding="utf-8") as handle:
        if not rows:
            handle.write("")
            return path
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        for row in rows:
            writer.writerow(dict(row))
    return path


def _write_filtered(path: Path, rows: list[Row], predicate: object) -> Path:
    filtered = [row for row in rows if callable(predicate) and predicate(row)]
    return _write_rows(path, filtered)


def _write_summary(path: Path, store: NetPulseStore, session_id: str) -> Path:
    measurements = store.list_measurements(session_id)
    incidents = store.list_incidents(session_id)
    speeds = store.list_speed_tests(session_id)
    markers = store.list_markers(session_id)
    successes = sum(1 for row in measurements if int(row["success"]) == 1)
    failures = len(measurements) - successes
    summary = {
        "session_id": session_id,
        "measurement_count": len(measurements),
        "successful_measurements": successes,
        "failed_measurements": failures,
        "packet_loss_percent": failures / len(measurements) * 100.0 if measurements else 0.0,
        "incident_count": len(incidents),
        "speed_test_count": len(speeds),
        "manual_marker_count": len(markers),
    }
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=list(summary.keys()))
        writer.writeheader()
        writer.writerow(summary)
    return path


def _write_dictionary(path: Path) -> Path:
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=["field", "description"])
        writer.writeheader()
        for field, description in DATA_DICTIONARY.items():
            writer.writerow({"field": field, "description": description})
        writer.writerow(
            {
                "field": "json_fields",
                "description": json.dumps(["affected_targets_json", "metrics_snapshot_json"]),
            }
        )
    return path
