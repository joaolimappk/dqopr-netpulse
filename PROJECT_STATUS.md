# DQOPR NetPulse Project Status

Branch: `csharp-rewrite`

Version: `0.3.0-alpha`

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

## In Progress

- Hardening production probe behavior across varied Windows networks.
- Storage migrations beyond schema version 1.
- WPF MVVM polish beyond the dashboard vertical slice.
- Windows runtime smoke validation.

## Implemented But Unverified

- WPF dashboard is wired to the real coordinator and compiles locally; GitHub Windows smoke validation is pending for this milestone.
- SQLite persistence stores/retrieves sessions, probe measurements, speed tests, and interface events in tests, but not yet tested under multi-day sessions.
- Schedule logic is integrated into a running monitor and unit tested with fakes.
- Built-in throughput estimate is implemented, but it is not an ISP-certified speed test.

## Blocked

- Clean Windows installation requires a later installer milestone.
- Installer generation is not implemented in the C# branch yet.

## Not Started

- Ookla CLI integration strategy.
- Stateful incident manager.
- CSV export.
- HTML reports.
- Graph generation.
- Python database migration compatibility.
- Windows installer and signing pipeline.
- GitHub prerelease for the C# branch.

## Latest Verification

- `dotnet build DQOPR.NetPulse.sln --configuration Release`: passed locally on Ubuntu, including the Windows-targeted WPF project.
- `dotnet test DQOPR.NetPulse.sln --configuration Release --no-build`: passed locally on Ubuntu, 17 tests.
- `dotnet format DQOPR.NetPulse.sln --verify-no-changes --verbosity minimal`: passed.
- `dotnet list DQOPR.NetPulse.sln package --vulnerable --include-transitive`: no vulnerable packages reported.
- `python3 -m ruff check .`: passed.
- `python3 -m mypy src/dqopr_netpulse`: passed.
- `python3 -m pytest -q`: passed, 24 tests with 2 skipped.
- GitHub Actions run `30141005831`: passed on `windows-latest`, built, tested, published WPF app artifact `dqopr-netpulse-csharp-win-x64`.
- Windows smoke validation for this vertical slice: pending next pushed workflow run.
- Real attended Windows desktop verification: not yet completed.
- Installer verification: not yet started.
