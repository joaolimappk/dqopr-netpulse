"""Shared typed models for monitoring, diagnostics, storage, and reporting."""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import UTC, datetime
from enum import StrEnum
from typing import Any
from uuid import uuid4


def utc_now() -> datetime:
    """Return a timezone-aware UTC timestamp."""
    return datetime.now(UTC)


class ProbeMethod(StrEnum):
    ICMP = "icmp"
    TCP = "tcp"
    DNS = "dns"
    HTTPS = "https"
    SPEED = "speed"
    ROUTE = "route"


class IncidentType(StrEnum):
    LOCAL_GATEWAY_PACKET_LOSS = "local_gateway_packet_loss"
    EXTERNAL_PACKET_LOSS = "external_packet_loss"
    HIGH_LATENCY = "high_latency"
    LATENCY_SPIKE = "latency_spike"
    HIGH_JITTER = "high_jitter"
    DNS_FAILURE = "dns_failure"
    HTTPS_FAILURE = "https_failure"
    COMPLETE_OUTAGE = "complete_internet_outage"
    INTERMITTENT_OUTAGE = "intermittent_outage"
    DOWNLOAD_DEGRADATION = "download_speed_degradation"
    UPLOAD_DEGRADATION = "upload_speed_degradation"
    WIFI_DEGRADATION = "wifi_signal_degradation"
    INTERFACE_CHANGED = "network_interface_changed"
    VPN_DETECTED = "vpn_detected"
    SLEEP_INTERRUPTION = "computer_sleep_or_test_interruption"
    SINGLE_TARGET_FAILURE = "inconclusive_isolated_target_failure"


class FaultDomain(StrEnum):
    LOCAL_NETWORK = "local_network"
    ISP_OR_UPSTREAM = "isp_or_upstream"
    DNS = "dns"
    TARGET_OR_ROUTE = "target_or_route"
    COMPUTER_OR_CONFIGURATION = "computer_or_configuration"
    INCONCLUSIVE = "inconclusive"


class Confidence(StrEnum):
    HIGH = "High confidence"
    MODERATE = "Moderate confidence"
    LOW = "Low confidence"
    INCONCLUSIVE = "Inconclusive"


class Severity(StrEnum):
    INFO = "info"
    LOW = "low"
    MEDIUM = "medium"
    HIGH = "high"
    CRITICAL = "critical"


@dataclass(frozen=True)
class Target:
    name: str
    host: str
    tcp_port: int = 443
    enabled: bool = True
    is_gateway: bool = False


@dataclass(frozen=True)
class SessionConfig:
    contracted_download_mbps: float | None = None
    contracted_upload_mbps: float | None = None
    duration_seconds: int | None = 3600
    cycle_count: int | None = None
    latency_interval_seconds: float = 2.0
    tcp_interval_seconds: float = 10.0
    dns_interval_seconds: float = 15.0
    https_interval_seconds: float = 30.0
    route_interval_seconds: float = 900.0
    speedtest_interval_seconds: float = 1800.0
    speedtest_enabled: bool = False
    targets: tuple[Target, ...] = field(default_factory=tuple)


@dataclass(frozen=True)
class NetworkInterfaceSnapshot:
    name: str | None
    interface_type: str = "unknown"
    local_ip: str | None = None
    gateway_ip: str | None = None
    dns_servers: tuple[str, ...] = ()
    wifi_signal_percent: int | None = None
    vpn_detected: bool = False


@dataclass(frozen=True)
class Measurement:
    session_id: str
    target_name: str
    target_address: str
    method: ProbeMethod
    sequence: int
    success: bool
    timestamp_utc: datetime = field(default_factory=utc_now)
    local_timestamp: str | None = None
    timezone: str | None = None
    rtt_ms: float | None = None
    min_latency_ms: float | None = None
    max_latency_ms: float | None = None
    avg_latency_ms: float | None = None
    median_latency_ms: float | None = None
    jitter_ms: float | None = None
    packet_loss_percent: float | None = None
    consecutive_loss_count: int = 0
    timeout_ms: float | None = None
    dns_duration_ms: float | None = None
    tcp_connect_duration_ms: float | None = None
    https_response_duration_ms: float | None = None
    http_status_code: int | None = None
    active_interface_name: str | None = None
    interface_type: str | None = None
    wifi_signal_percent: int | None = None
    gateway_address: str | None = None
    public_ip_masked: str | None = None
    vpn_detected: bool = False
    error_type: str | None = None
    error_message: str | None = None
    incident_id: str | None = None
    during_manual_marker: bool = False


@dataclass(frozen=True)
class SpeedTestResult:
    session_id: str
    timestamp_utc: datetime = field(default_factory=utc_now)
    download_mbps: float | None = None
    upload_mbps: float | None = None
    latency_ms: float | None = None
    server_name: str | None = None
    server_location: str | None = None
    methodology: str = "provider-cli"
    success: bool = True
    error_message: str | None = None


@dataclass(frozen=True)
class Incident:
    session_id: str
    incident_type: IncidentType
    start_time_utc: datetime
    end_time_utc: datetime | None
    severity: Severity
    affected_tests: tuple[str, ...]
    affected_targets: tuple[str, ...]
    probable_fault_domain: FaultDomain
    confidence: Confidence
    explanation: str
    id: str = field(default_factory=lambda: str(uuid4()))
    worst_latency_ms: float | None = None
    packet_loss_percent: float | None = None
    consecutive_failures: int = 0
    local_gateway_status: str | None = None
    external_target_status: str | None = None
    dns_status: str | None = None
    https_status: str | None = None
    speedtest_context: str | None = None
    supporting_measurement_ids: tuple[int, ...] = ()


@dataclass(frozen=True)
class ManualMarker:
    session_id: str
    timestamp_utc: datetime = field(default_factory=utc_now)
    note: str | None = None
    metrics_snapshot: dict[str, Any] = field(default_factory=dict)
    active_interface_name: str | None = None
    wifi_signal_percent: int | None = None
    gateway_status: str | None = None
    public_target_status: str | None = None
