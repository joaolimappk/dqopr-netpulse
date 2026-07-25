using System.Windows;
using System.IO;
using DQOPR.NetPulse.App.ViewModels;
using DQOPR.NetPulse.Core.Monitoring;
using DQOPR.NetPulse.Core.Time;
using DQOPR.NetPulse.Networking.Probes;
using DQOPR.NetPulse.Platform.Windows.Network;
using DQOPR.NetPulse.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace DQOPR.NetPulse.App;

public partial class MainWindow : Window
{
    public MainWindow(string? databasePath = null)
    {
        InitializeComponent();
        ViewModel = CreateViewModel(databasePath ?? GetDefaultDatabasePath());
        DataContext = ViewModel;
        Loaded += async (_, _) => await ViewModel.InitializeAsync().ConfigureAwait(true);
    }

    public DashboardViewModel ViewModel { get; }

    private DashboardViewModel CreateViewModel(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
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
        return new DashboardViewModel(coordinator, quickTestRunner, store, clock, action => Dispatcher.InvokeAsync(action));
    }

    private static string GetDefaultDatabasePath()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DQOPR NetPulse");
        return Path.Combine(folder, "netpulse-csharp.sqlite3");
    }
}
