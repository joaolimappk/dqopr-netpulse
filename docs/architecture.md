# Architecture

DQOPR NetPulse separates measurement collection, diagnosis, persistence, reporting, and user interface code so each part can be tested independently.

## Current State

The current repository includes:

- Package metadata in `pyproject.toml`.
- Domain models in `src/dqopr_netpulse/models.py`.
- Configuration defaults and validation in `src/dqopr_netpulse/configuration.py`.
- SQLite storage with schema migration 1 in `src/dqopr_netpulse/storage.py`.
- Monitoring engine callbacks for live state, activity, and measurement updates.
- One-cycle quick-test orchestration that reuses the monitoring engine, storage, speed-test wrapper, exports, and reports.
- PySide6 GUI with a background worker thread connected to the monitoring engine.
- CSV/ZIP exports, graph generation, and self-contained HTML report generation.

## Target Architecture

```text
GUI / CLI
  |
  v
Session controller
  |
  +-- Platform networking adapters
  +-- Probe scheduler
  +-- Probe implementations
  +-- Incident engine
  +-- SQLite storage
  +-- CSV, graph, and HTML report generation
```

## Module Responsibilities

| Area | Responsibility |
| --- | --- |
| `configuration` | Defaults, user settings, threshold validation, data directory selection. |
| `models` | Typed records shared by probes, storage, reports, and GUI. |
| `storage` | SQLite connection management, schema migrations, transactions, and query helpers. |
| `monitoring` | Scheduling, session lifecycle, probe orchestration, pause/resume/stop semantics. |
| `quick_test` | One-cycle quick-test orchestration, progress callbacks, speed-test capture, and summary generation. |
| `probes` | ICMP, TCP, DNS, HTTPS, route, and speed-test checks through mockable interfaces. |
| `diagnostics` | Incident grouping, fault-domain classification, severity, confidence, and explanations. |
| `reports` | Self-contained HTML report generation and printable output. |
| `graphs` | Latency, jitter, loss, speed, DNS, HTTPS, gateway comparison, Wi-Fi, and incident graphs. |
| `exports` | Stable UTF-8 CSV and ZIP exports. |
| `gui` | PySide6 startup wizard, live monitoring dashboard, background worker, reports, settings, and help. |
| `platform_windows` | Windows network interface, Wi-Fi signal, sleep/resume, route, and signature-aware packaging helpers. |

## Data Flow

1. The user runs a quick test or creates a monitoring session through the GUI or CLI.
2. Configuration is validated and serialized into the session record.
3. Probes run on configured intervals using monotonic clocks for timing.
4. Measurements are written to SQLite immediately inside transactions.
5. The incident engine groups related failures using rolling windows and supporting measurement IDs.
6. Exports and reports read from SQLite instead of relying on in-memory state.
7. The GUI receives state, activity, and measurement callbacks from the background worker and reflects stored measurements as they arrive.

## Storage

SQLite is the durable source of truth. The initial schema stores test sessions, measurements, speed tests, incidents, and manual markers. Future migrations should add network-interface events, configuration, and application logs without mutating historical semantics silently.

## Platform Boundaries

Windows-specific behavior must live behind adapters so Linux development and unit tests can use fakes. The application should run as a standard user whenever possible. If a measurement benefits from elevated privileges, provide a non-administrative fallback and clearly document the limitation.

## Security And Privacy Boundaries

NetPulse does not inspect packet contents, does not install drivers, and does not attempt to bypass endpoint security. Generated reports should default to private mode and redact user-identifying network fields unless the user explicitly chooses technical detail.
