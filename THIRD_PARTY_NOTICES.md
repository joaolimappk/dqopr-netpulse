# Third-Party Notices

Copyright © 2026 DQOPR.

DQOPR NetPulse depends on third-party open-source software.

## Direct Runtime Dependencies

| Package | Purpose | License |
| --- | --- | --- |
| Python | Runtime | Python Software Foundation License |
| PySide6 | Windows graphical interface | LGPLv3/GPLv3/commercial licensing options; review distribution obligations before release |
| Matplotlib | Graph generation | Matplotlib license |

## Development Dependencies

| Package | Purpose | License |
| --- | --- | --- |
| coverage | Test coverage | Apache-2.0 |
| mypy | Static typing | MIT |
| pytest | Tests | MIT |
| ruff | Linting and formatting checks | MIT |

## Packaging Tools

| Tool | Purpose | License / Terms |
| --- | --- | --- |
| PyInstaller | Standalone Windows executable | GPLv2-or-later with bootloader exception |
| Inno Setup | Windows installer | Inno Setup license |
| GitHub Actions | CI and release automation | GitHub terms |

Before publishing a release, regenerate this file from the locked dependency set and include full notices required by all bundled packages.
