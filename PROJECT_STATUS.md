# DQOPR NetPulse Project Status

Branch: `csharp-rewrite`

Version: `0.3.0-alpha.3`

## Completed

- Created `csharp-rewrite` from current `main` and pushed upstream.
- Preserved the Python/PySide6 alpha implementation.
- Marked Python implementation as: **Legacy prototype — not recommended for production evidence collection**.
- Added .NET solution and project layout.
- Added initial WPF dashboard shell.
- Added .NET version metadata.
- Added independent schedule calculator and monitoring schedule contracts.
- Added Quick Test options requiring 10 to 20 probes.
- Added ICMP-only packet-loss calculation.
- Added per-target/per-method jitter calculation.
- Added initial SQLite schema bootstrap with WAL and foreign-key setup.
- Added Linux-compatible .NET tests for the initial core methodology.
- Added Windows GitHub Actions build/test/publish workflow for the C# branch.
- Added functional C# vertical slice for real ICMP/TCP/DNS/HTTPS probes, throughput estimates, monitoring coordinator, Quick Test runner, SQLite persistence, WPF live updates, session recovery marking, JSON export, and CI smoke evidence capture.
- Added implemented C# WPF pages for Dashboard, History, Session Details, Reports, Settings, Activity Log, and About.
- Added CSV export, standalone HTML report generation, persisted settings, manual markers, session deletion, UI command audit, and expanded smoke screenshot coverage.

## In Progress

- Hardening production probe behavior across varied Windows networks.
- Storage migrations beyond schema version 1.
- Windows runner validation of the full UI milestone.

## Implemented But Unverified

- WPF dashboard, History, Session Details, Reports, Settings, Activity Log, and About are wired to the view model and ready for GitHub Windows runner smoke validation.
- SQLite persistence stores/retrieves sessions, probe measurements, speed tests, and interface events in tests, but not yet tested under multi-day sessions.
- Schedule logic is integrated into a running monitor and unit tested with fakes.
- Built-in throughput estimate is implemented, but it is not an ISP-certified speed test.

## Blocked

- Clean Windows installation requires a later installer milestone.
- Installer generation is not implemented in the C# branch yet.

## Not Started

- Ookla CLI integration strategy.
- Stateful incident manager.
- Python database migration compatibility.
- Windows installer and signing pipeline.
- GitHub prerelease for the C# branch.

## Latest Verification

- Previous verified GitHub Actions run `30142831951`: passed on `windows-latest` for commit `7a7b1a9b087a17bcfe1bddea77895ef3c4bbc11f`, built, tested, published WPF app artifact `dqopr-netpulse-csharp-win-x64`, and uploaded smoke evidence artifact `dqopr-netpulse-csharp-smoke-evidence`.
- Previous smoke evidence artifact `dqopr-netpulse-csharp-smoke-evidence`: verified locally after download; includes `active-monitoring.png`, `quick-test-complete.png`, `netpulse-smoke.sqlite3`, `measurements-export.json`, and `smoke_metadata.json`.
- Previous smoke SQLite evidence: 2 sessions, 38 measurements, 6 speed-test rows, 5 network-interface events, 0 incidents.
- Current milestone local .NET validation: blocked on this Ubuntu machine because `dotnet` is not installed.
- Current milestone GitHub Windows runner validation: pending after push.
- Real attended Windows desktop verification: not yet completed.
- Installer verification: not yet started.
