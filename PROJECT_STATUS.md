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

## In Progress

- C# networking probe interfaces and cancellation-safe probe runners.
- Storage migrations beyond schema version 1.
- WPF MVVM wiring and live monitoring state.
- Windows build workflow validation.

## Implemented But Unverified

- WPF application shell compiles only after Windows workflow validation.
- SQLite bootstrap is unit tested on Linux but not yet tested under long Windows sessions.
- Schedule logic is unit tested but not yet integrated into a running monitor.

## Blocked

- Clean Windows installation, Quick Test, and monitoring verification require GitHub Actions or a Windows machine.
- Installer generation is not implemented in the C# branch yet.

## Not Started

- ICMP/TCP/DNS/HTTPS probe implementations.
- Built-in adaptive speed-test implementation.
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
- `dotnet test DQOPR.NetPulse.sln --configuration Release --no-build`: passed locally on Ubuntu, 12 tests.
- `dotnet format DQOPR.NetPulse.sln --verify-no-changes --verbosity minimal`: passed.
- `dotnet list DQOPR.NetPulse.sln package --vulnerable --include-transitive`: no vulnerable packages reported.
- `python3 -m ruff check .`: passed.
- `python3 -m mypy src/dqopr_netpulse`: passed.
- `python3 -m pytest -q`: passed, 24 tests with 2 skipped.
- GitHub Actions run `30141005831`: passed on `windows-latest`, built, tested, published WPF app artifact `dqopr-netpulse-csharp-win-x64`.
- Windows runtime verification: not yet completed.
- Installer verification: not yet started.
