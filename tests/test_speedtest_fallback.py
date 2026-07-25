from __future__ import annotations

import time
import urllib.request
from types import TracebackType

from dqopr_netpulse.speedtest import run_builtin_http_speedtest


class FakeResponse:
    def __init__(self, payload: bytes) -> None:
        self._payload = payload
        self._offset = 0

    def __enter__(self) -> FakeResponse:
        return self

    def __exit__(
        self,
        exc_type: type[BaseException] | None,
        exc: BaseException | None,
        traceback: TracebackType | None,
    ) -> bool | None:
        return None

    def read(self, size: int = -1) -> bytes:
        if size < 0:
            size = len(self._payload) - self._offset
        chunk = self._payload[self._offset : self._offset + size]
        self._offset += len(chunk)
        if chunk:
            time.sleep(0.001)
        return chunk


def test_builtin_http_speedtest_measures_download_and_upload() -> None:
    def opener(request: urllib.request.Request, _timeout: float) -> FakeResponse:
        if request.data is not None:
            return FakeResponse(b"ok")
        return FakeResponse(b"x" * 8_000_000)

    result = run_builtin_http_speedtest("speed-session", opener=opener)

    assert result.success
    assert result.download_mbps is not None
    assert result.download_mbps > 0
    assert result.upload_mbps is not None
    assert result.upload_mbps > 0
    assert "Built-in HTTPS throughput estimate" in result.methodology
