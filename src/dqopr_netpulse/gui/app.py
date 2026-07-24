"""Windows-oriented PySide6 application shell for DQOPR NetPulse.

The GUI intentionally exposes signals and small factory functions so monitoring,
storage, graphing, and reporting backends can be integrated without making import
time perform any application startup work.
"""

from __future__ import annotations

import asyncio
import sys
import time
from collections import deque
from collections.abc import Sequence
from dataclasses import replace
from datetime import datetime
from pathlib import Path
from typing import cast

from PySide6.QtCore import QObject, Qt, QThread, QTimer, Signal, Slot
from PySide6.QtGui import QAction, QCloseEvent, QIcon
from PySide6.QtWidgets import (
    QApplication,
    QCheckBox,
    QComboBox,
    QDialog,
    QDialogButtonBox,
    QDoubleSpinBox,
    QFileDialog,
    QFormLayout,
    QFrame,
    QGridLayout,
    QGroupBox,
    QHBoxLayout,
    QHeaderView,
    QLabel,
    QLineEdit,
    QMainWindow,
    QMenu,
    QMessageBox,
    QPlainTextEdit,
    QProgressBar,
    QPushButton,
    QSpinBox,
    QStatusBar,
    QStyle,
    QStyleFactory,
    QTableWidget,
    QTableWidgetItem,
    QTabWidget,
    QVBoxLayout,
    QWidget,
    QWizard,
    QWizardPage,
)

from dqopr_netpulse.configuration import AppConfig, default_data_dir, validate_session_config
from dqopr_netpulse.exports.csv_export import export_session_csv
from dqopr_netpulse.models import Measurement, ProbeMethod, SessionConfig, Target
from dqopr_netpulse.monitoring.engine import MonitoringSession
from dqopr_netpulse.quick_test import QuickTestRunner, QuickTestSummary
from dqopr_netpulse.reports.html_report import generate_html_report
from dqopr_netpulse.storage import NetPulseStore

APP_DISPLAY_NAME = "DQOPR NetPulse"

_DURATION_OPTIONS = (
    "Quick test - 10 minutes",
    "Standard test - 1 hour",
    "Evening test - 6 hours",
    "Full-day test - 24 hours",
    "Continuous monitoring",
    "Custom duration",
    "Custom number of test cycles",
)
_QUICK_DURATION_SECONDS = 10 * 60
_STANDARD_DURATION_SECONDS = 60 * 60
_EVENING_DURATION_SECONDS = 6 * 60 * 60
_FULL_DAY_DURATION_SECONDS = 24 * 60 * 60
_CONTINUOUS_INDEX = 4
_CUSTOM_DURATION_INDEX = 5
_CUSTOM_CYCLES_INDEX = 6


class BackendSignals(QObject):
    """Signals used by future backend controllers to connect monitoring behavior."""

    new_test_requested = Signal(SessionConfig)
    start_monitoring_requested = Signal()
    pause_monitoring_requested = Signal()
    stop_monitoring_requested = Signal()
    manual_marker_requested = Signal(str)
    view_incidents_requested = Signal()
    view_graphs_requested = Signal()
    generate_report_requested = Signal()
    export_csv_requested = Signal(str)
    open_previous_session_requested = Signal(str)
    settings_requested = Signal(SessionConfig)
    help_requested = Signal()
    about_requested = Signal()


class StartupWizard(QWizard):
    """Collect the user-visible SessionConfig fields before a monitoring session."""

    def __init__(
        self,
        initial_config: SessionConfig | None = None,
        parent: QWidget | None = None,
    ) -> None:
        super().__init__(parent)
        self.setWindowTitle(f"{APP_DISPLAY_NAME} Setup")
        self.setWizardStyle(QWizard.WizardStyle.ModernStyle)
        self.setOption(QWizard.WizardOption.NoBackButtonOnStartPage)

        self._initial_config = initial_config or SessionConfig()
        self._speed_page = ContractedSpeedPage(self._initial_config)
        self._duration_page = TestDurationPage(self._initial_config)
        self._advanced_page = AdvancedIntervalsPage(self._initial_config)

        self.addPage(self._speed_page)
        self.addPage(self._duration_page)
        self.addPage(self._advanced_page)

    def session_config(self) -> SessionConfig:
        """Return a validated SessionConfig assembled from the wizard fields."""
        targets = self._initial_config.targets
        config = SessionConfig(
            contracted_download_mbps=self._speed_page.download_mbps(),
            contracted_upload_mbps=self._speed_page.upload_mbps(),
            duration_seconds=self._duration_page.duration_seconds(),
            cycle_count=self._duration_page.cycle_count(),
            latency_interval_seconds=self._advanced_page.latency_interval_seconds(),
            tcp_interval_seconds=self._advanced_page.tcp_interval_seconds(),
            dns_interval_seconds=self._advanced_page.dns_interval_seconds(),
            https_interval_seconds=self._advanced_page.https_interval_seconds(),
            route_interval_seconds=self._advanced_page.route_interval_seconds(),
            speedtest_interval_seconds=self._advanced_page.speedtest_interval_seconds(),
            speedtest_enabled=self._advanced_page.speedtest_enabled(),
            targets=targets,
        )
        validate_session_config(config)
        return config


class ContractedSpeedPage(QWizardPage):
    """Wizard page for ISP plan speed settings."""

    def __init__(self, config: SessionConfig) -> None:
        super().__init__()
        self.setTitle("Contracted speeds")
        self.setSubTitle("Enter the speeds your ISP plan promises, or leave them unknown.")

        self._download_unknown = QCheckBox("I don't know")
        self._upload_unknown = QCheckBox("I don't know")
        self._download = _speed_spin_box(config.contracted_download_mbps)
        self._upload = _speed_spin_box(config.contracted_upload_mbps)

        self._download_unknown.setChecked(config.contracted_download_mbps is None)
        self._upload_unknown.setChecked(config.contracted_upload_mbps is None)
        self._download.setEnabled(not self._download_unknown.isChecked())
        self._upload.setEnabled(not self._upload_unknown.isChecked())
        self._download_unknown.toggled.connect(self._download.setDisabled)
        self._upload_unknown.toggled.connect(self._upload.setDisabled)

        layout = QFormLayout(self)
        layout.addRow("Download Mbps", self._download)
        layout.addRow("", self._download_unknown)
        layout.addRow("Upload Mbps", self._upload)
        layout.addRow("", self._upload_unknown)

    def download_mbps(self) -> float | None:
        if self._download_unknown.isChecked():
            return None
        return float(self._download.value())

    def upload_mbps(self) -> float | None:
        if self._upload_unknown.isChecked():
            return None
        return float(self._upload.value())


