"""Windows-specific extension points.

The Linux development build uses command-line fallbacks. Production Windows
builds can replace these helpers with Win32/WMI implementations while preserving
the public return models.
"""

from __future__ import annotations

from dqopr_netpulse.networking import detect_active_interface

__all__ = ["detect_active_interface"]
