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
