"""Network probes used by the monitoring engine."""

from __future__ import annotations

import asyncio
import platform
import ssl
import statistics
import time
from collections import defaultdict
from collections.abc import Awaitable, Callable

from dqopr_netpulse.models import Measurement, NetworkInterfaceSnapshot, ProbeMethod, Target

ProbeFunc = Callable[[str, Target, int, NetworkInterfaceSnapshot], Awaitable[Measurement]]


class ProbeRunner:
    """Runs probe methods and keeps per-target rolling loss/jitter state."""

    def __init__(self, timeout_seconds: float = 2.0) -> None:
        self.timeout_seconds = timeout_seconds
        self._previous_latency: dict[tuple[str, ProbeMethod], float] = {}
        self._consecutive_losses: dict[tuple[str, ProbeMethod], int] = defaultdict(int)

    async def icmp(
        self,
        session_id: str,
        target: Target,
        sequence: int,
        interface: NetworkInterfaceSnapshot,
    ) -> Measurement:
        method = ProbeMethod.ICMP
        started = time.monotonic()
        args = _ping_args(target.host, self.timeout_seconds)
        try:
            process = await asyncio.create_subprocess_exec(
                *args,
                stdout=asyncio.subprocess.PIPE,
                stderr=asyncio.subprocess.PIPE,
            )
            stdout, stderr = await asyncio.wait_for(
                process.communicate(), timeout=self.timeout_seconds + 1.0
            )
        except (OSError, TimeoutError) as exc:
            return self._failure(
                session_id, target, method, sequence, interface, type(exc).__name__, str(exc)
            )

        elapsed_ms = (time.monotonic() - started) * 1000.0
        output = (stdout + stderr).decode(errors="replace")
        success = process.returncode == 0
        rtt_ms = _parse_ping_latency(output) if success else None
        return self._measurement(
            session_id=session_id,
            target=target,
            method=method,
            sequence=sequence,
            success=success,
            interface=interface,
            rtt_ms=rtt_ms if rtt_ms is not None else (elapsed_ms if success else None),
            error_type=None if success else "icmp_failed",
            error_message=None if success else output.strip()[:500],
        )

    async def tcp(
        self,
        session_id: str,
        target: Target,
        sequence: int,
        interface: NetworkInterfaceSnapshot,
    ) -> Measurement:
        started = time.monotonic()
        try:
            reader, writer = await asyncio.wait_for(
                asyncio.open_connection(target.host, target.tcp_port),
                timeout=self.timeout_seconds,
            )
            writer.close()
            await writer.wait_closed()
        except (OSError, TimeoutError) as exc:
            return self._failure(
                session_id,
                target,
                ProbeMethod.TCP,
                sequence,
                interface,
                type(exc).__name__,
                str(exc),
            )
        elapsed_ms = (time.monotonic() - started) * 1000.0
        return self._measurement(
            session_id=session_id,
            target=target,
            method=ProbeMethod.TCP,
            sequence=sequence,
            success=True,
            interface=interface,
            rtt_ms=elapsed_ms,
            tcp_connect_duration_ms=elapsed_ms,
        )

    async def dns(
        self,
        session_id: str,
        target: Target,
        sequence: int,
        interface: NetworkInterfaceSnapshot,
        hostname: str = "example.com",
    ) -> Measurement:
        started = time.monotonic()
        loop = asyncio.get_running_loop()
        try:
            await asyncio.wait_for(loop.getaddrinfo(hostname, 443), timeout=self.timeout_seconds)
        except (OSError, TimeoutError) as exc:
            return self._failure(
                session_id,
                target,
                ProbeMethod.DNS,
                sequence,
                interface,
                type(exc).__name__,
                str(exc),
            )
        elapsed_ms = (time.monotonic() - started) * 1000.0
        return self._measurement(
            session_id=session_id,
            target=target,
            method=ProbeMethod.DNS,
            sequence=sequence,
            success=True,
            interface=interface,
            dns_duration_ms=elapsed_ms,
            rtt_ms=elapsed_ms,
        )

    async def https(
        self,
        session_id: str,
        target: Target,
        sequence: int,
        interface: NetworkInterfaceSnapshot,
    ) -> Measurement:
        started = time.monotonic()
        context = ssl.create_default_context()
        host = target.host
        try:
            reader, writer = await asyncio.wait_for(
                asyncio.open_connection(host, 443, ssl=context, server_hostname=host),
                timeout=self.timeout_seconds,
            )
            request = f"HEAD / HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n"
            writer.write(request.encode("ascii"))
            await writer.drain()
            response = await asyncio.wait_for(reader.readline(), timeout=self.timeout_seconds)
            writer.close()
            await writer.wait_closed()
        except (OSError, TimeoutError, ssl.SSLError) as exc:
            return self._failure(
                session_id,
                target,
                ProbeMethod.HTTPS,
                sequence,
                interface,
                type(exc).__name__,
                str(exc),
            )
        elapsed_ms = (time.monotonic() - started) * 1000.0
        status = _parse_http_status(response.decode(errors="replace"))
        return self._measurement(
            session_id=session_id,
            target=target,
            method=ProbeMethod.HTTPS,
            sequence=sequence,
            success=status is not None and 100 <= status < 600,
            interface=interface,
            rtt_ms=elapsed_ms,
            https_response_duration_ms=elapsed_ms,
            http_status_code=status,
        )

    def _failure(
        self,
        session_id: str,
        target: Target,
        method: ProbeMethod,
        sequence: int,
        interface: NetworkInterfaceSnapshot,
        error_type: str,
        error_message: str,
    ) -> Measurement:
        return self._measurement(
            session_id=session_id,
            target=target,
            method=method,
            sequence=sequence,
            success=False,
            interface=interface,
            error_type=error_type,
            error_message=error_message[:500],
        )

    def _measurement(
        self,
        session_id: str,
        target: Target,
        method: ProbeMethod,
        sequence: int,
        success: bool,
        interface: NetworkInterfaceSnapshot,
        rtt_ms: float | None = None,
        tcp_connect_duration_ms: float | None = None,
        dns_duration_ms: float | None = None,
        https_response_duration_ms: float | None = None,
        http_status_code: int | None = None,
        error_type: str | None = None,
        error_message: str | None = None,
    ) -> Measurement:
        key = (target.name, method)
        previous = self._previous_latency.get(key)
        jitter_ms = abs(rtt_ms - previous) if rtt_ms is not None and previous is not None else None
        if success:
            self._consecutive_losses[key] = 0
            if rtt_ms is not None:
                self._previous_latency[key] = rtt_ms
        else:
            self._consecutive_losses[key] += 1
        consecutive = self._consecutive_losses[key]
        return Measurement(
            session_id=session_id,
            target_name=target.name,
            target_address=target.host,
            method=method,
            sequence=sequence,
            success=success,
            rtt_ms=rtt_ms,
            min_latency_ms=rtt_ms,
            max_latency_ms=rtt_ms,
            avg_latency_ms=rtt_ms,
            median_latency_ms=rtt_ms,
            jitter_ms=jitter_ms,
            packet_loss_percent=100.0 if not success else 0.0,
            consecutive_loss_count=consecutive,
            timeout_ms=self.timeout_seconds * 1000.0,
            dns_duration_ms=dns_duration_ms,
            tcp_connect_duration_ms=tcp_connect_duration_ms,
            https_response_duration_ms=https_response_duration_ms,
            http_status_code=http_status_code,
            active_interface_name=interface.name,
            interface_type=interface.interface_type,
            wifi_signal_percent=interface.wifi_signal_percent,
            gateway_address=interface.gateway_ip,
            vpn_detected=interface.vpn_detected,
            error_type=error_type,
            error_message=error_message,
        )


def calculate_jitter(latencies_ms: list[float]) -> float | None:
    """Calculate jitter as mean absolute difference between consecutive RTTs."""
    if len(latencies_ms) < 2:
        return None
    deltas = [
        abs(current - previous)
        for previous, current in zip(latencies_ms, latencies_ms[1:], strict=False)
    ]
    return statistics.fmean(deltas)


def _ping_args(host: str, timeout_seconds: float) -> list[str]:
    if platform.system().lower() == "windows":
        return ["ping", "-n", "1", "-w", str(int(timeout_seconds * 1000)), host]
    return ["ping", "-c", "1", "-W", str(max(1, int(timeout_seconds))), host]


def _parse_ping_latency(output: str) -> float | None:
    match = re_search_latency(output)
    return float(match) if match is not None else None


def re_search_latency(output: str) -> str | None:
    import re

    match = re.search(r"time[=<]\s*([0-9.]+)\s*ms", output, flags=re.IGNORECASE)
    return match.group(1) if match else None


def _parse_http_status(line: str) -> int | None:
    parts = line.split()
    if len(parts) >= 2 and parts[0].startswith("HTTP/"):
        try:
            return int(parts[1])
        except ValueError:
            return None
    return None
