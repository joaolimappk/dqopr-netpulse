"""Self-contained ISP-oriented HTML report generation."""

from __future__ import annotations

import base64
import html
from pathlib import Path
from sqlite3 import Row

from dqopr_netpulse import __version__
from dqopr_netpulse.graphs import generate_session_graphs
from dqopr_netpulse.storage import NetPulseStore


def generate_html_report(
    store: NetPulseStore,
    session_id: str,
    output_path: Path,
    contracted_download_mbps: float | None = None,
    contracted_upload_mbps: float | None = None,
    private_mode: bool = True,
) -> Path:
    graph_dir = output_path.parent / f"{output_path.stem}-graphs"
    graphs = generate_session_graphs(store, session_id, graph_dir)
    measurements = store.list_measurements(session_id)
    incidents = store.list_incidents(session_id)
    speeds = store.list_speed_tests(session_id)
    markers = store.list_markers(session_id)
    failures = [row for row in measurements if int(row["success"]) == 0]
    packet_loss_percent = len(failures) / len(measurements) * 100.0 if measurements else 0.0
    speed_note = _speed_note(contracted_download_mbps, contracted_upload_mbps)

    overall = html.escape(_overall(incidents, packet_loss_percent))
    body = f"""<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>DQOPR NetPulse ISP Evidence Report</title>
  <style>
    body {{ font-family: Segoe UI, Arial, sans-serif; margin: 32px; color: #1f2937; }}
    h1, h2 {{ color: #111827; }}
    .summary {{ display: grid; grid-template-columns: repeat(auto-fit,
      minmax(180px, 1fr)); gap: 12px; }}
    .metric {{ border: 1px solid #d1d5db; border-radius: 6px; padding: 12px; }}
    table {{ border-collapse: collapse; width: 100%; margin: 12px 0 24px; }}
    th, td {{ border: 1px solid #d1d5db; padding: 8px; text-align: left; vertical-align: top; }}
    th {{ background: #f3f4f6; }}
    img {{ max-width: 100%; border: 1px solid #d1d5db; margin: 10px 0 18px; }}
    .note {{ background: #f8fafc; border-left: 4px solid #2563eb; padding: 10px 12px; }}
  </style>
</head>
<body>
  <h1>DQOPR NetPulse ISP Evidence Report</h1>
  <p class="note">This report explains probable causes conservatively. It does not claim
  certainty when the data only supports a probability.</p>
  <h2>Executive Summary</h2>
  <div class="summary">
    <div class="metric"><strong>Overall assessment</strong><br>{overall}</div>
    <div class="metric"><strong>Total incidents</strong><br>{len(incidents)}</div>
    <div class="metric"><strong>Packet loss</strong><br>{packet_loss_percent:.2f}%</div>
    <div class="metric"><strong>Measurements</strong><br>{len(measurements)}</div>
  </div>
  <h2>Contracted Speeds</h2>
  <p>{html.escape(speed_note)}</p>
  <h2>Local Versus External Comparison</h2>
  <p>{html.escape(_local_external_summary(incidents))}</p>
  <h2>Packet Loss, Latency, DNS, HTTPS, and Speed</h2>
  {_summary_table(measurements, speeds)}
  <h2>Incident Timeline</h2>
  {_incident_table(incidents)}
  <h2>Manual User Markers</h2>
  {_marker_table(markers)}
  <h2>Graphs</h2>
  {_graph_images(graphs)}
  <h2>Methodology</h2>
  <p>NetPulse records local gateway checks, multiple independent public-target checks,
  DNS resolution, HTTPS requests, and optional speed tests. Jitter is calculated as
  the absolute change between consecutive round-trip latency samples for the same
  target and method. Latency spikes are detected with both an absolute threshold
  and a recent-baseline-relative rule.</p>
  <h2>Threshold Definitions</h2>
  <p>Default thresholds: high latency at 150 ms, latency spike at three times recent
  median or at least 75 ms above baseline, high jitter at 40 ms, packet-loss warning
  at 5%, outage after repeated consecutive failures.</p>
  <h2>Limitations</h2>
  <p>ICMP can be deprioritized or blocked by some networks. A single failed target is
  not proof of a general outage. Speed tests consume bandwidth and represent only
  the conditions at the time they were run.</p>
  <h2>Privacy Notes</h2>
  <p>Private report mode: {html.escape(str(private_mode))}. NetPulse does not collect
  Wi-Fi passwords, browser history, personal files, packet contents, authentication
  tokens, telemetry, analytics, or crash uploads by default.</p>
  <h2>Software Version</h2>
  <p>DQOPR NetPulse {html.escape(__version__)}</p>
  <h2>Export-File Inventory</h2>
  <p>CSV exports include raw measurements, packet-loss samples, DNS tests, HTTPS tests,
  speed tests, incidents, manual markers, session summary, and a data dictionary.</p>
</body>
</html>
"""
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(body, encoding="utf-8")
    return output_path


