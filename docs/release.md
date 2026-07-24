# Release Process

This process is intended for maintainers preparing public DQOPR NetPulse releases.

## Before Release

- Confirm product scope and user-facing claims match the implementation.
- Review privacy behavior and report redaction.
- Review dependency licenses and update `THIRD_PARTY_NOTICES.md`.
- Run unit, integration, and Windows-specific tests.
- Build on a clean Windows runner.
- Confirm the application runs as a standard user.
- Confirm installer and uninstaller behavior.

## Validation

Run:

```powershell
python scripts\release_validate.py --strict
```

The validator checks required files, runs Python quality gates when tools are installed, scans text files for common accidental secret patterns, builds the executable and installer when supported tools and entry points exist, creates SHA-256 checksums, verifies signatures when possible, and writes build metadata.

## Artifact Checklist

- Source archive.
- Windows executable directory or archive.
- Inno Setup installer.
- `SHA256SUMS.txt`.
- Build metadata JSON.
- Third-party notices.
- Changelog entry.
- Signature verification evidence for signed builds.

## Versioning

The version in `pyproject.toml`, `src/dqopr_netpulse/__init__.py`, installer definitions, changelog, and release tag should match.

## Release Notes

Release notes should state:

- What changed.
- Known limitations.
- Whether the release is signed.
- SHA-256 checksum location.
- Privacy-relevant changes.
- Any changes to incident methodology or thresholds.

## Rollback

If a release artifact is found to be unsafe or materially broken, remove the artifact from public distribution, publish an advisory, and issue a corrected release with a new version.
