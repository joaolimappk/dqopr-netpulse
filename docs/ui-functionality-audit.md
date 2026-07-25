# C# UI Functionality Audit

Branch: `csharp-rewrite`

Version audited: `0.3.0-alpha.4`

Primary source file: `src/DQOPR.NetPulse.App/MainWindow.xaml`

View model source: `src/DQOPR.NetPulse.App/ViewModels/DashboardViewModel.cs`

Command catalog source: `src/DQOPR.NetPulse.Core/UI/UiCommandCatalog.cs`

## Audit result

No visible tab is intentionally blank. No enabled menu item, dashboard action, report action, context action, or keyboard shortcut is intentionally a placeholder. Commands that depend on application state are disabled by their `CanExecute` state until a monitoring session or selected session exists.

The C# branch exposes a tray menu only when the user enables Minimize to tray in Settings and then minimizes the window.

## Main navigation tabs

| Label | Source file | Command or handler | Current behavior | Intended behavior | Status |
| --- | --- | --- | --- | --- | --- |
| Dashboard | `MainWindow.xaml` | `SelectedTabIndex = 0`, `ShowDashboardCommand` | Shows live monitoring state, timers, activity, interface/gateway, health cards, speed estimates, controls, and recent activity. | Primary monitoring dashboard. | Implemented |
| History | `MainWindow.xaml` | `SelectedTabIndex = 1`, `ShowHistoryCommand`, `RefreshCommand` | Shows SQLite sessions with metrics and session actions. | Browse and manage stored sessions. | Implemented |
| Session Details | `MainWindow.xaml` | `SelectedTabIndex = 2`, `OpenSessionCommand` | Shows selected session metadata, timeline tables, packet loss, speed results, events, markers, and simple charts. | Inspect stored evidence from a session. | Implemented |
| Reports | `MainWindow.xaml` | `SelectedTabIndex = 3`, `ShowReportsCommand` | Exports selected/current session as CSV, JSON, or standalone HTML and opens generated output. | Produce files for ISP evidence review. | Implemented |
| Settings | `MainWindow.xaml` | `SelectedTabIndex = 4`, `ShowSettingsCommand` | Edits and persists monitoring intervals, targets, timeout, database/export paths, and app behavior flags. | Configure monitoring and storage. | Implemented |
| Activity Log | `MainWindow.xaml` | `SelectedTabIndex = 5`, `ShowActivityLogCommand` | Shows timestamped application events with copy/save/clear actions. | Inspect current app activity and diagnostics. | Implemented |
| About | `MainWindow.xaml` | `SelectedTabIndex = 6`, `ShowAboutCommand` | Shows product, version, publisher, license, repository, runtime, data folder, and diagnostic-copy action. | Show app identity and support metadata. | Implemented |

## Top menus

| Menu | Label | Source file | Command or handler | Current behavior | Intended behavior | Status |
| --- | --- | --- | --- | --- | --- | --- |
| File | New monitoring session | `MainWindow.xaml` | `StartCommand` | Starts a monitoring session when idle. Disabled during active monitoring or quick test. | Start monitoring. | Implemented |
| File | Run Quick Test | `MainWindow.xaml` | `QuickTestCommand` | Runs a multi-probe quick diagnostic when idle. | Run one quick snapshot. | Implemented |
| File | Open session | `MainWindow.xaml` | `ShowHistoryCommand` | Navigates to History. | Let user select a stored session. | Implemented |
| File | Export current session | `MainWindow.xaml` | `ExportCurrentSessionCommand` | Exports selected or latest session when available. Disabled when no session exists. | Create CSV/JSON/HTML evidence. | Implemented |
| File | Settings | `MainWindow.xaml` | `ShowSettingsCommand` | Navigates to Settings. | Open settings page. | Implemented |
| File | Exit | `MainWindow.xaml` | `ExitCommand` | Closes the application. | Exit app. | Implemented |
| Monitor | Start | `MainWindow.xaml` | `StartCommand` | Starts monitoring when idle. | Start monitoring. | Implemented |
| Monitor | Pause | `MainWindow.xaml` | `PauseCommand` | Pauses an active session. Disabled unless monitoring. | Pause monitoring. | Implemented |
| Monitor | Resume | `MainWindow.xaml` | `ResumeCommand` | Resumes a paused session. Disabled unless paused. | Resume monitoring. | Implemented |
| Monitor | Stop | `MainWindow.xaml` | `StopCommand` | Stops an active or paused session; uses confirmation when enabled. | Stop monitoring. | Implemented |
| Monitor | Internet Feels Bad Now | `MainWindow.xaml` | `MarkerCommand` | Saves a manual marker for the latest session. Disabled until a session exists. | Mark a user-observed issue. | Implemented |
| View | Dashboard | `MainWindow.xaml` | `ShowDashboardCommand` | Navigates to Dashboard. | Show dashboard. | Implemented |
| View | History | `MainWindow.xaml` | `ShowHistoryCommand` | Navigates to History. | Show session history. | Implemented |
| View | Reports | `MainWindow.xaml` | `ShowReportsCommand` | Navigates to Reports. | Show reports/export page. | Implemented |
| View | Activity log | `MainWindow.xaml` | `ShowActivityLogCommand` | Navigates to Activity Log. | Show activity log. | Implemented |
| View | Refresh | `MainWindow.xaml` | `RefreshCommand` | Reloads sessions from SQLite. | Refresh stored evidence. | Implemented |
| Help | Documentation | `MainWindow.xaml` | `OpenDocumentationCommand` | Opens the GitHub repository page. | Open project docs. | Implemented |
| Help | Open data folder | `MainWindow.xaml` | `OpenDataFolderCommand` | Opens the application data folder. | Inspect local app data. | Implemented |
| Help | Open logs folder | `MainWindow.xaml` | `OpenLogsFolderCommand` | Opens the logs folder, creating it if needed. | Inspect app logs. | Implemented |
| Help | Report an issue | `MainWindow.xaml` | `ReportIssueCommand` | Opens the GitHub issue page. | Report bugs. | Implemented |
| Help | About | `MainWindow.xaml` | `ShowAboutCommand` | Navigates to About. | Show app metadata. | Implemented |

