# C# Rewrite Checklist

## Architecture

- [x] Create `csharp-rewrite` branch from current `main`.
- [x] Preserve Python alpha.
- [x] Add .NET solution.
- [x] Add Core, Diagnostics, Networking, Storage, Reporting, Platform.Windows, App projects.
- [x] Add initial tests.
- [ ] Add dependency/license inventory for C# packages.

## Correctness

- [x] Independent schedule calculator.
- [x] Unit test 10-minute session with speed test every 5 minutes.
- [x] Quick Test requires multiple probes.
- [x] Packet loss uses ICMP only.
- [x] DNS failures are separate from packet loss.
- [x] Jitter is per target and method.
- [ ] Rolling median, P95, and spike thresholds.
- [ ] Stateful incident lifecycle.
- [x] Pause time excluded from active duration.

## Implementation

- [x] Cancellation-safe monitor coordinator.
- [x] ICMP probes.
- [x] TCP probes.
- [x] DNS probes.
- [x] HTTPS probes.
- [x] Route snapshots.
- [x] Interface snapshots.
- [ ] Public-IP checks.
- [x] Built-in speed-test estimate.
- [x] SQLite repository layer.
- [x] Reports and CSV exports.
- [x] Basic graph rendering in WPF details and HTML reports.
- [x] WPF MVVM dashboard.
- [x] Settings and sessions pages.
- [x] Activity log and About pages.
- [x] Manual issue markers.
- [x] Minimize-to-tray Restore/Exit menu.
- [x] Expanded UI smoke screenshot capture.

## Windows

- [x] Windows build workflow passes.
- [x] Windows smoke workflow produces screenshots and SQLite/export evidence.
- [x] Workflow run `30142831951` verified artifact counts: 2 sessions, 38 measurements, 6 speed-test rows, 5 network-interface events, 0 incidents.
- [ ] Windows workflow passes for C# UI milestone `0.3.0-alpha.3`.
- [ ] WPF app launches on Windows 10.
- [ ] WPF app launches on Windows 11.
- [ ] Quick Test works on Windows.
- [ ] Continuous monitoring works on Windows.
- [ ] Installer builds.
- [ ] Installer tested on clean Windows.
- [ ] Installer attached to prerelease.
