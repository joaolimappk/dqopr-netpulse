# Windows Packaging

DQOPR NetPulse should be packaged on Windows using PyInstaller for the application executable and Inno Setup for the installer.

## Requirements

- Windows 10 or Windows 11, 64-bit.
- Python 3.12.
- PyInstaller.
- Inno Setup 6.
- Optional Authenticode signing toolchain.

Linux is suitable for development and many tests, but reliable Windows executable and installer builds should run on Windows or a Windows CI runner.

## Expected Build Flow

1. Create a clean virtual environment.
2. Install the project and development tools.
3. Run tests, linting, and type checks.
4. Build the PyInstaller executable into `dist/DQOPR-NetPulse`.
5. Build the Inno Setup installer from `packaging/windows/netpulse.iss`.
6. Sign the executable and installer when signing credentials are available.
7. Verify signatures.
8. Generate SHA-256 checksums.
9. Publish artifacts and validation metadata.

The release validator automates as much of this as the local machine supports:

```powershell
python scripts\release_validate.py --strict
```

## Installer Behavior

The installer should:

- Install under the normal Windows application directory.
- Create Start Menu shortcuts.
- Offer an optional desktop shortcut.
- Include an uninstaller.
- Register the application in Windows Installed Apps.
- Display the Apache-2.0 license.
- Avoid writing normal runtime data to protected directories.
- Avoid services, drivers, shell extensions, scheduled tasks, startup entries, ads, bundled unrelated software, browser modifications, or downloaded executable components.

## User Data Location

Normal application data should be stored under the per-user application-data directory, expected as `%LOCALAPPDATA%\DQOPR NetPulse` on Windows.

## Development Builds

Unsigned development builds should be labeled clearly and should not claim that Windows Defender or SmartScreen warnings can be avoided. Reputation warnings are possible for new or unsigned applications.
