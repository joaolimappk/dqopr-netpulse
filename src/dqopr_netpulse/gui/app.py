"""Windows-oriented PySide6 application shell for DQOPR NetPulse.

The GUI intentionally exposes signals and small factory functions so monitoring,
storage, graphing, and reporting backends can be integrated without making import
time perform any application startup work.
"""

from __future__ import annotations

import sys
from collections.abc import Sequence
from dataclasses import replace
from typing import cast

from PySide6.QtCore import QObject, Qt, Signal
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
    QPushButton,
    QSpinBox,
    QStatusBar,
    QStyle,
    QStyleFactory,
    QTableWidget,
    QToolBar,
    QVBoxLayout,
    QWidget,
    QWizard,
    QWizardPage,
)

from dqopr_netpulse.configuration import AppConfig, default_data_dir, validate_session_config
from dqopr_netpulse.models import SessionConfig, Target

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


class MainWindow(QMainWindow):
    """Main application window with placeholder actions for backend integration."""

    def __init__(
        self,
        app_config: AppConfig | None = None,
        parent: QWidget | None = None,
    ) -> None:
        super().__init__(parent)
        self.signals = BackendSignals(self)
        self._app_config = app_config or AppConfig(data_dir=default_data_dir())
        self._session_config = self._app_config.session
        self._status = QLabel("Idle")
        self._session_summary = QLabel()
        self._session_summary.setWordWrap(True)
        self._activity_log = QPlainTextEdit()
        self._activity_log.setReadOnly(True)
        self._metric_labels: dict[str, QLabel] = {}
        self._actions: dict[str, QAction] = {}

        self.setWindowTitle(APP_DISPLAY_NAME)
        self.setMinimumSize(1040, 680)
        self._build_actions()
        self._build_menu_bar()
        self._build_toolbar()
        self._build_central_widget()
        self._build_status_bar()
        self._connect_actions()
        self._refresh_session_summary()
        self._append_log("Ready. Create a new test or start monitoring with the current settings.")

    @property
    def session_config(self) -> SessionConfig:
        """Return the current wizard-produced session configuration."""
        return self._session_config

    def update_dashboard_status(self, status: str) -> None:
        """Update dashboard status text from an external controller."""
        self._status.setText(status)
        self._append_log(status)

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
            "incidents": _action(self, "View Incidents"),
            "graphs": _action(self, "View Graphs"),
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
        monitor_menu.addAction(self._actions["pause"])
        monitor_menu.addAction(self._actions["stop"])
        monitor_menu.addSeparator()
        monitor_menu.addAction(self._actions["bad_now"])

        view_menu = cast(QMenu, self.menuBar().addMenu("&View"))
        view_menu.addAction(self._actions["incidents"])
        view_menu.addAction(self._actions["graphs"])

        tools_menu = cast(QMenu, self.menuBar().addMenu("&Tools"))
        tools_menu.addAction(self._actions["settings"])

        help_menu = cast(QMenu, self.menuBar().addMenu("&Help"))
        help_menu.addAction(self._actions["help"])
        help_menu.addAction(self._actions["about"])

    def _build_toolbar(self) -> None:
        toolbar = QToolBar("Monitoring")
        toolbar.setMovable(False)
        toolbar.setToolButtonStyle(Qt.ToolButtonStyle.ToolButtonTextBesideIcon)
        toolbar_actions = (
            "new_test",
            "start",
            "pause",
            "stop",
            "bad_now",
            "incidents",
            "graphs",
            "report",
        )
        for key in toolbar_actions:
            toolbar.addAction(self._actions[key])
            if key in {"new_test", "stop", "graphs"}:
                toolbar.addSeparator()
        self.addToolBar(toolbar)

    def _build_central_widget(self) -> None:
        root = QWidget()
        outer = QVBoxLayout(root)
        outer.setContentsMargins(16, 16, 16, 16)
        outer.setSpacing(12)

        header = QHBoxLayout()
        title = QLabel(APP_DISPLAY_NAME)
        title.setObjectName("windowTitle")
        title.setStyleSheet("font-size: 22px; font-weight: 600;")
        header.addWidget(title)
        header.addStretch(1)
        header.addWidget(QLabel("Status:"))
        header.addWidget(self._status)
        outer.addLayout(header)

        summary_group = QGroupBox("Current Session")
        summary_layout = QVBoxLayout(summary_group)
        summary_layout.addWidget(self._session_summary)
        outer.addWidget(summary_group)

        metric_grid = QGridLayout()
        metric_grid.setSpacing(10)
        metrics = (
            ("Gateway", "Waiting"),
            ("Public Targets", "Waiting"),
            ("Latency", "-- ms"),
            ("Packet Loss", "-- %"),
            ("Jitter", "-- ms"),
            ("DNS", "Waiting"),
            ("HTTPS", "Waiting"),
            ("Speed", "Not scheduled"),
        )
        for index, (name, value) in enumerate(metrics):
            box = _metric_box(name, value)
            self._metric_labels[name] = cast(QLabel, box.findChild(QLabel, "metricValue"))
            metric_grid.addWidget(box, index // 4, index % 4)
        outer.addLayout(metric_grid)

        quick_actions = QGridLayout()
        quick_action_keys = (
            "new_test",
            "start",
            "pause",
            "stop",
            "bad_now",
            "incidents",
            "graphs",
            "report",
            "export_csv",
            "open_session",
            "settings",
            "help",
            "about",
        )
        for index, key in enumerate(quick_action_keys):
            button = QPushButton(self._actions[key].text())
            button.setIcon(self._actions[key].icon())
            button.clicked.connect(self._actions[key].trigger)
            quick_actions.addWidget(button, index // 4, index % 4)
        outer.addLayout(quick_actions)

        tables = QHBoxLayout()
        tables.addWidget(
            _placeholder_table("Recent Incidents", ("Time", "Severity", "Classification"))
        )
        tables.addWidget(_placeholder_table("Recent Measurements", ("Time", "Target", "Result")))
        outer.addLayout(tables, stretch=1)

        log_group = QGroupBox("Activity")
        log_layout = QVBoxLayout(log_group)
        log_layout.addWidget(self._activity_log)
        outer.addWidget(log_group, stretch=1)

        self.setCentralWidget(root)

    def _build_status_bar(self) -> None:
        status_bar = QStatusBar()
        status_bar.showMessage(f"Data folder: {self._app_config.data_dir}")
        self.setStatusBar(status_bar)

    def _connect_actions(self) -> None:
        self._actions["new_test"].triggered.connect(self._new_test)
        self._actions["start"].triggered.connect(self._start_monitoring)
        self._actions["pause"].triggered.connect(self._pause_monitoring)
        self._actions["stop"].triggered.connect(self._stop_monitoring)
        self._actions["bad_now"].triggered.connect(self._manual_marker)
        self._actions["incidents"].triggered.connect(self._view_incidents)
        self._actions["graphs"].triggered.connect(self._view_graphs)
        self._actions["report"].triggered.connect(self._generate_report)
        self._actions["export_csv"].triggered.connect(self._export_csv)
        self._actions["open_session"].triggered.connect(self._open_previous_session)
        self._actions["settings"].triggered.connect(self._settings)
        self._actions["help"].triggered.connect(self._help)
        self._actions["about"].triggered.connect(self._about)

    def _new_test(self) -> None:
        wizard = StartupWizard(self._session_config, self)
        if wizard.exec() == QDialog.DialogCode.Accepted:
            try:
                self._session_config = wizard.session_config()
            except ValueError as exc:
                QMessageBox.warning(self, "Invalid settings", str(exc))
                return
            self._refresh_session_summary()
            self.signals.new_test_requested.emit(self._session_config)
            self._append_log("New test configured.")

    def _start_monitoring(self) -> None:
        self._status.setText("Monitoring")
        self.signals.start_monitoring_requested.emit()
        self._append_log("Start monitoring requested.")

    def _pause_monitoring(self) -> None:
        self._status.setText("Paused")
        self.signals.pause_monitoring_requested.emit()
        self._append_log("Pause requested.")

    def _stop_monitoring(self) -> None:
        self._status.setText("Stopped")
        self.signals.stop_monitoring_requested.emit()
        self._append_log("Stop requested.")

    def _manual_marker(self) -> None:
        dialog = ManualMarkerDialog(self)
        if dialog.exec() == QDialog.DialogCode.Accepted:
            note = dialog.note()
            self.signals.manual_marker_requested.emit(note)
            self._append_log("Manual quality marker recorded.")

    def _view_incidents(self) -> None:
        self.signals.view_incidents_requested.emit()
        QMessageBox.information(self, "Incidents", "Incident view is ready for backend data.")

    def _view_graphs(self) -> None:
        self.signals.view_graphs_requested.emit()
        QMessageBox.information(self, "Graphs", "Graph view is ready for backend data.")

    def _generate_report(self) -> None:
        self.signals.generate_report_requested.emit()
        QMessageBox.information(
            self,
            "ISP Report",
            "Report generation is ready for backend integration.",
        )

    def _export_csv(self) -> None:
        path, _ = QFileDialog.getSaveFileName(
            self,
            "Export CSV",
            "netpulse-session.csv",
            "CSV files (*.csv)",
        )
        if path:
            self.signals.export_csv_requested.emit(path)
            self._append_log(f"CSV export requested: {path}")

    def _open_previous_session(self) -> None:
        path, _ = QFileDialog.getOpenFileName(
            self,
            "Open Previous Session",
            str(self._app_config.data_dir),
            "NetPulse database (*.sqlite3);;All files (*)",
        )
        if path:
            self.signals.open_previous_session_requested.emit(path)
            self._append_log(f"Open previous session requested: {path}")

    def _settings(self) -> None:
        wizard = StartupWizard(self._session_config, self)
        if wizard.exec() == QDialog.DialogCode.Accepted:
            try:
                self._session_config = wizard.session_config()
            except ValueError as exc:
                QMessageBox.warning(self, "Invalid settings", str(exc))
                return
            self._refresh_session_summary()
            self.signals.settings_requested.emit(self._session_config)
            self._append_log("Settings updated.")

    def _help(self) -> None:
        self.signals.help_requested.emit()
        QMessageBox.information(
            self,
            "Help",
            "Use New Test to configure evidence collection, then Start Monitoring.",
        )

    def _about(self) -> None:
        self.signals.about_requested.emit()
        QMessageBox.about(
            self,
            f"About {APP_DISPLAY_NAME}",
            f"{APP_DISPLAY_NAME}\nInternet Quality Monitor and ISP Evidence Reporter",
        )

    def _refresh_session_summary(self) -> None:
        self._session_summary.setText(_format_session_config(self._session_config))

    def _append_log(self, message: str) -> None:
        self._activity_log.appendPlainText(message)

    def closeEvent(self, event: QCloseEvent) -> None:
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
