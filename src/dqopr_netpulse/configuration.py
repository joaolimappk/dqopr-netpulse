"""Configuration defaults and validation for NetPulse."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from dqopr_netpulse.models import SessionConfig, Target

DEFAULT_PUBLIC_TARGETS: tuple[Target, ...] = (
    Target(name="Cloudflare", host="1.1.1.1"),
    Target(name="Google", host="8.8.8.8"),
    Target(name="Quad9", host="9.9.9.9"),
    Target(name="OpenDNS", host="208.67.222.222"),
)


@dataclass(frozen=True)
class Thresholds:
    high_latency_ms: float = 150.0
    latency_spike_multiplier: float = 3.0
    latency_spike_min_delta_ms: float = 75.0
    high_jitter_ms: float = 40.0
    packet_loss_warning_percent: float = 5.0
    consecutive_loss_outage_count: int = 3
    speed_below_plan_warning_percent: float = 90.0
    speed_below_plan_major_percent: float = 75.0
    speed_below_plan_critical_percent: float = 50.0
    wifi_signal_warning_percent: int = 35
    rolling_window_samples: int = 30


@dataclass(frozen=True)
class AppConfig:
    data_dir: Path
    thresholds: Thresholds = Thresholds()
    session: SessionConfig = SessionConfig(targets=DEFAULT_PUBLIC_TARGETS)
    retention_days: int = 180
    private_report_mode: bool = True

    @property
    def database_path(self) -> Path:
        return self.data_dir / "netpulse.sqlite3"


def default_data_dir(app_name: str = "DQOPR NetPulse") -> Path:
    """Return an OS-appropriate per-user application data directory."""
    import os
    import sys

    if sys.platform == "win32":
        base = os.getenv("LOCALAPPDATA") or os.path.expanduser("~\\AppData\\Local")
        return Path(base) / app_name
    if sys.platform == "darwin":
        return Path.home() / "Library" / "Application Support" / app_name
    return Path(os.getenv("XDG_DATA_HOME", Path.home() / ".local" / "share")) / "dqopr-netpulse"


def validate_session_config(config: SessionConfig) -> None:
    """Validate user-visible monitoring settings."""
    if config.contracted_download_mbps is not None and config.contracted_download_mbps <= 0:
        raise ValueError("Contracted download speed must be positive or unknown.")
    if config.contracted_upload_mbps is not None and config.contracted_upload_mbps <= 0:
        raise ValueError("Contracted upload speed must be positive or unknown.")
    if config.duration_seconds is not None and config.duration_seconds <= 0:
        raise ValueError("Duration must be positive, continuous, or cycle-based.")
    if config.cycle_count is not None and config.cycle_count <= 0:
        raise ValueError("Cycle count must be positive when provided.")
    if config.speedtest_interval_seconds < 300:
        raise ValueError("Speed tests must not be scheduled more often than every 5 minutes.")
    for field_name in (
        "latency_interval_seconds",
        "tcp_interval_seconds",
        "dns_interval_seconds",
        "https_interval_seconds",
        "route_interval_seconds",
    ):
        if getattr(config, field_name) <= 0:
            raise ValueError(f"{field_name} must be positive.")
