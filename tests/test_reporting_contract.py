from __future__ import annotations

import inspect
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import pytest

from dqopr_netpulse.models import Measurement, ProbeMethod
from dqopr_netpulse.storage import NetPulseStore

csv_export = pytest.importorskip("dqopr_netpulse.exports.csv_export")
html_report = pytest.importorskip("dqopr_netpulse.reports.html_report")


BASE_TIME = datetime(2026, 1, 2, 3, 4, 5, tzinfo=UTC)


def _measurement(session_id: str = "report-session") -> Measurement:
    return Measurement(
        session_id=session_id,
        target_name='Cloudflare, "edge"',
        target_address="1.1.1.1",
        method=ProbeMethod.ICMP,
        sequence=1,
        success=False,
        timestamp_utc=BASE_TIME,
        packet_loss_percent=100.0,
        consecutive_loss_count=1,
        error_type="timeout",
        error_message='quoted "timeout", newline\nkept',
    )


def _store_with_measurement(tmp_path: Path, session_id: str = "report-session") -> NetPulseStore:
    store = NetPulseStore(tmp_path / "netpulse.sqlite3")
    store.create_session(session_id, "{}")
    store.add_measurement(_measurement(session_id))
    return store


def test_measurement_csv_export_uses_valid_csv_escaping(tmp_path: Path) -> None:
    store = _store_with_measurement(tmp_path)
    output_dir = tmp_path / "csv"

    try:
        paths = csv_export.export_session_csv(store, "report-session", output_dir)
        exported = {path.name: path for path in paths}
        csv_text = exported["raw_measurements.csv"].read_text(encoding="utf-8")
    finally:
        store.close()

    assert "target_name" in csv_text
    assert '"Cloudflare, ""edge"""' in csv_text
    assert '"quoted ""timeout"", newline' in csv_text


def test_html_report_handles_unknown_speeds_and_missing_data(
    tmp_path: Path,
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    store = _store_with_measurement(tmp_path)
    destination = tmp_path / "report.html"
    generate_report = html_report.generate_html_report
    signature = inspect.signature(generate_report)
    kwargs: dict[str, Any] = {}

    monkeypatch.setattr(html_report, "generate_session_graphs", lambda *_args: [])
    if "store" in signature.parameters:
        kwargs["store"] = store
    if "session_id" in signature.parameters:
        kwargs["session_id"] = "report-session"
    if "output_path" in signature.parameters:
        kwargs["output_path"] = destination
    elif "path" in signature.parameters:
        kwargs["path"] = destination
    if "contracted_download_mbps" in signature.parameters:
        kwargs["contracted_download_mbps"] = None
    if "contracted_upload_mbps" in signature.parameters:
        kwargs["contracted_upload_mbps"] = None

    try:
        result = generate_report(**kwargs)
        html = destination.read_text(encoding="utf-8") if destination.exists() else str(result)
    finally:
        store.close()

    assert "<html" in html.lower()
    assert "contracted speed" in html.lower()
    assert "not provided" in html.lower() or "unknown" in html.lower()
    assert "percentage-of-plan" in html.lower() or "percentage of plan" in html.lower()
