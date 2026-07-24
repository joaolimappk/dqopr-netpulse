"""Compatibility reporting API for exports and HTML reports."""

from __future__ import annotations

import csv
import html
from pathlib import Path

from dqopr_netpulse.models import (
    Incident,
    ManualMarker,
    Measurement,
    SessionConfig,
    SpeedTestResult,
)


def export_measurements_csv(measurements: list[Measurement], path: Path) -> Path:
    """Write measurement dataclasses to CSV using normal CSV escaping."""
    path.parent.mkdir(parents=True, exist_ok=True)
    rows = [_measurement_row(measurement) for measurement in measurements]
    with path.open("w", newline="", encoding="utf-8") as handle:
        if not rows:
            handle.write("")
            return path
        writer = csv.DictWriter(handle, fieldnames=list(rows[0].keys()))
        writer.writeheader()
        writer.writerows(rows)
    return path


def write_measurements_csv(measurements: list[Measurement], destination: Path) -> Path:
    return export_measurements_csv(measurements, destination)


def render_html_report(
    *,
    output_path: Path,
    session_id: str,
    measurements: list[Measurement],
    speed_tests: list[SpeedTestResult],
    incidents: list[Incident],
    markers: list[ManualMarker],
    config: SessionConfig,
) -> Path:
    """Render a minimal self-contained report from in-memory objects."""
    speed_note = (
        "Contracted speed was not provided, so percentage-of-plan calculations are unavailable."
        if config.contracted_download_mbps is None and config.contracted_upload_mbps is None
        else (
            f"Contracted download: {config.contracted_download_mbps or 'unknown'} Mbps. "
            f"Contracted upload: {config.contracted_upload_mbps or 'unknown'} Mbps."
        )
    )
    body = f"""<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>DQOPR NetPulse Report</title></head>
<body>
<h1>DQOPR NetPulse ISP Evidence Report</h1>
<p>Session: {html.escape(session_id)}</p>
<h2>Executive Summary</h2>
<p>Measurements: {len(measurements)}. Incidents: {len(incidents)}.
Manual markers: {len(markers)}.</p>
<h2>Contracted Speed</h2>
<p>{html.escape(speed_note)}</p>
<h2>Methodology</h2>
<p>NetPulse reports measured latency, packet loss, DNS, HTTPS, and optional
speed tests conservatively.</p>
<h2>Speed Tests</h2>
<p>{len(speed_tests)} speed tests recorded.</p>
</body>
</html>
"""
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(body, encoding="utf-8")
    return output_path


def generate_html_report(**kwargs: object) -> Path:
    return render_html_report(**kwargs)  # type: ignore[arg-type]


def _measurement_row(measurement: Measurement) -> dict[str, object]:
    row: dict[str, object] = {}
    for key, value in measurement.__dict__.items():
        if hasattr(value, "value"):
            row[key] = value.value
        else:
            row[key] = value
    return row
