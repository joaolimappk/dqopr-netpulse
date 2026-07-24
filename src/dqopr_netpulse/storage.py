"""SQLite persistence with explicit schema migrations."""

from __future__ import annotations

import json
import sqlite3
from collections.abc import Iterator
from contextlib import contextmanager
from datetime import UTC, datetime
from pathlib import Path

from dqopr_netpulse.models import Incident, ManualMarker, Measurement, SpeedTestResult

SCHEMA_VERSION = 1


class NetPulseStore:
    """Durable SQLite store used by both CLI and GUI."""

    def __init__(self, path: Path) -> None:
        self.path = path
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._connection = sqlite3.connect(self.path)
        self._connection.row_factory = sqlite3.Row
        self._connection.execute("PRAGMA journal_mode=WAL")
        self._connection.execute("PRAGMA foreign_keys=ON")
        self.migrate()

    def close(self) -> None:
        self._connection.close()

    @contextmanager
    def transaction(self) -> Iterator[sqlite3.Connection]:
        with self._connection:
            yield self._connection

    def migrate(self) -> None:
        with self.transaction() as db:
            db.execute("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)")
            row = db.execute(
                "SELECT version FROM schema_version ORDER BY version DESC LIMIT 1"
            ).fetchone()
            current = int(row["version"]) if row else 0
            if current < 1:
                _migration_001(db)
                db.execute("DELETE FROM schema_version")
                db.execute("INSERT INTO schema_version(version) VALUES (?)", (SCHEMA_VERSION,))

    def create_session(self, session_id: str, config_json: str) -> None:
        with self.transaction() as db:
            db.execute(
                """
                INSERT INTO test_sessions(id, started_at_utc, status, config_json)
                VALUES (?, ?, 'running', ?)
                """,
                (session_id, _iso(datetime.now(UTC)), config_json),
            )

    def finish_session(self, session_id: str, status: str = "completed") -> None:
        with self.transaction() as db:
            db.execute(
                """
                UPDATE test_sessions
                SET ended_at_utc = ?, status = ?
                WHERE id = ?
                """,
                (_iso(datetime.now(UTC)), status, session_id),
            )

    def add_measurement(self, measurement: Measurement) -> int:
        payload = _dataclass_to_storage(measurement)
        columns = ", ".join(payload.keys())
        placeholders = ", ".join("?" for _ in payload)
        with self.transaction() as db:
            cursor = db.execute(
                f"INSERT INTO measurements ({columns}) VALUES ({placeholders})",
                tuple(payload.values()),
            )
            if cursor.lastrowid is None:
                raise RuntimeError("SQLite did not return a measurement row id.")
            return int(cursor.lastrowid)

    def add_speed_test(self, result: SpeedTestResult) -> int:
        payload = _dataclass_to_storage(result)
        columns = ", ".join(payload.keys())
        placeholders = ", ".join("?" for _ in payload)
        with self.transaction() as db:
            cursor = db.execute(
                f"INSERT INTO speed_tests ({columns}) VALUES ({placeholders})",
                tuple(payload.values()),
            )
            if cursor.lastrowid is None:
                raise RuntimeError("SQLite did not return a speed-test row id.")
            return int(cursor.lastrowid)

    def add_incident(self, incident: Incident) -> str:
        payload = _dataclass_to_storage(incident)
        with self.transaction() as db:
            db.execute(
                """
                INSERT OR REPLACE INTO incidents (
                  id, session_id, incident_type, start_time_utc, end_time_utc, severity,
                  affected_tests_json, affected_targets_json, worst_latency_ms,
                  packet_loss_percent, consecutive_failures, local_gateway_status,
                  external_target_status, dns_status, https_status, speedtest_context,
                  probable_fault_domain, confidence, explanation, supporting_measurement_ids_json
                ) VALUES (
                  :id, :session_id, :incident_type, :start_time_utc, :end_time_utc, :severity,
                  :affected_tests, :affected_targets, :worst_latency_ms,
                  :packet_loss_percent, :consecutive_failures, :local_gateway_status,
                  :external_target_status, :dns_status, :https_status, :speedtest_context,
                  :probable_fault_domain, :confidence, :explanation, :supporting_measurement_ids
                )
                """,
                payload,
            )
        return incident.id

    def add_marker(self, marker: ManualMarker) -> int:
        payload = _dataclass_to_storage(marker)
        with self.transaction() as db:
            cursor = db.execute(
                """
                INSERT INTO manual_markers (
                  session_id, timestamp_utc, note, metrics_snapshot_json,
                  active_interface_name, wifi_signal_percent, gateway_status,
                  public_target_status
                ) VALUES (
                  :session_id, :timestamp_utc, :note, :metrics_snapshot,
                  :active_interface_name, :wifi_signal_percent, :gateway_status,
                  :public_target_status
                )
                """,
                payload,
            )
            if cursor.lastrowid is None:
                raise RuntimeError("SQLite did not return a marker row id.")
            return int(cursor.lastrowid)

    def list_measurements(self, session_id: str) -> list[sqlite3.Row]:
        return list(
            self._connection.execute(
                "SELECT * FROM measurements WHERE session_id = ? ORDER BY timestamp_utc, id",
                (session_id,),
            )
        )

    def list_speed_tests(self, session_id: str) -> list[sqlite3.Row]:
        return list(
            self._connection.execute(
                "SELECT * FROM speed_tests WHERE session_id = ? ORDER BY timestamp_utc, id",
                (session_id,),
            )
        )

    def list_incidents(self, session_id: str) -> list[sqlite3.Row]:
        return list(
            self._connection.execute(
                "SELECT * FROM incidents WHERE session_id = ? ORDER BY start_time_utc, id",
                (session_id,),
            )
        )

    def list_markers(self, session_id: str) -> list[sqlite3.Row]:
        return list(
            self._connection.execute(
                "SELECT * FROM manual_markers WHERE session_id = ? ORDER BY timestamp_utc, id",
                (session_id,),
            )
        )