## Dashboard buttons

| Label | Source file | Command or handler | Current behavior | Intended behavior | Status |
| --- | --- | --- | --- | --- | --- |
| Run Quick Test | `MainWindow.xaml` | `QuickTestCommand` | Runs quick test and updates measurements, jitter, speed estimates, activity, history, and SQLite. | One-time quality snapshot. | Implemented |
| Start Monitoring | `MainWindow.xaml` | `StartCommand` | Starts continuous monitoring with persisted settings. | Begin evidence session. | Implemented |
| Pause | `MainWindow.xaml` | `PauseCommand` | Pauses monitoring and active timer. | Pause without counting idle time. | Implemented |
| Resume | `MainWindow.xaml` | `ResumeCommand` | Resumes paused monitoring. | Continue evidence session. | Implemented |
| Stop | `MainWindow.xaml` | `StopCommand` | Stops monitoring and persists session status. | End evidence session. | Implemented |
| Internet Feels Bad Now | `MainWindow.xaml` | `MarkerCommand` | Saves a manual marker for the latest session. | Record a user-observed symptom. | Implemented |

## History context menu

| Label | Source file | Command or handler | Current behavior | Intended behavior | Status |
| --- | --- | --- | --- | --- | --- |
| Open details | `MainWindow.xaml` | `OpenSessionCommand` | Loads selected session data and navigates to Session Details. | Inspect selected session. | Implemented |
| Export session | `MainWindow.xaml` | `ExportCurrentSessionCommand` | Exports selected session as CSV, JSON, and HTML. | Produce evidence files. | Implemented |
| Delete session | `MainWindow.xaml` | `DeleteSelectedSessionCommand` | Confirms deletion, then removes related SQLite rows. | Delete stored session data. | Implemented |

## Reports and export commands

| Label | Source file | Command or handler | Current behavior | Intended behavior | Status |
| --- | --- | --- | --- | --- | --- |
| Output location | `MainWindow.xaml` | `ExportDirectory` property | User can edit the output directory before exporting. | Choose output location. | Implemented |
| Open Folder | `MainWindow.xaml` | `OpenExportFolderCommand` | Opens/creates the output directory. | Open generated evidence location. | Implemented |
| Export CSV | `MainWindow.xaml` | `ExportCsvCommand` | Writes `netpulse-<session>.csv`. | Export tabular measurements and speed tests. | Implemented |
| Export JSON | `MainWindow.xaml` | `ExportJsonCommand` | Writes `netpulse-<session>.json`. | Export structured session data. | Implemented |
| Generate HTML Report | `MainWindow.xaml` | `GenerateHtmlReportCommand` | Writes `netpulse-<session>.html`. | Generate standalone evidence report. | Implemented |
| Open Generated File | `MainWindow.xaml` | `OpenLastExportCommand` | Opens the latest generated file. Disabled until a file exists. | Review export output. | Implemented |

## Settings controls

