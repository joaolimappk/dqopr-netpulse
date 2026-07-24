from __future__ import annotations

import importlib.util

import pytest

pytestmark = pytest.mark.skipif(
    importlib.util.find_spec("PySide6") is None,
    reason="PySide6 is not installed",
)


def test_gui_import_has_factories() -> None:
    from dqopr_netpulse import gui

    assert gui.APP_DISPLAY_NAME == "DQOPR NetPulse"
    assert callable(gui.create_app)
    assert callable(gui.create_main_window)


def test_gui_app_module_import_does_not_create_qapplication() -> None:
    from PySide6.QtWidgets import QApplication

    before = QApplication.instance()
    import dqopr_netpulse.gui.app as gui_app

    assert QApplication.instance() is before
    assert callable(gui_app.main)
