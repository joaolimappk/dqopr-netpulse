# Code Signing

DQOPR NetPulse releases should be prepared for legitimate Authenticode signing. Signing reduces tamper risk and helps users identify the publisher, but it cannot guarantee that Windows Defender or SmartScreen will never show a warning.

## Recommended Approach

- Use a trusted code-signing certificate or Microsoft Trusted Signing when available.
- Timestamp signatures with a trusted timestamp authority.
- Sign both the PyInstaller executable files and the final installer.
- Verify signatures after signing.
- Keep unsigned development builds clearly labeled.

## Private Key Handling

Never commit private signing keys, certificate files, passwords, PINs, or signing-service credentials. Use GitHub Actions secrets, OpenID Connect to a signing service, a hardware token, or another secure signing workflow.

## Example Local Signing Command

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a "dist\DQOPR-NetPulse\DQOPR-NetPulse.exe"
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a "release_artifacts\DQOPR-NetPulse-Setup-0.2.1.exe"
```

Adjust the timestamp authority and certificate selection to match the maintainer's signing provider.

## Verification

```powershell
signtool verify /pa /all "dist\DQOPR-NetPulse\DQOPR-NetPulse.exe"
signtool verify /pa /all "release_artifacts\DQOPR-NetPulse-Setup-0.2.1.exe"
```

The release validator also attempts PowerShell Authenticode verification on Windows:

```powershell
python scripts\release_validate.py --signing-expected
```

## SmartScreen Notes

SmartScreen reputation is influenced by certificate trust, publisher reputation, download reputation, prevalence, and application behavior. Do not document bypass steps for users. Instead, keep builds transparent, signed, timestamped, versioned, and distributed from trusted project locations.
