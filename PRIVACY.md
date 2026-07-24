# Privacy

DQOPR NetPulse is designed as a local-first diagnostic tool.

## Data Collected For Diagnosis

The application may record:

- Test timestamps.
- Target names and addresses.
- Probe success or failure.
- Latency, jitter, packet loss, DNS timing, TCP timing, HTTPS timing, and speed-test results.
- Active network-interface name and type.
- Local gateway address.
- Configured DNS servers.
- Wi-Fi signal percentage when Windows exposes it.
- VPN detection status.
- User-created manual incident markers and notes.

## Data Not Collected

NetPulse must not collect:

- Wi-Fi passwords.
- Browser history.
- Personal files.
- Unrelated running-process details.
- Authentication tokens.
- Packet contents.
- Full MAC addresses in exported reports.

NetPulse does not perform packet interception or deep packet inspection.

## Local Storage

Monitoring data is stored locally in SQLite under the per-user application-data directory. On Windows this is expected to be under `%LOCALAPPDATA%\DQOPR NetPulse`.

## Reports And Exports

Private report mode should be enabled by default. Private mode masks or omits identifiers such as public IP address, local IP address, gateway address, computer name, username, and Wi-Fi SSID where practical.

Technical report mode may include more diagnostic detail when the user explicitly chooses it. Users should preview generated reports before sharing them with an ISP or support provider.

## Network Communication

Monitoring requires network probes to configured targets. Optional speed testing may contact a speed-test provider and can consume bandwidth. No telemetry, analytics, crash uploads, or automatic data submission should be enabled by default.