def _migration_001(db: sqlite3.Connection) -> None:
    db.executescript(
        """
        CREATE TABLE IF NOT EXISTS test_sessions (
            id TEXT PRIMARY KEY,
            started_at_utc TEXT NOT NULL,
            ended_at_utc TEXT,
            status TEXT NOT NULL,
            config_json TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS measurements (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id TEXT NOT NULL REFERENCES test_sessions(id) ON DELETE CASCADE,
            timestamp_utc TEXT NOT NULL,
            local_timestamp TEXT,
            timezone TEXT,
            target_name TEXT NOT NULL,
            target_address TEXT NOT NULL,
            method TEXT NOT NULL,
            sequence INTEGER NOT NULL,
            success INTEGER NOT NULL,
            rtt_ms REAL,
            min_latency_ms REAL,
            max_latency_ms REAL,
            avg_latency_ms REAL,
            median_latency_ms REAL,
            jitter_ms REAL,
            packet_loss_percent REAL,
            consecutive_loss_count INTEGER NOT NULL,
            timeout_ms REAL,
            dns_duration_ms REAL,
            tcp_connect_duration_ms REAL,
            https_response_duration_ms REAL,
            http_status_code INTEGER,
            active_interface_name TEXT,
            interface_type TEXT,
            wifi_signal_percent INTEGER,
            gateway_address TEXT,
            public_ip_masked TEXT,
            vpn_detected INTEGER NOT NULL,
            error_type TEXT,
            error_message TEXT,
            incident_id TEXT,
            during_manual_marker INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS idx_measurements_session_time
            ON measurements(session_id, timestamp_utc);
        CREATE TABLE IF NOT EXISTS speed_tests (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id TEXT NOT NULL REFERENCES test_sessions(id) ON DELETE CASCADE,
            timestamp_utc TEXT NOT NULL,
            download_mbps REAL,
            upload_mbps REAL,
            latency_ms REAL,
            server_name TEXT,
            server_location TEXT,
            methodology TEXT NOT NULL,
            success INTEGER NOT NULL,
            error_message TEXT
        );
        CREATE TABLE IF NOT EXISTS incidents (
            id TEXT PRIMARY KEY,
            session_id TEXT NOT NULL REFERENCES test_sessions(id) ON DELETE CASCADE,
            incident_type TEXT NOT NULL,
            start_time_utc TEXT NOT NULL,
            end_time_utc TEXT,
            duration_seconds REAL
                GENERATED ALWAYS AS (
                    CASE WHEN end_time_utc IS NULL THEN NULL
                    ELSE (julianday(end_time_utc) - julianday(start_time_utc)) * 86400.0 END
                ) VIRTUAL,
            severity TEXT NOT NULL,
            affected_tests_json TEXT NOT NULL,
            affected_targets_json TEXT NOT NULL,
            worst_latency_ms REAL,
            packet_loss_percent REAL,
            consecutive_failures INTEGER NOT NULL,
            local_gateway_status TEXT,
            external_target_status TEXT,
            dns_status TEXT,
            https_status TEXT,
            speedtest_context TEXT,
            probable_fault_domain TEXT NOT NULL,
            confidence TEXT NOT NULL,
            explanation TEXT NOT NULL,
            supporting_measurement_ids_json TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS manual_markers (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id TEXT NOT NULL REFERENCES test_sessions(id) ON DELETE CASCADE,
            timestamp_utc TEXT NOT NULL,
            note TEXT,
            metrics_snapshot_json TEXT NOT NULL,
            active_interface_name TEXT,
            wifi_signal_percent INTEGER,
            gateway_status TEXT,
            public_target_status TEXT
        );
        CREATE TABLE IF NOT EXISTS network_interface_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            session_id TEXT NOT NULL REFERENCES test_sessions(id) ON DELETE CASCADE,
            timestamp_utc TEXT NOT NULL,
            event_type TEXT NOT NULL,
            interface_name TEXT,
            interface_type TEXT,
            local_ip TEXT,
            gateway_ip TEXT,
            dns_servers_json TEXT NOT NULL,
            wifi_signal_percent INTEGER,
            vpn_detected INTEGER NOT NULL
        );
        CREATE TABLE IF NOT EXISTS configuration (
            key TEXT PRIMARY KEY,
            value_json TEXT NOT NULL,
            updated_at_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS application_logs (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp_utc TEXT NOT NULL,
            level TEXT NOT NULL,
            logger TEXT NOT NULL,
            message TEXT NOT NULL,
            context_json TEXT NOT NULL
        );
        """
    )


def _iso(value: datetime) -> str:
    return value.astimezone(UTC).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _dataclass_to_storage(value: object) -> dict[str, object]:
    data: dict[str, object] = {}
    for key, raw in vars(value).items():
        if isinstance(raw, datetime):
            data[key] = raw.isoformat().replace("+00:00", "Z")
        elif isinstance(raw, tuple):
            data[key] = json.dumps(list(raw), separators=(",", ":"))
        elif isinstance(raw, dict):
            data[key] = json.dumps(raw, separators=(",", ":"), sort_keys=True)
        elif hasattr(raw, "value"):
            data[key] = raw.value
        elif isinstance(raw, bool):
            data[key] = int(raw)
        else:
            data[key] = raw
    return data
