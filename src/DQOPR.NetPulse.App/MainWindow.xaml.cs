using System.Windows;
using System.IO;
using DQOPR.NetPulse.App.ViewModels;
using DQOPR.NetPulse.Core.Configuration;
using DQOPR.NetPulse.Core.Monitoring;
using DQOPR.NetPulse.Core.Time;
using DQOPR.NetPulse.Networking.Probes;
using DQOPR.NetPulse.Platform.Windows.Network;
using DQOPR.NetPulse.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DQOPR.NetPulse.App;

public partial class MainWindow : Window
{
    private readonly bool interactivePrompts;
    private Forms.NotifyIcon? notifyIcon;

    public MainWindow(string? databasePath = null, string? exportDirectory = null, bool interactivePrompts = true)
    {
        this.interactivePrompts = interactivePrompts;
        InitializeComponent();
        ViewModel = CreateViewModel(databasePath ?? GetDefaultDatabasePath(), exportDirectory);
        DataContext = ViewModel;
        Loaded += async (_, _) =>
        {
            await ViewModel.InitializeAsync().ConfigureAwait(true);
            if (ViewModel.StartMinimizedEnabled)
            {
                WindowState = WindowState.Minimized;
            }
        };
        StateChanged += (_, _) => HideToTrayIfNeeded();
    }

    public DashboardViewModel ViewModel { get; }

    private DashboardViewModel CreateViewModel(string databasePath, string? exportDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        var dataFolder = Path.GetDirectoryName(databasePath) ?? Environment.CurrentDirectory;
        exportDirectory ??= Path.Combine(dataFolder, "exports");
        var settingsPath = Path.Combine(dataFolder, "settings.json");
        var defaultSettings = ApplicationSettings.Defaults(databasePath, exportDirectory);
        var settingsStore = new ApplicationSettingsStore(settingsPath, defaultSettings);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();
        var store = new SqliteNetPulseStore(connectionString);
        var clock = new SystemMonitoringClock();
        var probes = new NetworkProbeService();
        var environment = new WindowsNetworkEnvironmentService();
        var coordinator = new MonitoringCoordinator(probes, environment, store, clock);
        var quickTestRunner = new QuickTestRunner(probes, environment, store, clock);
        return new DashboardViewModel(
            coordinator,
            quickTestRunner,
            store,
            clock,
            settingsStore,
            defaultSettings,
            action => Dispatcher.InvokeAsync(action),
            (title, message) => System.Windows.MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning),
            Confirm,
            Close);
    }

    private bool Confirm(string title, string message)
    {
        if (!interactivePrompts)
        {
            return true;
        }

        return System.Windows.MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    protected override void OnClosed(EventArgs e)
    {
        notifyIcon?.Dispose();
        base.OnClosed(e);
    }

    private void HideToTrayIfNeeded()
    {
        if (WindowState != WindowState.Minimized || !ViewModel.MinimizeToTrayEnabled)
        {
            return;
        }

        EnsureNotifyIcon();
        Hide();
    }

    private void EnsureNotifyIcon()
    {
        if (notifyIcon is not null)
        {
            notifyIcon.Visible = true;
            return;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Restore", null, (_, _) => Dispatcher.Invoke(RestoreFromTray));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));

        notifyIcon = new Forms.NotifyIcon
        {
            Text = "DQOPR NetPulse",
            Icon = Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreFromTray);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        if (notifyIcon is not null)
        {
            notifyIcon.Visible = false;
        }
    }

    private static string GetDefaultDatabasePath()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DQOPR NetPulse");
        return Path.Combine(folder, "netpulse-csharp.sqlite3");
    }
}
