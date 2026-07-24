# Contributing To DQOPR NetPulse

Thank you for helping build DQOPR NetPulse. The project exists to give nontechnical users clear, conservative, privacy-respecting evidence about internet quality problems.

## Development Principles

- Reliability over cleverness.
- Conservative diagnosis over unsupported certainty.
- Standard-user operation over administrator requirements.
- Durable local data over in-memory-only monitoring.
- Privacy over unnecessary collection.
- Transparent packaging and signing over attempts to avoid security controls.

## Getting Started

```bash
python -m venv .venv
source .venv/bin/activate
python -m pip install --upgrade pip
python -m pip install -e ".[dev]"
python -m ruff check .
python -m mypy src/dqopr_netpulse
python -m pytest
```

Use Windows 10 or Windows 11 for Windows networking, PyInstaller, installer, and signing validation.

## Code Expectations

- Keep monitoring logic separate from the GUI.
- Keep network probes mockable.
- Use typed dataclasses or typed domain objects for persisted records.
- Add tests for incident logic, storage migrations, exports, and Windows platform behavior.
- Document thresholds and user-facing diagnostic language.
- Do not add telemetry, analytics, or automatic uploads by default.
- Do not add behavior that disables, bypasses, or interferes with security products.

## Pull Requests

Before opening a pull request:

- Run formatting, linting, type checks, and tests.
- Update docs when behavior, schema, packaging, or privacy posture changes.
- Include screenshots for GUI changes.
- Include sample anonymized report or CSV output when report formats change.
- Explain any new dependency and its license.

## Commit And Branch Guidance

Use focused branches and small pull requests when practical. Separate unrelated code, documentation, packaging, and dependency changes so reviewers can reason about risk.

## Reporting Vulnerabilities

Do not report suspected security vulnerabilities in public issues. Follow [SECURITY.md](SECURITY.md).
