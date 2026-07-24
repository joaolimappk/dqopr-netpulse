# Changelog

All notable changes to DQOPR NetPulse will be documented in this file.

The format is based on Keep a Changelog, and this project uses semantic versioning while it matures.

## [Unreleased]

### Added

- Initial open-source project documentation.
- Apache License 2.0.
- Windows packaging and signing documentation.
- Inno Setup installer scaffolding.
- GitHub issue and pull request templates.
- Windows build and validation workflow.
- Release validation script for tests, release metadata, secret scans, checksums, optional executable and installer builds, and signature verification.

## [0.2.0] - 2026-07-24

### Added

- Prominent **Run Quick Test** dashboard action for a one-cycle diagnostic snapshot.
- Quick-test confirmation dialog with options to run the snapshot, start a 1-hour monitoring test, or cancel.
- Quick-test progress stages, elapsed timer, activity-feed updates, cancellation through Stop, and final summary text.
- One-cycle quick-test orchestration that reuses the monitoring engine, probe runner, SQLite storage, speed-test wrapper, CSV export, and HTML report generation.
- Quick-test speed-test capture with graceful skipped-result storage when the speedtest CLI is unavailable.
- Automated quick-test coverage for progress callbacks, speed-test persistence, summary generation, and contracted-speed percentages.

### Changed

- Dashboard export and report actions now operate on the most recent saved test session instead of only emitting placeholder signals.

### Fixed

- Prevented quick testing and continuous monitoring from starting at the same time.
- Prevented duplicate quick-test starts while a test is active.
- Ensured unknown contracted speeds do not produce percentage-of-plan values in quick-test summaries.

## [0.1.1] - 2026-07-24

### Added

- Live PySide6 dashboard worker that starts the monitoring engine from the GUI.
- State, activity, and measurement callbacks from monitoring sessions for UI updates.
- Dashboard timer, current operation text, watchdog messaging, recent measurement table, and tabbed layout.
- Regression coverage proving a monitoring session emits live callbacks and persists measurements.

### Changed

- Simplified the main dashboard to focus on current health, live activity, and primary monitoring controls.
- Improved generated-artifact ignore rules for logs, packet captures, HAR files, temporary files, and backups.

### Fixed

- Fixed the GUI-only start path that showed "Monitoring" while no probes were running and all cards stayed waiting.
- Prevented duplicate Start actions while a monitoring session is already active.
- Ensured injected network-interface snapshots are used when adding the detected gateway target.
- Ensured DNS and HTTPS checks use a public target when a local gateway target is inserted.

## [0.1.0] - 2026-07-24

### Added

- Initial package metadata.
- Core typed models for targets, measurements, speed tests, incidents, manual markers, and session configuration.
- Configuration defaults and validation.
- SQLite storage layer with schema versioning and migration 1.