class TestDurationPage(QWizardPage):
    """Wizard page for fixed, cycle-based, or continuous monitoring duration."""

    def __init__(self, config: SessionConfig) -> None:
        super().__init__()
        self.setTitle("Test duration")
        self.setSubTitle("Choose how long this evidence-gathering session should run.")

        self._mode = QComboBox()
        self._mode.addItems(_DURATION_OPTIONS)
        self._custom_duration = QSpinBox()
        self._custom_duration.setRange(1, 365)
        self._custom_duration_unit = QComboBox()
        self._custom_duration_unit.addItems(("minutes", "hours", "days"))

        self._cycle_count = QSpinBox()
        self._cycle_count.setRange(1, 1_000_000)
        self._cycle_count.setValue(config.cycle_count or 100)

        self._apply_initial_duration(config)
        self._mode.currentIndexChanged.connect(self._refresh_enabled_fields)

        layout = QFormLayout(self)
        duration_row = QHBoxLayout()
        duration_row.addWidget(self._custom_duration)
        duration_row.addWidget(self._custom_duration_unit)
        duration_widget = QWidget()
        duration_widget.setLayout(duration_row)

        layout.addRow("Preset", self._mode)
        layout.addRow("Custom duration", duration_widget)
        layout.addRow("Custom cycles", self._cycle_count)
        self._refresh_enabled_fields()

    def duration_seconds(self) -> int | None:
        selected = self._mode.currentIndex()
        if selected == 0:
            return _QUICK_DURATION_SECONDS
        if selected == 1:
            return _STANDARD_DURATION_SECONDS
        if selected == 2:
            return _EVENING_DURATION_SECONDS
        if selected == 3:
            return _FULL_DAY_DURATION_SECONDS
        if selected in {_CONTINUOUS_INDEX, _CUSTOM_CYCLES_INDEX}:
            return None

        multiplier = {"minutes": 60, "hours": 3600, "days": 86400}[
            self._custom_duration_unit.currentText()
        ]
        return int(self._custom_duration.value() * multiplier)

    def cycle_count(self) -> int | None:
        if self._mode.currentIndex() == _CUSTOM_CYCLES_INDEX:
            return int(self._cycle_count.value())
        return None

    def _apply_initial_duration(self, config: SessionConfig) -> None:
        if config.cycle_count is not None:
            self._mode.setCurrentIndex(_CUSTOM_CYCLES_INDEX)
            self._cycle_count.setValue(config.cycle_count)
            return
        if config.duration_seconds is None:
            self._mode.setCurrentIndex(_CONTINUOUS_INDEX)
            return

        duration_to_index = {
            _QUICK_DURATION_SECONDS: 0,
            _STANDARD_DURATION_SECONDS: 1,
            _EVENING_DURATION_SECONDS: 2,
            _FULL_DAY_DURATION_SECONDS: 3,
        }
        preset_index = duration_to_index.get(config.duration_seconds)
        if preset_index is not None:
            self._mode.setCurrentIndex(preset_index)
            return

        self._mode.setCurrentIndex(_CUSTOM_DURATION_INDEX)
        self._set_custom_duration(config.duration_seconds)

    def _set_custom_duration(self, duration_seconds: int) -> None:
        if duration_seconds % 86400 == 0:
            self._custom_duration_unit.setCurrentText("days")
            self._custom_duration.setValue(max(1, duration_seconds // 86400))
        elif duration_seconds % 3600 == 0:
            self._custom_duration_unit.setCurrentText("hours")
            self._custom_duration.setValue(max(1, duration_seconds // 3600))
        else:
            self._custom_duration_unit.setCurrentText("minutes")
            self._custom_duration.setValue(max(1, duration_seconds // 60))

    def _refresh_enabled_fields(self, _index: int | None = None) -> None:
        selected = self._mode.currentIndex()
        is_custom_duration = selected == _CUSTOM_DURATION_INDEX
        self._custom_duration.setEnabled(is_custom_duration)
        self._custom_duration_unit.setEnabled(is_custom_duration)
        self._cycle_count.setEnabled(selected == _CUSTOM_CYCLES_INDEX)


class AdvancedIntervalsPage(QWizardPage):
    """Wizard page for interval warnings and advanced monitoring cadence."""

    def __init__(self, config: SessionConfig) -> None:
        super().__init__()
        self.setTitle("Warning and advanced settings")
        self.setSubTitle("Short intervals create stronger evidence but can add network traffic.")

        self._latency = _interval_spin_box(config.latency_interval_seconds, " sec")
        self._tcp = _interval_spin_box(config.tcp_interval_seconds, " sec")
        self._dns = _interval_spin_box(config.dns_interval_seconds, " sec")
        self._https = _interval_spin_box(config.https_interval_seconds, " sec")
        self._route = _interval_spin_box(config.route_interval_seconds, " sec")
        self._speedtest = _interval_spin_box(config.speedtest_interval_seconds / 60, " min")
        self._speedtest.setRange(5.0, 1440.0)
        self._speedtest_enabled = QCheckBox("Run periodic speed tests")
        self._speedtest_enabled.setChecked(config.speedtest_enabled)
        self._warning = QLabel()
        self._warning.setWordWrap(True)
        self._warning.setFrameShape(QFrame.Shape.StyledPanel)

        interval_widgets = (
            self._latency,
            self._tcp,
            self._dns,
            self._https,
            self._route,
            self._speedtest,
        )
        for widget in interval_widgets:
            widget.valueChanged.connect(self._refresh_warning)
        self._speedtest_enabled.toggled.connect(self._speedtest.setEnabled)
        self._speedtest.setEnabled(self._speedtest_enabled.isChecked())

        layout = QFormLayout(self)
        layout.addRow("Latency probe", self._latency)
        layout.addRow("TCP probe", self._tcp)
        layout.addRow("DNS probe", self._dns)
        layout.addRow("HTTPS probe", self._https)
        layout.addRow("Route snapshot", self._route)
        layout.addRow("", self._speedtest_enabled)
        layout.addRow("Speed test", self._speedtest)
        layout.addRow("Interval warning", self._warning)

        self._refresh_warning()

    def latency_interval_seconds(self) -> float:
        return float(self._latency.value())

    def tcp_interval_seconds(self) -> float:
        return float(self._tcp.value())

    def dns_interval_seconds(self) -> float:
        return float(self._dns.value())

    def https_interval_seconds(self) -> float:
        return float(self._https.value())

    def route_interval_seconds(self) -> float:
        return float(self._route.value())

    def speedtest_interval_seconds(self) -> float:
        return float(self._speedtest.value() * 60)

    def speedtest_enabled(self) -> bool:
        return self._speedtest_enabled.isChecked()

    def _refresh_warning(self, _value: float | int | None = None) -> None:
        concerns: list[str] = []
        if self._latency.value() < 1.0:
            concerns.append("latency probes are very frequent")
        if self._tcp.value() < 5.0:
            concerns.append("TCP probes are aggressive")
        if self._dns.value() < 10.0:
            concerns.append("DNS probes are frequent")
        if self._speedtest_enabled.isChecked() and self._speedtest.value() < 15.0:
            concerns.append("speed tests can consume noticeable bandwidth")

        if concerns:
            self._warning.setText("Warning: " + "; ".join(concerns) + ".")
        else:
            self._warning.setText(
                "Recommended defaults are suitable for ordinary evidence collection."
            )


class MonitoringWorker(QObject):
    """Runs the monitoring engine away from the GUI thread."""

    activity = Signal(str)
    state_changed = Signal(str)
    measurement = Signal(object)
    failed = Signal(str)
    finished = Signal(str)

    def __init__(self, app_config: AppConfig) -> None:
        super().__init__()
        self._app_config = app_config
        self._loop: asyncio.AbstractEventLoop | None = None
        self._session: MonitoringSession | None = None

    @Slot()
    def run(self) -> None:
        store: NetPulseStore | None = None
        loop = asyncio.new_event_loop()
        self._loop = loop
        asyncio.set_event_loop(loop)
        try:
            store = NetPulseStore(self._app_config.database_path)
            self._session = MonitoringSession(
                self._app_config,
                store,
                activity_callback=self.activity.emit,
                measurement_callback=self.measurement.emit,
                state_callback=self.state_changed.emit,
            )
            session_id = loop.run_until_complete(self._session.run())
            self.finished.emit(session_id)
        except Exception as exc:  # pragma: no cover - exercised by Windows/UI runtime
            self.failed.emit(str(exc) or type(exc).__name__)
        finally:
            if store is not None:
                store.close()
            loop.close()
            self._loop = None
            self._session = None

    def stop(self) -> None:
        self._call_session("stop")

    def pause(self) -> None:
        self._call_session("pause")

    def resume(self) -> None:
        self._call_session("resume")

    def mark_bad_now(self, note: str) -> None:
        loop = self._loop
        session = self._session
        if loop is not None and session is not None:
            loop.call_soon_threadsafe(self._add_marker, session, note)

    def _call_session(self, method_name: str) -> None:
        loop = self._loop
        session = self._session
        if loop is not None and session is not None:
            loop.call_soon_threadsafe(getattr(session, method_name))

    def _add_marker(self, session: MonitoringSession, note: str) -> None:
        marker_id = session.add_manual_marker(note)
        self.activity.emit(f"Manual quality marker saved (marker {marker_id}).")


class QuickTestWorker(QObject):
    """Runs one complete quick test away from the GUI thread."""

    activity = Signal(str)
    progress = Signal(int, int, str)
    measurement = Signal(object)
    failed = Signal(str)
    finished = Signal(object)

    def __init__(self, app_config: AppConfig) -> None:
        super().__init__()
        self._app_config = app_config
        self._loop: asyncio.AbstractEventLoop | None = None
        self._runner: QuickTestRunner | None = None

    @Slot()
    def run(self) -> None:
        store: NetPulseStore | None = None
        loop = asyncio.new_event_loop()
        self._loop = loop
        asyncio.set_event_loop(loop)
        try:
            store = NetPulseStore(self._app_config.database_path)
            self._runner = QuickTestRunner(
                self._app_config,
                store,
                activity_callback=self.activity.emit,
                progress_callback=self.progress.emit,
                measurement_callback=self.measurement.emit,
            )
            summary = loop.run_until_complete(self._runner.run())
            self.finished.emit(summary)
        except Exception as exc:  # pragma: no cover - exercised by Windows/UI runtime
            self.failed.emit(str(exc) or type(exc).__name__)
        finally:
            if store is not None:
                store.close()
            loop.close()
            self._loop = None
            self._runner = None

    def cancel(self) -> None:
        loop = self._loop
        runner = self._runner
        if loop is not None and runner is not None:
            loop.call_soon_threadsafe(runner.cancel)


class MainWindow(QMainWindow):
    """Main application window connected to the monitoring engine."""

    def __init__(
        self,
        app_config: AppConfig | None = None,
        parent: QWidget | None = None,
    ) -> None:
        super().__init__(parent)
        self.signals = BackendSignals(self)
        self._app_config = app_config or AppConfig(data_dir=default_data_dir())
        self._session_config = self._app_config.session
        self._actions: dict[str, QAction] = {}
        self._metric_labels: dict[str, QLabel] = {}
        self._recent_measurements: deque[Measurement] = deque(maxlen=200)
        self._worker_thread: QThread | None = None
        self._worker: MonitoringWorker | None = None
        self._quick_worker_thread: QThread | None = None
        self._quick_worker: QuickTestWorker | None = None
        self._last_session_id: str | None = None
        self._last_quick_summary: QuickTestSummary | None = None
        self._state = "ready"
        self._started_monotonic: float | None = None
        self._paused_monotonic: float | None = None
        self._last_measurement_monotonic: float | None = None
        self._completed_cycles = 0
        self._incident_count = 0

        self._status_label = QLabel("Ready")
        self._status_label.setObjectName("statusLabel")
        self._elapsed_label = QLabel("Elapsed: 00:00:00")
        self._remaining_label = QLabel("Mode: Ready")
        self._last_measurement_label = QLabel("Last measurement: none yet")
        self._recording_label = QLabel("Recording: idle")
        self._current_operation = QLabel("Currently testing: idle")
        self._current_operation.setWordWrap(True)
        self._spinner = QProgressBar()
        self._spinner.setRange(0, 0)
        self._spinner.setTextVisible(False)
        self._spinner.setMaximumHeight(8)
        self._spinner.hide()
        self._quick_summary_label = QLabel("Quick test results will appear here.")
        self._quick_summary_label.setWordWrap(True)
        self._activity_log = QPlainTextEdit()
        self._activity_log.setReadOnly(True)
        self._activity_log.setMaximumBlockCount(100)
        self._session_summary = QLabel()
        self._session_summary.setWordWrap(True)
        self._measurement_table = QTableWidget(0, 4)
        self._measurement_table.setHorizontalHeaderLabels(("Time", "Test", "Target", "Result"))
        self._measurement_table.horizontalHeader().setSectionResizeMode(
            QHeaderView.ResizeMode.Stretch
        )

        self.setWindowTitle(APP_DISPLAY_NAME)
        self.setMinimumSize(960, 680)
        self._timer = QTimer(self)
        self._timer.setInterval(1000)
        self._timer.timeout.connect(self._tick)

        self._build_actions()
        self._build_menu_bar()
        self._build_central_widget()
        self._build_status_bar()
        self._connect_actions()
        self._refresh_session_summary()
        self._set_state("ready")
        self._append_activity("Ready. Configure a test or start monitoring.")

    @property
    def session_config(self) -> SessionConfig:
        """Return the current wizard-produced session configuration."""
        return self._session_config

    def update_dashboard_status(self, status: str) -> None:
        """Update dashboard status text from an external controller."""
        self._set_state(status.lower().replace(" ", "_"))
        self._append_activity(status)

    def set_metric(self, name: str, value: str) -> None:
        """Set a metric label by display name for backend-driven updates."""
        label = self._metric_labels.get(name)
        if label is not None:
            label.setText(value)

    def _build_actions(self) -> None:
        style = self.style()
        self._actions = {
            "new_test": _action(
                self,
                "New Test",
                style.standardIcon(QStyle.StandardPixmap.SP_FileDialogNewFolder),
            ),
            "start": _action(
                self,
                "Start Monitoring",
                style.standardIcon(QStyle.StandardPixmap.SP_MediaPlay),
            ),
            "quick_test": _action(
                self,
                "Run Quick Test",
                style.standardIcon(QStyle.StandardPixmap.SP_ComputerIcon),
            ),
            "pause": _action(
                self,
                "Pause",
                style.standardIcon(QStyle.StandardPixmap.SP_MediaPause),
            ),
            "stop": _action(self, "Stop", style.standardIcon(QStyle.StandardPixmap.SP_MediaStop)),
            "bad_now": _action(
                self,
                "Internet Feels Bad Now",
                style.standardIcon(QStyle.StandardPixmap.SP_MessageBoxWarning),
            ),
            "report": _action(
                self,
                "Generate ISP Report",
                style.standardIcon(QStyle.StandardPixmap.SP_FileDialogDetailedView),
            ),
            "export_csv": _action(
                self,
                "Export CSV",
                style.standardIcon(QStyle.StandardPixmap.SP_DriveFDIcon),
            ),
            "open_session": _action(
                self,
                "Open Previous Session",
                style.standardIcon(QStyle.StandardPixmap.SP_DirOpenIcon),
            ),
            "settings": _action(
                self,
                "Settings",
                style.standardIcon(QStyle.StandardPixmap.SP_FileDialogContentsView),
            ),
            "help": _action(
                self,
                "Help",
                style.standardIcon(QStyle.StandardPixmap.SP_DialogHelpButton),
            ),
            "about": _action(
                self,
                "About",
                style.standardIcon(QStyle.StandardPixmap.SP_MessageBoxInformation),
            ),
        }

    def _build_menu_bar(self) -> None:
        file_menu = cast(QMenu, self.menuBar().addMenu("&File"))
        file_menu.addAction(self._actions["new_test"])
        file_menu.addAction(self._actions["open_session"])
        file_menu.addSeparator()
        file_menu.addAction(self._actions["export_csv"])
        file_menu.addAction(self._actions["report"])
        file_menu.addSeparator()
        file_menu.addAction("E&xit", self.close)

        monitor_menu = cast(QMenu, self.menuBar().addMenu("&Monitor"))
        monitor_menu.addAction(self._actions["start"])
        monitor_menu.addAction(self._actions["quick_test"])
        monitor_menu.addAction(self._actions["pause"])
        monitor_menu.addAction(self._actions["stop"])
        monitor_menu.addSeparator()
        monitor_menu.addAction(self._actions["bad_now"])

        tools_menu = cast(QMenu, self.menuBar().addMenu("&Tools"))
        tools_menu.addAction(self._actions["settings"])

        help_menu = cast(QMenu, self.menuBar().addMenu("&Help"))
        help_menu.addAction(self._actions["help"])
        help_menu.addAction(self._actions["about"])

    def _build_central_widget(self) -> None:
        tabs = QTabWidget()
        tabs.addTab(self._dashboard_tab(), "Dashboard")
        tabs.addTab(self._details_tab(), "Details")
        tabs.addTab(
            _placeholder_table("Incidents", ("Time", "Severity", "Classification")), "Incidents"
        )
        tabs.addTab(self._reports_tab(), "Reports")
        tabs.addTab(self._settings_tab(), "Settings")
        self.setCentralWidget(tabs)

    def _dashboard_tab(self) -> QWidget:
        root = QWidget()
        outer = QVBoxLayout(root)
        outer.setContentsMargins(18, 18, 18, 18)
        outer.setSpacing(14)

        header = QHBoxLayout()
        title = QLabel(APP_DISPLAY_NAME)
        title.setStyleSheet("font-size: 24px; font-weight: 650;")
        header.addWidget(title)
        header.addStretch(1)
        self._status_label.setStyleSheet("font-size: 20px; font-weight: 650;")
        header.addWidget(self._status_label)
        outer.addLayout(header)

        time_row = QHBoxLayout()
        time_row.addWidget(self._elapsed_label)
        time_row.addWidget(self._remaining_label)
        time_row.addWidget(self._last_measurement_label)
        time_row.addStretch(1)
        time_row.addWidget(self._recording_label)
        outer.addLayout(time_row)

        metric_grid = QGridLayout()
        metric_grid.setSpacing(12)
        metrics = (
            ("Latency", "Starting test..."),
            ("Packet Loss", "Starting test..."),
            ("Jitter", "Starting test..."),
            ("Download", "Waiting for quick test"),
            ("Upload", "Waiting for quick test"),
            ("Connection to your router", "Waiting for first result"),
            ("Internet connection", "Waiting for first result"),
        )
        for index, (name, value) in enumerate(metrics):
            box = _metric_box(name, value)
            self._metric_labels[name] = cast(QLabel, box.findChild(QLabel, "metricValue"))
            metric_grid.addWidget(box, index // 3, index % 3)
        outer.addLayout(metric_grid)

        operation_group = QGroupBox("Current operation")
        operation_layout = QVBoxLayout(operation_group)
        operation_layout.addWidget(self._current_operation)
        operation_layout.addWidget(self._spinner)
        outer.addWidget(operation_group)

        quick_button = QPushButton(self._actions["quick_test"].text())
        quick_button.setIcon(self._actions["quick_test"].icon())
        quick_button.clicked.connect(self._actions["quick_test"].trigger)
        quick_button.setMinimumHeight(44)
        quick_button.setStyleSheet("font-size: 17px; font-weight: 650;")
        self._actions["quick_test"].changed.connect(
            lambda action=self._actions["quick_test"], btn=quick_button: self._sync_button(
                action, btn
            )
        )
        self._sync_button(self._actions["quick_test"], quick_button)
        outer.addWidget(quick_button)

        controls = QHBoxLayout()
        for key in ("start", "pause", "stop", "bad_now"):
            button = QPushButton(self._actions[key].text())
            button.setIcon(self._actions[key].icon())
            button.clicked.connect(self._actions[key].trigger)
            button.setMinimumHeight(34)
            self._actions[key].changed.connect(
                lambda action=self._actions[key], btn=button: self._sync_button(action, btn)
            )
            self._sync_button(self._actions[key], button)
            controls.addWidget(button)
        controls.addStretch(1)
        outer.addLayout(controls)

        log_group = QGroupBox("Recent activity")
        log_layout = QVBoxLayout(log_group)
        log_layout.addWidget(self._quick_summary_label)
        log_layout.addWidget(self._activity_log)
        outer.addWidget(log_group, stretch=1)
        return root

    def _details_tab(self) -> QWidget:
        root = QWidget()
        layout = QVBoxLayout(root)
        layout.addWidget(QLabel("Recent measurements and technical probe results"))
        layout.addWidget(self._measurement_table)
        return root

    def _reports_tab(self) -> QWidget:
        root = QWidget()
        layout = QVBoxLayout(root)
        layout.addWidget(
            QLabel("Generate ISP evidence after or during a saved monitoring session.")
        )
        report = QPushButton(self._actions["report"].text())
        report.clicked.connect(self._actions["report"].trigger)
        export = QPushButton(self._actions["export_csv"].text())
        export.clicked.connect(self._actions["export_csv"].trigger)
        layout.addWidget(report)
        layout.addWidget(export)
        layout.addStretch(1)
        return root

    def _settings_tab(self) -> QWidget:
        root = QWidget()
        layout = QVBoxLayout(root)
        layout.addWidget(QLabel("Current session settings"))
        layout.addWidget(self._session_summary)
        settings = QPushButton(self._actions["settings"].text())
        settings.clicked.connect(self._actions["settings"].trigger)
        layout.addWidget(settings)
        layout.addStretch(1)
        return root

    def _build_status_bar(self) -> None:
        status_bar = QStatusBar()
        status_bar.showMessage(f"Data folder: {self._app_config.data_dir}")
        self.setStatusBar(status_bar)

    def _connect_actions(self) -> None:
        self._actions["new_test"].triggered.connect(self._new_test)
        self._actions["start"].triggered.connect(self._start_monitoring)
        self._actions["quick_test"].triggered.connect(self._run_quick_test)
        self._actions["pause"].triggered.connect(self._pause_or_resume_monitoring)
        self._actions["stop"].triggered.connect(self._stop_monitoring)
        self._actions["bad_now"].triggered.connect(self._manual_marker)
        self._actions["report"].triggered.connect(self._generate_report)
        self._actions["export_csv"].triggered.connect(self._export_csv)
        self._actions["open_session"].triggered.connect(self._open_previous_session)
        self._actions["settings"].triggered.connect(self._settings)
        self._actions["help"].triggered.connect(self._help)
        self._actions["about"].triggered.connect(self._about)

    def _new_test(self) -> None:
        if self._is_active():
            QMessageBox.information(
                self, "Monitoring active", "Stop monitoring before changing tests."
            )
            return
        wizard = StartupWizard(self._session_config, self)
        if wizard.exec() == QDialog.DialogCode.Accepted:
            try:
                self._session_config = wizard.session_config()
            except ValueError as exc:
                QMessageBox.warning(self, "Invalid settings", str(exc))
                return
            self._refresh_session_summary()
            self.signals.new_test_requested.emit(self._session_config)
            self._append_activity("New test configured.")

    def _start_monitoring(self) -> None:
        if self._is_active():
            self._append_activity("Start ignored because a monitoring session is already active.")
            return
        self._recent_measurements.clear()
        self._completed_cycles = 0
        self._incident_count = 0
        self._last_measurement_monotonic = None
        self._started_monotonic = time.monotonic()
        self._paused_monotonic = None
        self._reset_metrics()
        self._set_state("starting")
        self._spinner.setRange(0, 0)
        self._append_activity("Starting monitoring session...")
        self.signals.start_monitoring_requested.emit()

        app_config = replace(self._app_config, session=self._session_config)
        self._worker_thread = QThread(self)
        self._worker = MonitoringWorker(app_config)
        self._worker.moveToThread(self._worker_thread)
        self._worker_thread.started.connect(self._worker.run)
        self._worker.activity.connect(self._append_activity)
        self._worker.state_changed.connect(self._handle_worker_state)
        self._worker.measurement.connect(self._handle_measurement)
        self._worker.failed.connect(self._worker_failed)
        self._worker.finished.connect(self._worker_finished)
        self._worker.failed.connect(self._worker_thread.quit)
        self._worker.finished.connect(self._worker_thread.quit)
        self._worker_thread.finished.connect(self._thread_finished)
        self._worker_thread.start()
        self._timer.start()

    def _pause_or_resume_monitoring(self) -> None:
        worker = self._worker
        if worker is None:
            return
        if self._state == "paused":
            worker.resume()
            self.signals.pause_monitoring_requested.emit()
        else:
            worker.pause()
            self.signals.pause_monitoring_requested.emit()

    def _stop_monitoring(self) -> None:
        if self._quick_worker is not None:
            self._set_state("stopping")
            self._quick_worker.cancel()
            return
        worker = self._worker
        if worker is None:
            return
        self._set_state("stopping")
        self.signals.stop_monitoring_requested.emit()
        worker.stop()

    def _manual_marker(self) -> None:
        dialog = ManualMarkerDialog(self)
        if dialog.exec() == QDialog.DialogCode.Accepted:
            note = dialog.note()
            if self._worker is not None:
                self._worker.mark_bad_now(note)
            self.signals.manual_marker_requested.emit(note)
            self._append_activity("Internet feels bad marker requested.")

    def _generate_report(self) -> None:
        self.signals.generate_report_requested.emit()
        session_id = self._last_session_id
        if session_id is None:
            QMessageBox.information(self, "ISP Report", "Run a test before generating a report.")
            return
        path, _ = QFileDialog.getSaveFileName(
            self,
            "Generate ISP Report",
            f"netpulse-report-{session_id[:8]}.html",
            "HTML files (*.html)",
        )
        if path:
            store = NetPulseStore(self._app_config.database_path)
            try:
                generate_html_report(
                    store,
                    session_id,
                    Path(path),
                    contracted_download_mbps=self._session_config.contracted_download_mbps,
                    contracted_upload_mbps=self._session_config.contracted_upload_mbps,
                    private_mode=self._app_config.private_report_mode,
                )
            finally:
                store.close()
            self._append_activity(f"Report generated: {path}")

    def _export_csv(self) -> None:
        session_id = self._last_session_id
        if session_id is None:
            QMessageBox.information(self, "Export CSV", "Run a test before exporting results.")
            return
        path = QFileDialog.getExistingDirectory(
            self,
            "Export CSV",
            str(self._app_config.data_dir),
        )
        if path:
            self.signals.export_csv_requested.emit(path)
            store = NetPulseStore(self._app_config.database_path)
            try:
                export_session_csv(store, session_id, Path(path))
            finally:
                store.close()
            self._append_activity(f"CSV export completed: {path}")

    def _open_previous_session(self) -> None:
        path, _ = QFileDialog.getOpenFileName(
            self,
            "Open Previous Session",
            str(self._app_config.data_dir),
            "NetPulse database (*.sqlite3);;All files (*)",
        )
        if path:
            self.signals.open_previous_session_requested.emit(path)
            self._append_activity(f"Open previous session requested: {path}")

    def _settings(self) -> None:
        if self._is_active():
            QMessageBox.information(
                self, "Monitoring active", "Stop monitoring before changing settings."
            )
            return
        wizard = StartupWizard(self._session_config, self)
        if wizard.exec() == QDialog.DialogCode.Accepted:
            try:
                self._session_config = wizard.session_config()
            except ValueError as exc:
                QMessageBox.warning(self, "Invalid settings", str(exc))
                return
            self._refresh_session_summary()
            self.signals.settings_requested.emit(self._session_config)
            self._append_activity("Settings updated.")

    def _help(self) -> None:
        self.signals.help_requested.emit()
        QMessageBox.information(
            self,
            "Help",
            "Start Monitoring begins live tests. Watch Current operation and Recent activity "
            "to confirm measurements are being recorded. Run Quick Test performs one snapshot "
            "cycle and then stops automatically.",
        )

    def _about(self) -> None:
        self.signals.about_requested.emit()
        QMessageBox.about(
            self,
            f"About {APP_DISPLAY_NAME}",
            f"{APP_DISPLAY_NAME}\nInternet Quality Monitor and ISP Evidence Reporter",
        )

    @Slot(str)
    def _handle_worker_state(self, state: str) -> None:
        self._set_state(state)

    @Slot(object)
    def _handle_measurement(self, raw: object) -> None:
        measurement = cast(Measurement, raw)
        self._last_measurement_monotonic = time.monotonic()
        self._recent_measurements.append(measurement)
        if measurement.method == ProbeMethod.ICMP:
            self._completed_cycles = max(self._completed_cycles, measurement.sequence)
        self._update_metrics()
        self._add_measurement_row(measurement)
        self._recording_label.setText("Recording: measurements saved")
        if self._state == "starting":
            self._set_state("monitoring")

    @Slot(str)
    def _worker_failed(self, message: str) -> None:
        self._set_state("error")
        self._append_activity(f"Monitoring failed: {message}")
        QMessageBox.critical(self, "Monitoring failed", message)

    @Slot(str)
    def _worker_finished(self, session_id: str) -> None:
        self._last_session_id = session_id
        self._append_activity(f"Monitoring finished. Session ID: {session_id}")
        if self._state != "error":
            self._set_state("completed")

    @Slot()
    def _thread_finished(self) -> None:
        if self._worker is not None:
            self._worker.deleteLater()
        if self._worker_thread is not None:
            self._worker_thread.deleteLater()
        self._worker = None
        self._worker_thread = None
        if self._state not in {"monitoring", "paused", "starting"}:
            self._timer.stop()
        self._set_controls()

    def _run_quick_test(self) -> None:
        if self._is_active():
            self._append_activity("Quick test ignored because another test is already active.")
            return
        choice = self._confirm_quick_test()
        if choice == "one_hour":
            self._session_config = replace(
                self._session_config, duration_seconds=3600, cycle_count=None
            )
            self._refresh_session_summary()
            self._start_monitoring()
            return
        if choice != "quick":
            return

        self._recent_measurements.clear()
        self._completed_cycles = 0
        self._incident_count = 0
        self._last_measurement_monotonic = None
        self._started_monotonic = time.monotonic()
        self._paused_monotonic = None
        self._last_session_id = None
        self._last_quick_summary = None
        self._reset_metrics()
        self._set_state("quick_running")
        self._spinner.setRange(0, 10)
        self._spinner.setValue(0)
        self._spinner.setTextVisible(True)
        self._recording_label.setText("Recording: quick test")
        self._quick_summary_label.setText("Quick test running. Results are a snapshot.")
        self._append_activity("Quick test started.")

        app_config = replace(self._app_config, session=self._session_config)
        self._quick_worker_thread = QThread(self)
        self._quick_worker = QuickTestWorker(app_config)
        self._quick_worker.moveToThread(self._quick_worker_thread)
        self._quick_worker_thread.started.connect(self._quick_worker.run)
        self._quick_worker.activity.connect(self._append_activity)
        self._quick_worker.progress.connect(self._handle_quick_progress)
        self._quick_worker.measurement.connect(self._handle_measurement)
        self._quick_worker.failed.connect(self._quick_failed)
        self._quick_worker.finished.connect(self._quick_finished)
        self._quick_worker.failed.connect(self._quick_worker_thread.quit)
        self._quick_worker.finished.connect(self._quick_worker_thread.quit)
        self._quick_worker_thread.finished.connect(self._quick_thread_finished)
        self._quick_worker_thread.start()
        self._timer.start()

    def _confirm_quick_test(self) -> str:
        dialog = QMessageBox(self)
        dialog.setWindowTitle("Quick Test")
        dialog.setIcon(QMessageBox.Icon.Information)
        dialog.setText("Quick Test")
        dialog.setInformativeText(
            "This test performs one complete connection check, including latency, packet "
            "loss, DNS, website connectivity, download speed, and upload speed.\n\n"
            "It provides a useful snapshot of your connection, but a single test may miss "
            "intermittent problems.\n\n"
            "For stronger evidence when contacting your ISP, we recommend running "
            "continuous monitoring for at least 1 hour.\n\n"
            "Speed testing will temporarily use a significant portion of your internet "
            "connection."
        )
        quick = dialog.addButton("Run Quick Test", QMessageBox.ButtonRole.AcceptRole)
        one_hour = dialog.addButton("Start 1-Hour Test Instead", QMessageBox.ButtonRole.ActionRole)
        dialog.addButton("Cancel", QMessageBox.ButtonRole.RejectRole)
        dialog.exec()
        clicked = dialog.clickedButton()
        if clicked == quick:
            return "quick"
        if clicked == one_hour:
            return "one_hour"
        return "cancel"

    @Slot(int, int, str)
    def _handle_quick_progress(self, step: int, total: int, label: str) -> None:
        self._spinner.setRange(0, total)
        self._spinner.setValue(step)
        self._remaining_label.setText(f"Step {step} of {total}")
        self._current_operation.setText(f"Step {step} of {total} - {label}")

    @Slot(object)
    def _quick_finished(self, raw: object) -> None:
        summary = cast(QuickTestSummary, raw)
        self._last_quick_summary = summary
        self._last_session_id = summary.session_id
        self._quick_summary_label.setText(_format_quick_summary(summary))
        self.set_metric("Jitter", _optional_ms(summary.jitter_ms))
        self.set_metric("Download", _optional_mbps(summary.download_mbps, summary.download_percent))
        self.set_metric("Upload", _optional_mbps(summary.upload_mbps, summary.upload_percent))
        self._recording_label.setText("Recording: quick test saved")
        self._append_activity(f"Quick test finished. Session ID: {summary.session_id}")
        if self._state != "error":
            self._set_state("quick_completed")

    @Slot(str)
    def _quick_failed(self, message: str) -> None:
        self._set_state("error")
        self._append_activity(f"Quick test failed: {message}")
        QMessageBox.critical(self, "Quick test failed", message)

    @Slot()
    def _quick_thread_finished(self) -> None:
        if self._quick_worker is not None:
            self._quick_worker.deleteLater()
        if self._quick_worker_thread is not None:
            self._quick_worker_thread.deleteLater()
        self._quick_worker = None
        self._quick_worker_thread = None
        if self._state not in {"monitoring", "paused", "starting"}:
            self._timer.stop()
        self._set_controls()

    def _tick(self) -> None:
        self._update_timer_labels()
        if self._state in {"monitoring", "starting"}:
            if self._last_measurement_monotonic is None:
                if self._started_monotonic and time.monotonic() - self._started_monotonic > 15:
                    self._set_state("worker_not_responding")
                    self._append_activity("No measurements received for 15 seconds.")
            else:
                age = time.monotonic() - self._last_measurement_monotonic
                if age > max(15.0, self._session_config.latency_interval_seconds * 4):
                    self._set_state("worker_not_responding")
                    self._append_activity(f"No measurements received for {age:.0f} seconds.")

    def _set_state(self, state: str) -> None:
        self._state = state
        labels = {
            "ready": "Ready",
            "starting": "Starting...",
            "monitoring": "Monitoring active",
            "paused": "PAUSED",
            "stopping": "Stopping...",
            "quick_running": "Quick test running",
            "quick_completed": "Quick test complete",
            "completed": "Completed",
            "error": "Error",
            "worker_not_responding": "Worker not responding",
        }
        self._status_label.setText(labels.get(state, state.replace("_", " ").title()))
        self._spinner.setVisible(state in {"starting", "monitoring", "stopping", "quick_running"})
        if state in {"starting", "monitoring", "stopping"}:
            self._spinner.setRange(0, 0)
            self._spinner.setTextVisible(False)
        self._set_controls()
        self._update_timer_labels()

    def _set_controls(self) -> None:
        active = self._is_active()
        self._actions["start"].setEnabled(not active)
        self._actions["quick_test"].setEnabled(not active)
        self._actions["new_test"].setEnabled(not active)
        self._actions["settings"].setEnabled(not active)
        self._actions["pause"].setEnabled(
            self._state in {"monitoring", "paused", "worker_not_responding"}
        )
        self._actions["pause"].setText("Resume" if self._state == "paused" else "Pause")
        self._actions["stop"].setEnabled(active)
        self._actions["bad_now"].setEnabled(active)

    def _is_active(self) -> bool:
        return self._state in {
            "starting",
            "monitoring",
            "paused",
            "stopping",
            "worker_not_responding",
            "quick_running",
        }

    def _update_timer_labels(self) -> None:
        if self._started_monotonic is None:
            self._elapsed_label.setText("Elapsed: 00:00:00")
            self._remaining_label.setText("Mode: Ready")
            return
        elapsed = int(time.monotonic() - self._started_monotonic)
        self._elapsed_label.setText(f"Elapsed: {_format_duration(elapsed)}")
        if self._state == "quick_running":
            return
        if self._session_config.cycle_count is not None:
            self._remaining_label.setText(
                f"Cycle {self._completed_cycles} of {self._session_config.cycle_count}"
            )
        elif self._session_config.duration_seconds is not None:
            remaining = max(0, self._session_config.duration_seconds - elapsed)
            self._remaining_label.setText(f"Remaining: {_format_duration(remaining)}")
        else:
            self._remaining_label.setText("Mode: Continuous")
        if self._last_measurement_monotonic is None:
            self._last_measurement_label.setText("Last measurement: waiting for first result")
        else:
            age = max(0, int(time.monotonic() - self._last_measurement_monotonic))
            self._last_measurement_label.setText(f"Last measurement: {age} seconds ago")

    def _update_metrics(self) -> None:
        recent = list(self._recent_measurements)
        successes = [m for m in recent if m.success and m.rtt_ms is not None]
        failures = [m for m in recent[-50:] if not m.success]
        jitter_values = [m.jitter_ms for m in recent if m.jitter_ms is not None]
        gateway_recent = [m for m in recent[-25:] if _is_gateway_measurement(m)]
        internet_recent = [m for m in recent[-25:] if not _is_gateway_measurement(m)]
        if successes:
            self.set_metric("Latency", f"{successes[-1].rtt_ms:.0f} ms")
        self.set_metric(
            "Packet Loss", f"{(len(failures) / max(1, min(len(recent), 50)) * 100):.1f}%"
        )
        if jitter_values:
            self.set_metric("Jitter", f"{jitter_values[-1]:.0f} ms")
        self.set_metric("Connection to your router", _health_text(gateway_recent))
        self.set_metric("Internet connection", _health_text(internet_recent))
        if failures:
            self._incident_count += 1

    def _reset_metrics(self) -> None:
        for key in ("Latency", "Packet Loss", "Jitter"):
            self.set_metric(key, "Waiting for first result")
        self.set_metric("Download", "Waiting for quick test")
        self.set_metric("Upload", "Waiting for quick test")
        self.set_metric("Connection to your router", "Starting test...")
        self.set_metric("Internet connection", "Starting test...")
        self._measurement_table.setRowCount(0)
        self._recording_label.setText("Recording: starting")
        self._current_operation.setText("Currently testing: starting monitoring engine")

    def _add_measurement_row(self, measurement: Measurement) -> None:
        row = self._measurement_table.rowCount()
        self._measurement_table.insertRow(row)
        values = (
            measurement.timestamp_utc.astimezone().strftime("%H:%M:%S"),
            measurement.method.value.upper(),
            measurement.target_name,
            _measurement_result_text(measurement),
        )
        for column, value in enumerate(values):
            self._measurement_table.setItem(row, column, QTableWidgetItem(value))
        if row > 100:
            self._measurement_table.removeRow(0)

    def _refresh_session_summary(self) -> None:
        self._session_summary.setText(_format_session_config(self._session_config))

    @Slot(str)
    def _append_activity(self, message: str) -> None:
        timestamp = datetime.now().strftime("%H:%M:%S")
        self._current_operation.setText(f"Currently testing: {message}")
        self._activity_log.appendPlainText(f"{timestamp} — {message}")

    def _sync_button(self, action: QAction, button: QPushButton) -> None:
        button.setText(action.text())
        button.setEnabled(action.isEnabled())

    def closeEvent(self, event: QCloseEvent) -> None:
        if self._worker is not None:
            self._worker.stop()
        if self._quick_worker is not None:
            self._quick_worker.cancel()
        self.signals.stop_monitoring_requested.emit()
        super().closeEvent(event)


class ManualMarkerDialog(QDialog):
    """Small dialog for the Internet Feels Bad Now marker."""

    def __init__(self, parent: QWidget | None = None) -> None:
        super().__init__(parent)
        self.setWindowTitle("Internet Feels Bad Now")
        self._note = QLineEdit()
        self._note.setPlaceholderText("Optional note")

        buttons = QDialogButtonBox(
            QDialogButtonBox.StandardButton.Ok | QDialogButtonBox.StandardButton.Cancel
        )
        buttons.accepted.connect(self.accept)
        buttons.rejected.connect(self.reject)

        layout = QVBoxLayout(self)
        layout.addWidget(QLabel("Add a timestamped marker to the current monitoring session."))
        layout.addWidget(self._note)
        layout.addWidget(buttons)

    def note(self) -> str:
        return self._note.text().strip()


def create_app(argv: Sequence[str] | None = None) -> QApplication:
    """Create or return the QApplication without constructing any windows."""
    existing = QApplication.instance()
    if existing is not None:
        return cast(QApplication, existing)

    app = QApplication(list(argv) if argv is not None else sys.argv)
    app.setApplicationName(APP_DISPLAY_NAME)
    app.setOrganizationName("DQOPR")
    _apply_preferred_style(app)
    return app


def create_main_window(app_config: AppConfig | None = None) -> MainWindow:
    """Create the main window for integration tests or application startup."""
    return MainWindow(app_config=app_config)


def main(argv: Sequence[str] | None = None) -> int:
    """Start the GUI application."""
    app = create_app(argv)
    window = create_main_window()
    window.show()
    return int(app.exec())


def _speed_spin_box(value: float | None) -> QDoubleSpinBox:
    widget = QDoubleSpinBox()
    widget.setRange(0.1, 100_000.0)
    widget.setDecimals(1)
    widget.setSingleStep(10.0)
    widget.setSuffix(" Mbps")
    widget.setValue(value if value is not None else 100.0)
    return widget


def _interval_spin_box(value: float, suffix: str) -> QDoubleSpinBox:
    widget = QDoubleSpinBox()
    widget.setRange(0.1, 86_400.0)
    widget.setDecimals(1)
    widget.setSingleStep(1.0)
    widget.setSuffix(suffix)
    widget.setValue(value)
    return widget


def _action(parent: QObject, text: str, icon: QIcon | None = None) -> QAction:
    action = QAction(text, parent)
    if icon is not None:
        action.setIcon(icon)
    return action


def _metric_box(name: str, value: str) -> QGroupBox:
    box = QGroupBox(name)
    layout = QVBoxLayout(box)
    label = QLabel(value)
    label.setObjectName("metricValue")
    label.setAlignment(Qt.AlignmentFlag.AlignCenter)
    label.setStyleSheet("font-size: 18px; font-weight: 600;")
    layout.addWidget(label)
    return box


def _placeholder_table(title: str, columns: tuple[str, ...]) -> QGroupBox:
    group = QGroupBox(title)
    layout = QVBoxLayout(group)
    table = QTableWidget(0, len(columns))
    table.setHorizontalHeaderLabels(columns)
    table.horizontalHeader().setSectionResizeMode(QHeaderView.ResizeMode.Stretch)
    table.verticalHeader().setVisible(False)
    table.setAlternatingRowColors(True)
    table.setSelectionBehavior(QTableWidget.SelectionBehavior.SelectRows)
    table.setEditTriggers(QTableWidget.EditTrigger.NoEditTriggers)
    layout.addWidget(table)
    return group


def _format_duration(total_seconds: int) -> str:
    hours, remainder = divmod(max(0, total_seconds), 3600)
    minutes, seconds = divmod(remainder, 60)
    return f"{hours:02}:{minutes:02}:{seconds:02}"


def _is_gateway_measurement(measurement: Measurement) -> bool:
    return measurement.target_name == "Local Gateway"


def _health_text(measurements: list[Measurement]) -> str:
    if not measurements:
        return "Waiting for first result"
    recent = measurements[-10:]
    if all(measurement.success for measurement in recent):
        return "Healthy"
    failed_count = len([measurement for measurement in recent if not measurement.success])
    if failed_count == len(recent):
        return "Offline"
    return f"Degraded ({failed_count}/{len(recent)} failed)"


def _measurement_result_text(measurement: Measurement) -> str:
    if not measurement.success:
        reason = measurement.error_type or measurement.error_message or "failed"
        return f"Failed: {reason}"
    if measurement.method == ProbeMethod.DNS and measurement.dns_duration_ms is not None:
        return f"{measurement.dns_duration_ms:.0f} ms DNS"
    if (
        measurement.method == ProbeMethod.HTTPS
        and measurement.https_response_duration_ms is not None
    ):
        return f"{measurement.https_response_duration_ms:.0f} ms HTTPS"
    if measurement.method == ProbeMethod.TCP and measurement.tcp_connect_duration_ms is not None:
        return f"{measurement.tcp_connect_duration_ms:.0f} ms TCP"
    if measurement.rtt_ms is not None:
        return f"{measurement.rtt_ms:.0f} ms"
    return "Succeeded"


def _format_quick_summary(summary: QuickTestSummary) -> str:
    lines = [
        "Quick Test Complete" if summary.completed else "Quick Test Incomplete",
        f"Overall result: {summary.overall}",
        f"Local router: {summary.local_router_status}",
        f"Latency: {_optional_ms(summary.average_latency_ms)} average",
        f"Peak latency: {_optional_ms(summary.peak_latency_ms)}",
        f"Jitter: {_optional_ms(summary.jitter_ms)}",
        f"Packet loss: {summary.packet_loss_percent:.1f}%",
        f"DNS: {summary.dns_result}",
        f"HTTPS: {summary.https_result}",
        f"Download: {_optional_mbps(summary.download_mbps, summary.download_percent)}",
        f"Upload: {_optional_mbps(summary.upload_mbps, summary.upload_percent)}",
        f"Test duration: {summary.duration_seconds:.1f} seconds",
    ]
    if summary.detected_problems:
        lines.append("Detected issue: " + summary.detected_problems[0])
    else:
        lines.append("Detected issue: none in this snapshot")
    lines.append("A quick test is a snapshot and may miss intermittent problems.")
    return "\n".join(lines)


def _optional_ms(value: float | None) -> str:
    return "unavailable" if value is None else f"{value:.0f} ms"


def _optional_mbps(value: float | None, percent: float | None) -> str:
    if value is None:
        return "unavailable"
    if percent is None:
        return f"{value:.1f} Mbps"
    return f"{value:.1f} Mbps ({percent:.1f}% of contracted speed)"


def _format_session_config(config: SessionConfig) -> str:
    download = _speed_text(config.contracted_download_mbps)
    upload = _speed_text(config.contracted_upload_mbps)
    duration = (
        "continuous" if config.duration_seconds is None else f"{config.duration_seconds // 60} min"
    )
    cycles = "no cycle limit" if config.cycle_count is None else f"{config.cycle_count} cycles"
    speedtest = (
        f"enabled every {int(config.speedtest_interval_seconds // 60)} min"
        if config.speedtest_enabled
        else "disabled"
    )
    targets = ", ".join(target.name for target in config.targets if target.enabled) or "no targets"
    return (
        f"Plan: {download} down / {upload} up. Duration: {duration}, {cycles}. "
        f"Latency every {config.latency_interval_seconds:g}s, DNS every "
        f"{config.dns_interval_seconds:g}s, HTTPS every {config.https_interval_seconds:g}s. "
        f"Speed tests {speedtest}. Targets: {targets}."
    )


def _speed_text(value: float | None) -> str:
    if value is None:
        return "unknown"
    return f"{value:g} Mbps"


def _apply_preferred_style(app: QApplication) -> None:
    styles = QStyleFactory.keys()
    available_styles = {style.lower(): style for style in styles}
    for candidate in ("windowsvista", "windows", "fusion"):
        style_name = available_styles.get(candidate)
        if style_name is not None:
            app.setStyle(style_name)
            return


def config_with_targets(config: SessionConfig, targets: Sequence[Target]) -> SessionConfig:
    """Return a copy of config with a concrete target sequence.

    This helper keeps target editing out of the initial shell while giving backend
    integration code a typed path for supplying discovered gateway/public targets.
    """
    return replace(config, targets=tuple(targets))


if __name__ == "__main__":
    raise SystemExit(main())
