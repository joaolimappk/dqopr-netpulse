namespace DQOPR.NetPulse.Core.UI;

public sealed record UiCommandDefinition(
    string Area,
    string Label,
    string SourceFile,
    string CommandName,
    bool Visible,
    bool Enabled,
    bool Implemented,
    string Behavior,
    string? DisabledReason = null);

public static class UiCommandCatalog
{
    public static IReadOnlyList<UiCommandDefinition> VisibleCommands { get; } =
    [
        new("File", "New monitoring session", "MainWindow.xaml", "StartCommand", true, true, true, "Starts a monitoring session."),
        new("File", "Run Quick Test", "MainWindow.xaml", "QuickTestCommand", true, true, true, "Runs a diagnostic Quick Test."),
        new("File", "Open session", "MainWindow.xaml", "ShowHistoryCommand", true, true, true, "Navigates to History."),
        new("File", "Export current session", "MainWindow.xaml", "ExportCurrentSessionCommand", true, true, true, "Exports the selected/current session."),
        new("File", "Settings", "MainWindow.xaml", "ShowSettingsCommand", true, true, true, "Navigates to Settings."),
        new("File", "Exit", "MainWindow.xaml", "ExitCommand", true, true, true, "Closes the application."),
        new("View", "Dashboard", "MainWindow.xaml", "ShowDashboardCommand", true, true, true, "Navigates to Dashboard."),
        new("View", "History", "MainWindow.xaml", "ShowHistoryCommand", true, true, true, "Navigates to History."),
        new("View", "Reports", "MainWindow.xaml", "ShowReportsCommand", true, true, true, "Navigates to Reports."),
        new("View", "Activity log", "MainWindow.xaml", "ShowActivityLogCommand", true, true, true, "Navigates to Activity Log."),
        new("View", "Refresh", "MainWindow.xaml", "RefreshCommand", true, true, true, "Refreshes session data."),
        new("Help", "Documentation", "MainWindow.xaml", "OpenDocumentationCommand", true, true, true, "Opens project documentation."),
        new("Help", "Open data folder", "MainWindow.xaml", "OpenDataFolderCommand", true, true, true, "Opens the app data folder."),
        new("Help", "Open logs folder", "MainWindow.xaml", "OpenLogsFolderCommand", true, true, true, "Opens the logs folder."),
        new("Help", "Report an issue", "MainWindow.xaml", "ReportIssueCommand", true, true, true, "Opens the repository issue page."),
        new("Help", "About", "MainWindow.xaml", "ShowAboutCommand", true, true, true, "Shows About page."),
        new("Dashboard", "Internet Feels Bad Now", "MainWindow.xaml", "MarkerCommand", true, true, true, "Saves a manual issue marker."),
        new("History", "Delete selected session", "MainWindow.xaml", "DeleteSelectedSessionCommand", true, true, true, "Deletes the selected session after confirmation."),
        new("Reports", "CSV export", "MainWindow.xaml", "ExportCsvCommand", true, true, true, "Writes CSV export."),
        new("Reports", "JSON export", "MainWindow.xaml", "ExportJsonCommand", true, true, true, "Writes JSON export."),
        new("Reports", "HTML report", "MainWindow.xaml", "GenerateHtmlReportCommand", true, true, true, "Writes standalone HTML report."),
        new("Reports", "Open generated file", "MainWindow.xaml", "OpenLastExportCommand", true, true, true, "Opens the latest generated export when one exists."),
        new("Activity Log", "Copy", "MainWindow.xaml", "CopyAllActivityCommand", true, true, true, "Copies the activity log."),
        new("Activity Log", "Save Log", "MainWindow.xaml", "SaveActivityLogCommand", true, true, true, "Saves the activity log."),
        new("Activity Log", "Clear", "MainWindow.xaml", "ClearActivityCommand", true, true, true, "Clears the visible activity log."),
        new("About", "Copy Diagnostics", "MainWindow.xaml", "CopyDiagnosticsCommand", true, true, true, "Copies diagnostic metadata.")
    ];
}
