# DQOPR NetPulse

DQOPR NetPulse is an open-source Internet Quality Monitor and ISP Evidence Reporter for Windows users. Its goal is to collect local, durable, understandable evidence about intermittent internet problems such as latency spikes, jitter, packet loss, short outages, DNS failures, HTTPS failures, and speed degradation.

The project is currently in early alpha. On `main`, the Python/PySide6 implementation is the current published alpha. On the `csharp-rewrite` branch, that Python code is preserved as a legacy prototype while a native Windows C#/.NET implementation is developed beside it.

Python implementation status: **Legacy prototype — not recommended for production evidence collection**. It contains useful architecture, documentation, data models, tests, reporting ideas, and installer scaffolding, but the C# rewrite is intended to correct scheduling and statistical-methodology limitations before NetPulse is treated as production-grade evidence software.

The repository contains a working command-line monitoring engine, typed data models, configuration defaults, SQLite storage layer, conservative incident classifier, CSV/ZIP exports, graph generation, self-contained HTML report generation, a PySide6 GUI dashboard connected to the monitoring engine, tests, documentation, and Windows release scaffolding.

## What NetPulse Does

- Monitors local gateway health and multiple independent public internet targets.
- Runs a one-time Quick Test snapshot with latency, packet-loss, DNS, TCP, HTTPS, and optional speed-test evidence.
- Records measurements incrementally to SQLite so long-running tests are not kept only in memory.
- Uses conservative incident classifications and confidence labels.
- Generates CSV exports, graph images, ZIP export bundles, and self-contained HTML ISP reports.
- Prepares for standard-user Windows operation, PyInstaller packaging, Inno Setup installers, checksums, and Authenticode signing.

## What NetPulse Does Not Prove

NetPulse cannot definitively prove every ISP fault. Network evidence is probabilistic: a stable local gateway combined with simultaneous failures across multiple independent public targets can support a probable ISP or upstream issue, but it is not a legal or technical guarantee. Single-target failures, ICMP-only failures, VPN interference, Wi-Fi instability, device sleep, and remote-service outages must be interpreted cautiously.

NetPulse must never disable, bypass, suppress, or interfere with Windows Defender, SmartScreen, antivirus tools, firewalls, or other security controls.

## Planned User Workflow

1. Enter contracted download and upload speeds, or choose "I don't know".
2. Choose a test duration such as 10 minutes, 1 hour, 6 hours, 24 hours, continuous monitoring, a custom duration, or a custom cycle count.
3. Accept safe default probe intervals or adjust advanced settings.
4. Run a Quick Test for a fast snapshot, or start monitoring and leave the application running.
5. Press "Internet feels bad now" during noticeable problems.
6. Generate an ISP-oriented HTML report, graphs, and CSV exports.

## Methodology Summary

NetPulse is designed to compare several signals:

- Local gateway latency and packet loss.
- Multiple independent external target checks.
- DNS resolution timing and failures.
- TCP and HTTPS connectivity checks.
- Optional speed test results at conservative intervals.
- Interface changes, Wi-Fi signal, VPN detection, and manual user markers.

The diagnostic engine should prefer cautious language such as "suggests", "probable", and "inconclusive" unless the evidence is strong. See [docs/methodology.md](docs/methodology.md).

## Privacy Summary

NetPulse is local-first. It should collect only information needed for connection diagnosis, never inspect packet contents, and never collect Wi-Fi passwords, browser history, personal files, unrelated process lists, authentication tokens, or full MAC addresses in exported reports. Reports should default to private mode with masked identifiers. See [PRIVACY.md](PRIVACY.md) and [docs/privacy.md](docs/privacy.md).

## Development Setup

### Python Alpha

Requirements:

- Python 3.12
- A virtual environment
- Windows for reliable executable and installer builds

```bash
python -m venv .venv
source .venv/bin/activate
python -m pip install --upgrade pip
python -m pip install -e ".[dev]"
```

On Windows PowerShell:

```powershell
py -3.12 -m venv .venv
.\.venv\Scripts\Activate.ps1
python -m pip install --upgrade pip
python -m pip install -e ".[dev]"
```

### C# Rewrite Branch

Requirements:

- .NET 10 SDK
- Windows 10 or Windows 11 for WPF runtime verification

```bash
dotnet restore DQOPR.NetPulse.sln
dotnet test DQOPR.NetPulse.sln --configuration Release
```

The C# rewrite uses WPF and targets Windows for the desktop application. Core, diagnostics, storage, and non-UI tests are expected to run on Linux and Windows.

## Running Checks

```bash
python -m ruff check .
python -m mypy src/dqopr_netpulse
python -m pytest
python scripts/release_validate.py --skip-builds
```

Release candidates should use strict validation once Windows build tools and release artifacts are available:

```bash
python scripts/release_validate.py --strict
```

## Running The CLI

Run a short cycle-based monitor from a source checkout:

```bash
PYTHONPATH=src python -m dqopr_netpulse monitor --cycles 3 --latency-interval 2
```

Export a session:

```bash
PYTHONPATH=src python -m dqopr_netpulse export-csv SESSION_ID exports/session-csv
PYTHONPATH=src python -m dqopr_netpulse export-zip SESSION_ID exports/session.zip
PYTHONPATH=src python -m dqopr_netpulse report SESSION_ID reports/isp-report.html
```

The GUI entry point is available as `dqopr_netpulse.gui.app:main`. Run Quick Test performs one complete diagnostic snapshot and then stops automatically. Start Monitoring launches the monitoring engine in a background Qt thread, streams live measurements into the dashboard, and persists them to the local SQLite database. PySide6 is declared as a runtime dependency and should be validated on Windows before release packaging.

## Examples

Generated reports, ZIP bundles, SQLite databases, and graph images are ignored by default so personal network evidence is not accidentally published. See [examples](examples) for notes on creating anonymized local samples.

## Building A Windows Executable

Windows executable builds should run on Windows, not through unsupported Linux cross-compilation. The release validator can invoke PyInstaller when it is installed and an application entry point exists:

```powershell
python -m pip install pyinstaller
python scripts\release_validate.py
```

See [docs/packaging.md](docs/packaging.md).

## Creating A Windows Installer

The installer scaffolding uses Inno Setup:

```powershell
iscc packaging\windows\netpulse.iss
```

The script expects a PyInstaller output directory under `dist\DQOPR-NetPulse`. See [packaging/windows/README.md](packaging/windows/README.md).

## Checksums And Signatures

Release artifacts should be accompanied by SHA-256 checksums. Signed releases should use Authenticode signatures with trusted timestamping. The release validator writes `release_artifacts/SHA256SUMS.txt` and verifies signatures when signed artifacts are present or signing is expected.

See [docs/signing.md](docs/signing.md) and [docs/release.md](docs/release.md).

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md), [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md), and [SECURITY.md](SECURITY.md) before opening issues or pull requests.

## License

DQOPR NetPulse is licensed under the Apache License 2.0. See [LICENSE](LICENSE).

Copyright © 2026 DQOPR.
