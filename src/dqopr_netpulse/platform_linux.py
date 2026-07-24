"""Linux-specific extension points for development and test runs."""

from __future__ import annotations

from dqopr_netpulse.networking import detect_active_interface

__all__ = ["detect_active_interface"]
