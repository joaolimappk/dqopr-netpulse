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
- [ ] Pause time excluded from active duration.

## Implementation

- [ ] Cancellation-safe monitor coordinator.
- [ ] ICMP probes.
- [ ] TCP probes.
- [ ] DNS probes.
- [ ] HTTPS probes.
- [ ] Route snapshots.
- [ ] Interface snapshots.
- [ ] Public-IP checks.
- [ ] Built-in speed-test estimate.
- [ ] SQLite repository layer.
- [ ] Reports and CSV exports.
- [ ] Graphs.
- [ ] WPF MVVM dashboard.
- [ ] Settings and sessions pages.

## Windows

- [ ] Windows build workflow passes.
- [ ] WPF app launches on Windows 10.
- [ ] WPF app launches on Windows 11.
- [ ] Quick Test works on Windows.
- [ ] Continuous monitoring works on Windows.
- [ ] Installer builds.
- [ ] Installer tested on clean Windows.
- [ ] Installer attached to prerelease.
