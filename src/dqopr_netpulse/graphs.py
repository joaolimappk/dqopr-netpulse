"""PNG graph generation for monitoring sessions."""

from __future__ import annotations

from datetime import datetime
from pathlib import Path
from sqlite3 import Row

from dqopr_netpulse.storage import NetPulseStore


def generate_session_graphs(store: NetPulseStore, session_id: str, output_dir: Path) -> list[Path]:
    output_dir.mkdir(parents=True, exist_ok=True)
    measurements = store.list_measurements(session_id)
    speed_tests = store.list_speed_tests(session_id)
    graphs = [
        _plot_measurement(
            measurements, "rtt_ms", "Latency over time", "Latency (ms)", output_dir / "latency.png"
        ),
        _plot_measurement(
            measurements, "jitter_ms", "Jitter over time", "Jitter (ms)", output_dir / "jitter.png"
        ),
        _plot_measurement(
            measurements,
            "packet_loss_percent",
            "Packet loss over time",
            "Packet loss (%)",
            output_dir / "packet_loss.png",
        ),
        _plot_measurement(
            measurements,
            "consecutive_loss_count",
            "Consecutive failures over time",
            "Failures",
            output_dir / "consecutive_failures.png",
        ),
        _plot_measurement(
            measurements,
            "dns_duration_ms",
            "DNS response time",
            "DNS response (ms)",
            output_dir / "dns.png",
        ),
        _plot_measurement(
            measurements,
            "https_response_duration_ms",
            "HTTPS response time",
            "HTTPS response (ms)",
            output_dir / "https.png",
        ),
        _plot_measurement(
            measurements,
            "wifi_signal_percent",
            "Wi-Fi signal over time",
            "Signal (%)",
            output_dir / "wifi_signal.png",
        ),
        _plot_speed(
            speed_tests,
            "download_mbps",
            "Download speed over time",
            "Mbps",
            output_dir / "download_speed.png",
        ),
        _plot_speed(
            speed_tests,
            "upload_mbps",
            "Upload speed over time",
            "Mbps",
            output_dir / "upload_speed.png",
        ),
    ]
    return [path for path in graphs if path is not None]


def _plot_measurement(
    rows: list[Row], column: str, title: str, ylabel: str, path: Path
) -> Path | None:
    series: dict[str, list[tuple[datetime, float]]] = {}
    for row in rows:
        value = row[column]
        if value is None:
            continue
        series.setdefault(str(row["target_name"]), []).append(
            (_parse_time(str(row["timestamp_utc"])), float(value))
        )
    return _plot_series(series, title, ylabel, path)


def _plot_speed(rows: list[Row], column: str, title: str, ylabel: str, path: Path) -> Path | None:
    values = [
        (_parse_time(str(row["timestamp_utc"])), float(row[column]))
        for row in rows
        if row[column] is not None
    ]
    return _plot_series({"Speed test": values}, title, ylabel, path)


def _plot_series(
    series: dict[str, list[tuple[datetime, float]]], title: str, ylabel: str, path: Path
) -> Path | None:
    populated = {name: values for name, values in series.items() if values}
    if not populated:
        return None
    import matplotlib

    matplotlib.use("Agg")
    from matplotlib import pyplot as plt

    fig, ax = plt.subplots(figsize=(10, 4.8), layout="constrained")
    for label, values in populated.items():
        values.sort(key=lambda item: item[0])
        x_values = [item[0].timestamp() for item in values]
        ax.plot(
            x_values,
            [item[1] for item in values],
            marker=".",
            linewidth=1,
            label=label,
        )
    ax.set_title(title)
    ax.set_ylabel(ylabel)
    ax.set_xlabel("Time")
    ax.grid(True, alpha=0.3)
    if len(populated) > 1:
        ax.legend(loc="best")
    fig.autofmt_xdate()
    fig.savefig(path, dpi=140)
    plt.close(fig)
    return path


def _parse_time(value: str) -> datetime:
    return datetime.fromisoformat(value.replace("Z", "+00:00"))