def _overall(incidents: list[Row], packet_loss_percent: float) -> str:
    if not incidents and packet_loss_percent == 0:
        return "Healthy during the measured period"
    if packet_loss_percent >= 20:
        return "Significant instability detected"
    return "Degradation events detected"


def _speed_note(download: float | None, upload: float | None) -> str:
    if download is None and upload is None:
        return (
            "Contracted speed was not provided, so percentage-of-plan calculations are unavailable."
        )
    return (
        f"Contracted download: {download or 'unknown'} Mbps. "
        f"Contracted upload: {upload or 'unknown'} Mbps."
    )


def _local_external_summary(incidents: list[Row]) -> str:
    text = "No local-versus-external incidents were classified."
    for row in incidents:
        explanation = row["explanation"]
        if "local gateway remained responsive" in str(explanation).lower():
            return str(explanation)
        if "local gateway" in str(explanation).lower():
            text = str(explanation)
    return text


def _summary_table(measurements: list[Row], speeds: list[Row]) -> str:
    successful_latencies = [
        float(row["rtt_ms"])
        for row in measurements
        if row["rtt_ms"] is not None and int(row["success"]) == 1
    ]
    avg_latency = (
        sum(successful_latencies) / len(successful_latencies) if successful_latencies else None
    )
    latest_speed = speeds[-1] if speeds else None
    rows = [
        (
            "Average successful latency",
            f"{avg_latency:.2f} ms" if avg_latency is not None else "unavailable",
        ),
        ("Failed probes", str(sum(1 for row in measurements if int(row["success"]) == 0))),
        ("DNS samples", str(sum(1 for row in measurements if row["method"] == "dns"))),
        ("HTTPS samples", str(sum(1 for row in measurements if row["method"] == "https"))),
        (
            "Most recent speed test",
            "unavailable"
            if latest_speed is None
            else (
                f"{latest_speed['download_mbps'] or 'unknown'} down / "
                f"{latest_speed['upload_mbps'] or 'unknown'} up Mbps"
            ),
        ),
    ]
    return (
        "<table><tr><th>Metric</th><th>Value</th></tr>"
        + "".join(
            f"<tr><td>{html.escape(name)}</td><td>{html.escape(value)}</td></tr>"
            for name, value in rows
        )
        + "</table>"
    )


def _incident_table(incidents: list[Row]) -> str:
    if not incidents:
        return "<p>No incidents were detected.</p>"
    rows = []
    for row in incidents:
        rows.append(
            "<tr>"
            f"<td>{html.escape(str(row['start_time_utc']))}</td>"
            f"<td>{html.escape(str(row['end_time_utc'] or 'ongoing'))}</td>"
            f"<td>{html.escape(str(row['incident_type']))}</td>"
            f"<td>{html.escape(str(row['severity']))}</td>"
            f"<td>{html.escape(str(row['probable_fault_domain']))}</td>"
            f"<td>{html.escape(str(row['confidence']))}</td>"
            f"<td>{html.escape(str(row['explanation']))}</td>"
            "</tr>"
        )
    return (
        "<table><tr><th>Start</th><th>End</th><th>Type</th><th>Severity</th>"
        "<th>Assessment</th><th>Confidence</th><th>Explanation</th></tr>"
        + "".join(rows)
        + "</table>"
    )


def _marker_table(markers: list[Row]) -> str:
    if not markers:
        return "<p>No manual markers were recorded.</p>"
    rows = "".join(
        f"<tr><td>{html.escape(str(row['timestamp_utc']))}</td>"
        f"<td>{html.escape(str(row['note'] or ''))}</td></tr>"
        for row in markers
    )
    return "<table><tr><th>Time</th><th>Note</th></tr>" + rows + "</table>"


def _graph_images(graphs: list[Path]) -> str:
    if not graphs:
        return "<p>No graphable data was available.</p>"
    images = []
    for graph in graphs:
        encoded = base64.b64encode(graph.read_bytes()).decode("ascii")
        title = html.escape(graph.stem.replace("_", " ").title())
        images.append(f'<h3>{title}</h3><img src="data:image/png;base64,{encoded}">')
    return "\n".join(images)
