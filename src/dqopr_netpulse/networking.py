"""Network-interface discovery helpers.

The implementation deliberately uses standard-user commands and best-effort
parsing. Windows-specific API integrations can refine this later without
changing the storage or diagnostics contracts.
"""

from __future__ import annotations

import ipaddress
import platform
import re
import socket
import subprocess

from dqopr_netpulse.models import NetworkInterfaceSnapshot, Target

VPN_NAME_HINTS = ("vpn", "wireguard", "openvpn", "tailscale", "zerotier", "tap", "tun")


def detect_active_interface() -> NetworkInterfaceSnapshot:
    system = platform.system().lower()
    if system == "windows":
        return _detect_windows_interface()
    return _detect_linux_interface()


def default_targets_with_gateway(public_targets: tuple[Target, ...]) -> tuple[Target, ...]:
    snapshot = detect_active_interface()
    if snapshot.gateway_ip:
        gateway = Target(
            name="Local Gateway", host=snapshot.gateway_ip, enabled=True, is_gateway=True
        )
        return (gateway, *public_targets)
    return public_targets


def _detect_linux_interface() -> NetworkInterfaceSnapshot:
    route = _run(["ip", "route", "show", "default"])
    gateway_ip = None
    interface_name = None
    if route:
        gateway_match = re.search(r"\bvia\s+(\S+)", route)
        dev_match = re.search(r"\bdev\s+(\S+)", route)
        gateway_ip = gateway_match.group(1) if gateway_match else None
        interface_name = dev_match.group(1) if dev_match else None

    local_ip = None
    if interface_name:
        addr = _run(["ip", "-o", "-4", "addr", "show", "dev", interface_name])
        addr_match = re.search(r"\binet\s+([0-9.]+)/", addr)
        local_ip = addr_match.group(1) if addr_match else None

    dns_servers = tuple(re.findall(r"^nameserver\s+(\S+)", _read_resolv_conf(), flags=re.MULTILINE))
    interface_type = _infer_interface_type(interface_name)
    return NetworkInterfaceSnapshot(
        name=interface_name,
        interface_type=interface_type,
        local_ip=local_ip,
        gateway_ip=gateway_ip,
        dns_servers=dns_servers,
        vpn_detected=_looks_like_vpn(interface_name),
    )


def _detect_windows_interface() -> NetworkInterfaceSnapshot:
    route = _run(["route", "print", "-4"])
    gateway_ip = None
    local_ip = _local_ip_via_socket()
    for line in route.splitlines():
        parts = line.split()
        if len(parts) >= 5 and parts[0] == "0.0.0.0" and parts[1] == "0.0.0.0":
            gateway_ip = parts[2]
            if local_ip is None:
                local_ip = parts[3]
            break
    dns_servers = _windows_dns_servers()
    return NetworkInterfaceSnapshot(
        name=None,
        interface_type="unknown",
        local_ip=local_ip,
        gateway_ip=gateway_ip,
        dns_servers=dns_servers,
    )


def _windows_dns_servers() -> tuple[str, ...]:
    output = _run(["ipconfig", "/all"])
    return tuple(
        match.group(1) for match in re.finditer(r"DNS Servers[ .]*:\s*([0-9a-fA-F:.]+)", output)
    )


def _local_ip_via_socket() -> str | None:
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as sock:
            sock.connect(("8.8.8.8", 80))
            return str(ipaddress.ip_address(sock.getsockname()[0]))
    except OSError:
        return None


def _infer_interface_type(name: str | None) -> str:
    if not name:
        return "unknown"
    lower = name.lower()
    if _looks_like_vpn(name):
        return "vpn"
    if lower.startswith(("wl", "wifi", "wlan")):
        return "wi-fi"
    if lower.startswith(("en", "eth")):
        return "ethernet"
    if lower.startswith(("ww", "cell")):
        return "cellular"
    return "unknown"


def _looks_like_vpn(name: str | None) -> bool:
    return bool(name and any(hint in name.lower() for hint in VPN_NAME_HINTS))


def _read_resolv_conf() -> str:
    try:
        return "/etc/resolv.conf" and open("/etc/resolv.conf", encoding="utf-8").read()
    except OSError:
        return ""


def _run(args: list[str], timeout: float = 3.0) -> str:
    try:
        completed = subprocess.run(
            args,
            check=False,
            capture_output=True,
            text=True,
            timeout=timeout,
        )
    except (OSError, subprocess.TimeoutExpired):
        return ""
    return completed.stdout
