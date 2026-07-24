# Windows Packaging Assets

This directory contains Inno Setup scaffolding for the DQOPR NetPulse Windows installer.

## Build Prerequisites

- Windows 10 or Windows 11.
- Python 3.12.
- PyInstaller.
- Inno Setup 6.
- Optional Authenticode signing tools.

## Expected Inputs

The installer script expects a PyInstaller one-folder build at:

```text
dist\DQOPR-NetPulse
```

The main executable is expected at:

```text
dist\DQOPR-NetPulse\DQOPR-NetPulse.exe
```

## Build

```powershell
iscc packaging\windows\netpulse.iss
```

Or use the release validator:

```powershell
python scripts\release_validate.py --strict
```

## Signing

Sign the application executable before building the installer when possible, then sign the installer. See `docs/signing.md`.