| Label | Source file | Command or handler | Current behavior | Intended behavior | Status |
| --- | --- | --- | --- | --- | --- |
| Monitoring duration | `MainWindow.xaml` | `DraftSettings.MonitoringDuration` | Edits persisted setting. | Configure session duration. | Implemented |
| Probe timeout | `MainWindow.xaml` | `DraftSettings.ProbeTimeout` | Edits persisted setting. | Configure probe timeout. | Implemented |
| ICMP interval | `MainWindow.xaml` | `DraftSettings.IcmpInterval` | Edits persisted setting. | Configure ICMP cadence. | Implemented |
| Speed-test interval | `MainWindow.xaml` | `DraftSettings.SpeedTestInterval` | Edits persisted setting. | Configure throughput estimate cadence. | Implemented |
| TCP interval | `MainWindow.xaml` | `DraftSettings.TcpInterval` | Edits persisted setting. | Configure TCP cadence. | Implemented |
| DNS interval | `MainWindow.xaml` | `DraftSettings.DnsInterval` | Edits persisted setting. | Configure DNS cadence. | Implemented |
| HTTPS interval | `MainWindow.xaml` | `DraftSettings.HttpsInterval` | Edits persisted setting. | Configure HTTPS cadence. | Implemented |
| DNS hostname | `MainWindow.xaml` | `DraftSettings.DnsHostname` | Edits persisted setting. | Configure DNS target. | Implemented |
| ICMP targets | `MainWindow.xaml` | `DraftSettings.IcmpTargets` | Edits semicolon-delimited targets. | Configure ICMP targets. | Implemented |
| TCP endpoints | `MainWindow.xaml` | `DraftSettings.TcpEndpoints` | Edits semicolon-delimited host:port targets. | Configure TCP targets. | Implemented |
| HTTPS endpoint | `MainWindow.xaml` | `DraftSettings.HttpsEndpoint` | Edits persisted endpoint. | Configure HTTPS target. | Implemented |
| Download test endpoint | `MainWindow.xaml` | `DraftSettings.DownloadEndpoint` | Edits persisted endpoint. | Configure download estimate endpoint. | Implemented |
| Upload test endpoint | `MainWindow.xaml` | `DraftSettings.UploadEndpoint` | Edits persisted endpoint. | Configure upload estimate endpoint. | Implemented |
| Database path | `MainWindow.xaml` | `DraftSettings.DatabasePath` | Edits persisted database path for future app starts. | Configure SQLite location. | Implemented |
| Export directory | `MainWindow.xaml` | `DraftSettings.ExportDirectory` | Edits persisted export directory. | Configure export location. | Implemented |
| Start minimized | `MainWindow.xaml` | `DraftSettings.StartMinimized` | Persists setting and minimizes the main window after startup. | Startup preference. | Implemented |
| Minimize to tray | `MainWindow.xaml` | `DraftSettings.MinimizeToTray` | Persists setting and hides the minimized window to a notification icon. | Tray preference. | Implemented |
| Confirm before stopping | `MainWindow.xaml` | `DraftSettings.ConfirmBeforeStopping` | Persists confirmation preference. | Prevent accidental stop. | Implemented |
| Save | `MainWindow.xaml` | `SaveSettingsCommand` | Validates and saves settings JSON. | Persist settings. | Implemented |
| Cancel | `MainWindow.xaml` | `CancelSettingsCommand` | Restores draft values from saved settings. | Discard edits. | Implemented |
| Restore Defaults | `MainWindow.xaml` | `RestoreDefaultsCommand` | Restores defaults in the draft settings. | Reset configuration. | Implemented |

## Activity log commands

| Label | Source file | Command or handler | Current behavior | Intended behavior | Status |
| --- | --- | --- | --- | --- | --- |
| Copy | `MainWindow.xaml` | `CopyAllActivityCommand` | Copies activity entries to clipboard. | Share diagnostics. | Implemented |
| Save Log | `MainWindow.xaml` | `SaveActivityLogCommand` | Writes a timestamped log file under the app logs folder. | Persist diagnostics. | Implemented |
| Clear | `MainWindow.xaml` | `ClearActivityCommand` | Clears current in-memory activity view. | Reset visible log. | Implemented |

## Keyboard shortcuts

| Shortcut | Source file | Command or handler | Current behavior | Intended behavior | Status |
| --- | --- | --- | --- | --- | --- |
| Ctrl+N | `MainWindow.xaml` | `StartCommand` | Starts monitoring when idle. | New monitoring session. | Implemented |
| Ctrl+Q | `MainWindow.xaml` | `QuickTestCommand` | Runs Quick Test when idle. | One-time diagnostic. | Implemented |
| Ctrl+R | `MainWindow.xaml` | `RefreshCommand` | Refreshes stored sessions. | Refresh visible data. | Implemented |
| Ctrl+E | `MainWindow.xaml` | `ExportCurrentSessionCommand` | Exports selected/latest session when available. | Export evidence. | Implemented |
| F1 | `MainWindow.xaml` | `OpenDocumentationCommand` | Opens repository documentation. | Help/documentation. | Implemented |

## Tray menu

| Label | Source file | Command or handler | Current behavior | Intended behavior | Status |
| --- | --- | --- | --- | --- | --- |
| Restore | `MainWindow.xaml.cs` | `RestoreFromTray` | Restores the hidden main window from the notification area. | Bring the app back from tray. | Implemented |
| Exit | `MainWindow.xaml.cs` | `Close` | Closes the application from the tray menu. | Exit app. | Implemented |

## Error handling

- View-model commands show user-safe error dialogs for monitoring and quick-test failures and record error events in the activity log.
- Application-level unhandled exceptions are appended to `netpulse-errors.log` under the local app logs folder and displayed in a safe message box when the main window is visible.
- Export and settings commands report success or validation errors through the status feedback area.
