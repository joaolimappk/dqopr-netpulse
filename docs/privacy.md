# Privacy Design

This document expands the user-facing policy in `PRIVACY.md`.

## Collection Minimization

Collect only data needed to diagnose internet quality:

- Measurements and timings.
- Configured diagnostic targets.
- Local network metadata that explains fault domains.
- User-entered incident marker notes.

Do not collect packet contents, browsing activity, personal files, Wi-Fi passwords, unrelated running processes, or authentication secrets.

## Local-First Operation

Monitoring data is stored locally. No telemetry, analytics, crash uploads, or automatic data submission should be enabled by default.

Network probes communicate with the user's configured targets. Optional speed testing may contact a third-party speed-test service and should disclose that to the user before it runs.

## Report Modes

Private report mode should be the default. It should mask or omit:

- Public IP address.
- Local IP address.
- Gateway address.
- Computer name.
- Username.
- Wi-Fi SSID.
- Full MAC addresses.

Technical report mode can include more detail when the user explicitly chooses it.

## Preview Before Sharing

The report UI should let users preview the generated HTML report and CSV export inventory before sharing data with an ISP or support provider.

## Retention

The default retention period is 180 days. Active sessions must not be silently deleted. Users should be able to archive or delete old sessions manually.

## Security Review Checklist

- No packet capture or deep packet inspection.
- No hidden network uploads.
- No secret material in reports.
- No private signing material in the repository.
- No instructions to bypass endpoint security.
- No unnecessary services, drivers, scheduled tasks, or startup entries.
