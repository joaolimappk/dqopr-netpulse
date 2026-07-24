# Security Policy

## Supported Versions

DQOPR NetPulse is in early alpha. Security reports are accepted for the current development branch and any published release artifacts.

## Reporting A Vulnerability

Please do not open a public issue for vulnerabilities. Email the maintainer address listed by the project owner, or use the repository security advisory workflow when it is enabled.

Include:

- Affected version or commit.
- Operating system and Python version.
- Steps to reproduce.
- Potential impact.
- Whether any private data or signing material may be involved.

## Project Security Commitments

DQOPR NetPulse must not:

- Disable or bypass Windows Defender, SmartScreen, antivirus tools, firewalls, or other security controls.
- Install drivers, services, startup entries, scheduled tasks, or shell extensions without a documented user-facing need.
- Collect packet contents, browser history, Wi-Fi passwords, personal files, authentication tokens, or unrelated process information.
- Commit private signing keys, certificates, passwords, or release credentials.

## Release Signing

Release signing should use a trusted Authenticode certificate and timestamping. Private keys must be stored in a secure signing service or protected secret store, never in the repository.

## Dependency Review

New dependencies should be reviewed for license compatibility, maintenance health, supply-chain risk, and whether they materially improve the project.
