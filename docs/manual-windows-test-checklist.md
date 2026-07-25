# Manual Windows Test Checklist

Branch: `csharp-rewrite`

Version: `0.3.0-alpha.3`

Use this checklist on a real Windows 10 or Windows 11 desktop before proposing a merge to `main`.

## Install/run

- [ ] Download the latest `dqopr-netpulse-csharp-win-x64` artifact from the `csharp-build-test` workflow on `csharp-rewrite`.
- [ ] Extract the artifact to a clean folder outside the repository.
- [ ] Start `DQOPR.NetPulse.exe`.
- [ ] Confirm the app opens without an unhandled exception dialog.
- [ ] Confirm no installer is required for this portable build.

## Dashboard

- [ ] Confirm the Dashboard tab is populated, not blank.
- [ ] Run Quick Test.
- [ ] Confirm latency, jitter, packet loss, DNS, TCP, HTTPS, interface, gateway, download, and upload fields update or show an explicit unavailable/failure state.
- [ ] Confirm Recent activity records probe and speed-test activity.
- [ ] Click Internet Feels Bad Now after a session exists and confirm a marker is saved.

## Continuous monitoring

- [ ] Set Monitoring duration to `00:10:00`.
- [ ] Set Speed-test interval to `00:05:00`.
- [ ] Start monitoring.
- [ ] Confirm a download/upload throughput estimate runs near session start.
- [ ] Confirm another throughput estimate runs around the 5-minute mark.
- [ ] Pause and resume monitoring.
- [ ] Stop monitoring and confirm the stop confirmation behavior matches Settings.

## History and details

- [ ] Open History and confirm stored sessions appear with start/end/duration/status/interface/gateway/measurement/loss/latency/speed columns.
- [ ] Select a session and click Open Details.
- [ ] Confirm Session Details shows metadata, measurements, ICMP packet loss, DNS/TCP/HTTPS rows, speed-test rows, interface events, markers, and nonblank charts.
- [ ] Use the History row context menu for Open details and Export session.
- [ ] Delete a disposable test session and confirm the confirmation prompt appears.

## Reports/export

- [ ] In Reports, set the output directory to a writable folder.
- [ ] Export CSV and confirm a nonempty `netpulse-<session>.csv` file exists.
- [ ] Export JSON and confirm a nonempty `netpulse-<session>.json` file exists.
- [ ] Generate HTML Report and confirm a nonempty `netpulse-<session>.html` file exists.
- [ ] Open Generated File and confirm Windows opens the latest generated report/export.
- [ ] Confirm the HTML report includes the throughput estimate disclaimer.

## Settings

- [ ] Change intervals, targets, database path, export directory, and behavior flags.
- [ ] Save settings and restart the app.
- [ ] Confirm settings persisted.
- [ ] Enable Start minimized, restart, and confirm the main window starts minimized.
- [ ] Enable Minimize to tray, minimize the app, and confirm Restore and Exit work from the tray icon.
- [ ] Enter an invalid value and confirm validation prevents saving with a clear message.
- [ ] Restore defaults and save.

## Menus and shortcuts

- [ ] Exercise every File menu command.
- [ ] Exercise every Monitor menu command.
- [ ] Exercise every View menu command.
- [ ] Exercise every Help menu command.
- [ ] Confirm Ctrl+N, Ctrl+Q, Ctrl+R, Ctrl+E, and F1 execute or disable consistently with app state.
- [ ] Confirm no enabled menu item or button silently does nothing.

## Activity log and diagnostics

- [ ] Confirm Activity Log is populated.
- [ ] Copy the activity log.
- [ ] Save the activity log and confirm the file exists under the logs folder.
- [ ] Open data folder and logs folder from Help.
- [ ] Open About and copy diagnostic information.

## Known limitations for this milestone

- [ ] Portable artifact is unsigned.
- [ ] Installer generation is intentionally out of scope for this branch milestone.
- [ ] Built-in throughput is an estimate, not an ISP-certified speed test.
- [ ] Tray behavior is not exposed as a live tray menu in this milestone.
