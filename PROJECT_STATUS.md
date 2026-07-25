# DQOPR NetPulse Project Status

Branch: `csharp-rewrite`

Version: `0.3.0-alpha.4`

## Completed

- Created `csharp-rewrite` from current `main` and pushed upstream.
- Preserved the Python/PySide6 alpha implementation.
- Marked Python implementation as: **Legacy prototype — not recommended for production evidence collection**.
- Added .NET solution and project layout.
- Added initial WPF dashboard shell.
- Added .NET version metadata.
- Added independent schedule calculator and monitoring schedule contracts.
- Added Quick Test options requiring at least 20 probes.
- Added ICMP-only packet-loss calculation.
- Added alpha.4 ICMP jitter calculation scoped by session, protocol, target, host, address family, and probe stream.
- Added SQLite schema version 2 with methodology metadata, speed validity fields, diagnostics, and reference-result storage.
- Added Linux-compatible .NET tests for the initial core methodology.
- Added Windows GitHub Actions build/test/publish workflow for the C# branch.
- Added functional C# vertical slice for real ICMP/TCP/DNS/HTTPS probes, throughput estimates, monitoring coordinator, Quick Test runner, SQLite persistence, WPF live updates, session recovery marking, JSON export, and CI smoke evidence capture.
- Added implemented C# WPF pages for Dashboard, History, Session Details, Reports, Settings, Activity Log, and About.
- Added CSV export, standalone HTML report generation, persisted settings, manual markers, session deletion, UI command audit, and expanded smoke screenshot coverage.
- Added measurement-correctness audit, scoped dashboard latency/jitter cards, status-aware speed display, local-time UI/export fields, diagnostic bundle export, and manual external reference-result comparison.

## In Progress

- Hardening production probe behavior across varied Windows networks.
- Windows runner validation of the alpha.4 measurement-correctness milestone.

## Implemented But Unverified

- WPF dashboard, History, Session Details, Reports, Settings, Activity Log, and About are wired to the view model and passed GitHub Windows runner smoke validation.
- SQLite persistence stores/retrieves sessions, probe measurements, speed tests, and interface events in tests, but not yet tested under multi-day sessions.
- Schedule logic is integrated into a running monitor and unit tested with fakes.
- Built-in throughput estimate is implemented with alpha.4 validity metadata, but it is not an ISP-certified speed test.

## Blocked

- Clean Windows installation requires a later installer milestone.
- Installer generation is not implemented in the C# branch yet.

## Not Started

- Optional recognized speed-test engine integration strategy.
- Stateful incident manager.
- Python database migration compatibility.
- Windows installer and signing pipeline.
- GitHub prerelease for the C# branch.

## Latest Verification

- Current verified GitHub Actions run `30144179021`: passed on `windows-latest` for commit `74a6dcf48589c77ff5bbffe5be35900f10119fa8`, built, tested, published WPF app artifact `dqopr-netpulse-csharp-win-x64`, and uploaded smoke evidence artifact `dqopr-netpulse-csharp-smoke-evidence`.
- Current smoke evidence artifact `dqopr-netpulse-csharp-smoke-evidence`: verified locally after download; includes `active-monitoring.png`, `dashboard.png`, `quick-test-complete.png`, `history.png`, `session-details.png`, `reports.png`, `settings.png`, `activity-log.png`, `about.png`, `netpulse-smoke.sqlite3`, `measurements-export.json`, `smoke_metadata.json`, CSV export, JSON export, and HTML report.
- Current smoke SQLite evidence: 2 sessions, 37 measurements, 6 speed-test rows, 4 network-interface events, 0 manual markers, 0 incidents.
- Current local Linux-compatible validation: `python3 -m ruff check .` passed; `python3 -m mypy src/dqopr_netpulse` passed; `python3 -m pytest -q` passed, 24 tests with 2 skipped; `git diff --check` passed.
- Current local .NET validation: blocked on this Ubuntu machine because `dotnet` is not installed.
- Real attended Windows desktop alpha.4 comparison verification: not yet completed.
- Installer verification: not yet started.
