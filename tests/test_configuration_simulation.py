from __future__ import annotations

import pytest

from dqopr_netpulse.configuration import Thresholds, validate_session_config
from dqopr_netpulse.models import SessionConfig


def test_unknown_contracted_speeds_are_valid_monitoring_configuration() -> None:
    config = SessionConfig(contracted_download_mbps=None, contracted_upload_mbps=None)

    validate_session_config(config)


def test_nonpositive_contracted_speeds_are_rejected() -> None:
    with pytest.raises(ValueError, match="download speed"):
        validate_session_config(SessionConfig(contracted_download_mbps=0.0))

    with pytest.raises(ValueError, match="upload speed"):
        validate_session_config(SessionConfig(contracted_upload_mbps=-1.0))


def test_speed_threshold_defaults_express_plan_percentage_bands() -> None:
    thresholds = Thresholds()

    assert thresholds.speed_below_plan_warning_percent == 90.0
    assert thresholds.speed_below_plan_major_percent == 75.0
    assert thresholds.speed_below_plan_critical_percent == 50.0
