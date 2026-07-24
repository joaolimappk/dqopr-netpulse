"""Optional speed-test integration.

NetPulse does not run speed tests continuously. This wrapper is intentionally
optional so the monitor can run without a bundled provider-specific binary.
"""

from __future__ import annotations

import json
import shutil
import subprocess
import time
import urllib.error
import urllib.request
from collections.abc import Callable
from types import TracebackType
from typing import Protocol, cast

from dqopr_netpulse.models import SpeedTestResult

_DOWNLOAD_URL = "https://speed.cloudflare.com/__down?bytes=8000000"
_UPLOAD_URL = "https://speed.cloudflare.com/__up"
_DOWNLOAD_BYTES = 8_000_000
_UPLOAD_BYTES = 2_000_000
_HTTP_TIMEOUT_SECONDS = 30.0


class _SpeedResponse(Protocol):
    def __enter__(self) -> _SpeedResponse: ...

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        traceback: TracebackType | None,
    ) -> bool | None: ...

    def read(self, size: int = -1) -> bytes: ...


UrlOpen = Callable[[urllib.request.Request, float], _SpeedResponse]


def run_speedtest_cli(session_id: str) -> SpeedTestResult:
    """Run Ookla-compatible speedtest when installed, otherwise use a built-in fallback."""
    executable = shutil.which("speedtest")
    if executable is None:
        return run_builtin_http_speedtest(session_id)
    try:
        completed = subprocess.run(
            [executable, "--format=json"],
            check=False,
            capture_output=True,
            text=True,
            timeout=180,
        )
    except (OSError, subprocess.TimeoutExpired) as exc:
        return run_builtin_http_speedtest(
            session_id,
            fallback_reason=f"speedtest CLI failed: {exc}",
        )
    if completed.returncode != 0:
        return run_builtin_http_speedtest(
            session_id,
            fallback_reason=f"speedtest CLI exited with {completed.returncode}: "
            f"{completed.stderr[:240]}",
        )
    try:
        payload = json.loads(completed.stdout)
    except json.JSONDecodeError as exc:
        return run_builtin_http_speedtest(
            session_id,
            fallback_reason=f"speedtest CLI returned invalid JSON: {exc}",
        )
    return SpeedTestResult(
        session_id=session_id,
        download_mbps=_bandwidth_to_mbps(payload.get("download", {}).get("bandwidth")),
        upload_mbps=_bandwidth_to_mbps(payload.get("upload", {}).get("bandwidth")),
        latency_ms=payload.get("ping", {}).get("latency"),
        server_name=payload.get("server", {}).get("name"),
        server_location=payload.get("server", {}).get("location"),
        methodology="Ookla speedtest CLI JSON output",
    )


def run_builtin_http_speedtest(
    session_id: str,
    *,
    opener: UrlOpen | None = None,
    fallback_reason: str | None = None,
) -> SpeedTestResult:
    """Measure basic HTTP throughput without requiring an external speedtest binary."""
    opener = opener or _default_urlopen
    errors: list[str] = []
    download_mbps = _measure_download(opener, errors)
    upload_mbps = _measure_upload(opener, errors)
    success = download_mbps is not None or upload_mbps is not None
    reason_parts = [fallback_reason] if fallback_reason else ["speedtest CLI is not installed"]
    reason_parts.extend(errors)
    return SpeedTestResult(
        session_id=session_id,
        download_mbps=download_mbps,
        upload_mbps=upload_mbps,
        methodology=(
            "Built-in HTTPS throughput estimate using Cloudflare speed test endpoints. "
            "This is a quick-test fallback and may differ from provider speed-test results."
        ),
        success=success,
        error_message=None if success else "; ".join(reason_parts)[:500],
    )


def _bandwidth_to_mbps(bytes_per_second: object) -> float | None:
    if not isinstance(bytes_per_second, int | float):
        return None
    return float(bytes_per_second) * 8.0 / 1_000_000.0


def _measure_download(opener: UrlOpen, errors: list[str]) -> float | None:
    request = urllib.request.Request(
        _DOWNLOAD_URL,
        headers={"Cache-Control": "no-store", "User-Agent": "DQOPR-NetPulse"},
        method="GET",
    )
    started = time.monotonic()
    received = 0
    try:
        with opener(request, _HTTP_TIMEOUT_SECONDS) as response:
            while received < _DOWNLOAD_BYTES:
                chunk = response.read(256 * 1024)
                if not chunk:
                    break
                received += len(chunk)
    except (OSError, urllib.error.URLError) as exc:
        errors.append(f"download fallback failed: {exc}")
        return None
    return _throughput_mbps(received, time.monotonic() - started)


def _measure_upload(opener: UrlOpen, errors: list[str]) -> float | None:
    payload = b"0" * _UPLOAD_BYTES
    request = urllib.request.Request(
        _UPLOAD_URL,
        data=payload,
        headers={"Content-Type": "application/octet-stream", "User-Agent": "DQOPR-NetPulse"},
        method="POST",
    )
    started = time.monotonic()
    try:
        with opener(request, _HTTP_TIMEOUT_SECONDS) as response:
            response.read(1024)
    except (OSError, urllib.error.URLError) as exc:
        errors.append(f"upload fallback failed: {exc}")
        return None
    return _throughput_mbps(len(payload), time.monotonic() - started)


def _throughput_mbps(byte_count: int, elapsed_seconds: float) -> float | None:
    if byte_count <= 0 or elapsed_seconds <= 0:
        return None
    return byte_count * 8.0 / elapsed_seconds / 1_000_000.0


def _default_urlopen(request: urllib.request.Request, timeout: float) -> _SpeedResponse:
    return cast(_SpeedResponse, urllib.request.urlopen(request, timeout=timeout))


def speed_percentage(measured_mbps: float | None, contracted_mbps: float | None) -> float | None:
    if measured_mbps is None or contracted_mbps is None:
        return None
    if contracted_mbps <= 0:
        return None
    return measured_mbps / contracted_mbps * 100.0
