# DQOPR NetPulse C# Migration Plan

Status: active on `csharp-rewrite`.

The C# rewrite preserves the Python/PySide6 alpha as a reference implementation while building a native Windows application with corrected monitoring methodology. The goal is not a line-by-line translation. The goal is a defensible Windows evidence collector that separates probe types, stores data durably, explains uncertainty, and packages cleanly for nontechnical users.

## Target Stack

- C# and .NET 10 LTS
- WPF desktop UI
- MVVM architecture
- SQLite via `Microsoft.Data.Sqlite`
- Native Windows APIs where appropriate
- xUnit automated tests
- Windows installer in a later phase
- Apache License 2.0

## Architecture

Solution: `DQOPR.NetPulse.sln`

- `src/DQOPR.NetPulse.App`: WPF UI, navigation, ViewModels, commands, dialogs, dependency injection.
- `src/DQOPR.NetPulse.Core`: domain models, configuration, scheduling contracts, Quick Test options.
- `src/DQOPR.NetPulse.Networking`: ICMP, TCP, DNS, HTTPS, route, public-IP, and speed-test probes.
- `src/DQOPR.NetPulse.Diagnostics`: jitter, packet loss, baselines, incident correlation, fault-domain reasoning.
- `src/DQOPR.NetPulse.Storage`: SQLite schema, migrations, persistence, retention.
- `src/DQOPR.NetPulse.Reporting`: CSV, ZIP, graphs, self-contained HTML reports.
- `src/DQOPR.NetPulse.Platform.Windows`: adapters, gateway detection, power/interface events, app-data paths.
- `tests/*`: methodology, unit, storage, integration, and later Windows end-to-end tests.

## Migration Stages

1. Architecture and methodology baseline.
2. Core models and independent scheduling.
3. Statistical correctness for packet loss, jitter, and latency percentiles.
4. Probe interfaces and cancellation-safe probe runners.
5. SQLite schema and migration strategy from Python databases.
6. Stateful incident correlation.
7. Quick Test orchestration.
8. Continuous monitoring orchestration.
9. WPF dashboard and secondary pages.
10. CSV, graphs, and HTML reports.
11. Windows adapter, Wi-Fi, VPN, sleep/resume, and interface-event integration.
12. Installer, signing readiness, and release workflow.
13. Windows 10/11 end-to-end validation.
14. Prerelease installer publication.

## Branching And Release Rules

- All C# migration work happens on `csharp-rewrite`.
- Do not merge to `main` without explicit approval.
- Do not delete the Python implementation until explicitly authorized.
- Do not publish a stable C# release until Windows end-to-end testing, installer generation, report validation, and migration compatibility are complete.

## Current Milestone Scope

This first milestone establishes the solution layout, version metadata, WPF shell, corrected methodology documentation, independent schedule calculations, ICMP-only packet-loss calculation, ICMP jitter scoped by session/target/host/address family/probe stream, and SQLite schema bootstrap tests.
