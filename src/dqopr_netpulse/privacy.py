"""Privacy-preserving masking helpers for reports and exports."""

from __future__ import annotations

import hashlib
import ipaddress


def mask_ip(value: str | None) -> str | None:
    if not value:
        return value
    try:
        address = ipaddress.ip_address(value)
    except ValueError:
        return _stable_mask(value)
    if address.version == 4:
        parts = value.split(".")
        return ".".join((parts[0], parts[1], "x", "x"))
    exploded = address.exploded.split(":")
    return ":".join((*exploded[:3], "xxxx", "xxxx", "xxxx", "xxxx", "xxxx"))


def mask_text(value: str | None) -> str | None:
    if not value:
        return value
    return _stable_mask(value)


def _stable_mask(value: str) -> str:
    digest = hashlib.sha256(value.encode("utf-8")).hexdigest()[:10]
    return f"masked-{digest}"
