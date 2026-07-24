"""Optional speed-test integration.

NetPulse does not run speed tests continuously. This wrapper is intentionally
optional so the monitor can run without a bundled provider-specific binary.
"""

from __future__ import annotations

import json
import shutil
import subprocess

from dqopr_netpulse.models import SpeedTestResult


def run_speedtest_cli(session_id: str) -> SpeedTestResult:
    """Run Ookla-compatible `speedtest --format=json` when installed."""
    executable = shutil.which("speedtest")
    if executable is None:
        return SpeedTestResult(
            session_id=session_id,
            success=False,
            error_message="speedtest CLI is not installed; speed testing was skipped.",
        )
    try:
        completed = subprocess.run(
            [executable, "--format=json"],
            check=False,
            capture_output=True,
            text=True,
            timeout=180,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        return SpeedTestResult(session_id=session_id, success=False, error_message=str(exc))
    if completed.returncode != 0:
        return SpeedTestResult(
            session_id=session_id, success=False, error_message=completed.stderr[:500]
        )
    try:
        payload = json.loads(completed.stdout)
    except json.JSONDecodeError as exc:
        return SpeedTestResult(session_id=session_id, success=False, error_message=str(exc))
    return SpeedTestResult(
        session_id=session_id,
        download_mbps=_bandwidth_to_mbps(payload.get("download", {}).get("bandwidth")),
        upload_mbps=_bandwidth_to_mbps(payload.get("upload", {}).get("bandwidth")),
        latency_ms=payload.get("ping", {}).get("latency"),
        server_name=payload.get("server", {}).get("name"),
        server_location=payload.get("server", {}).get("location"),
        methodology="Ookla speedtest CLI JSON output",
    )


def _bandwidth_to_mbps(bytes_per_second: object) -> float | None:
    if not isinstance(bytes_per_second, int | float):
        return None
    return float(bytes_per_second) * 8.0 / 1_000_000.0


def speed_percentage(measured_mbps: float | None, contracted_mbps: float | None) -> float | None:
    if measured_mbps is None or contracted_mbps is None:
        return None
    if contracted_mbps <= 0:
        return None
    return measured_mbps / contracted_mbps * 100.0
