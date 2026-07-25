using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DQOPR.NetPulse.Core.Configuration;
using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Core.Monitoring;
using DQOPR.NetPulse.Core.QuickTest;
using DQOPR.NetPulse.Core.Storage;
using DQOPR.NetPulse.Core.Time;
using DQOPR.NetPulse.Diagnostics.Statistics;
using DQOPR.NetPulse.Reporting;

namespace DQOPR.NetPulse.App.ViewModels;

public sealed class DashboardViewModel : INotifyPropertyChanged
{
    private readonly MonitoringCoordinator coordinator;
    private readonly QuickTestRunner quickTestRunner;
    private readonly INetPulseStore store;
    private readonly Action<Action> dispatch;
    private readonly List<ProbeMeasurement> measurements = [];
    private readonly List<SpeedTestMeasurement> speedTests = [];
    private readonly IMonitoringClock clock;
    private CancellationTokenSource? monitoringCancellation;
    private CancellationTokenSource? quickTestCancellation;
    private string status = "Ready";
    private string elapsedActiveTime = "00:00:00";
    private string remainingTime = "Not started";
    private string lastMeasurementAge = "none";
    private string latency = "-- ms";
    private string packetLoss = "-- %";
    private string jitter = "Waiting for samples";
    private string routerHealth = "Unknown";
    private string internetHealth = "Unknown";
    private string dnsHealth = "Unknown";
    private string httpsHealth = "Unknown";
    private string currentOperation = "Waiting to start.";
    private Guid? latestSessionId;

    public DashboardViewModel(
        MonitoringCoordinator coordinator,
        QuickTestRunner quickTestRunner,
        INetPulseStore store,
        IMonitoringClock clock,
        Action<Action> dispatch)
    {
        this.coordinator = coordinator;
        this.quickTestRunner = quickTestRunner;
        this.store = store;
        this.clock = clock;
        this.dispatch = dispatch;

        StartCommand = new RelayCommand(() => StartMonitoringAsync(), () => !IsMonitoring);
        PauseCommand = new RelayCommand(PauseAsync, () => IsMonitoring);
        ResumeCommand = new RelayCommand(ResumeAsync, () => IsPaused);
        StopCommand = new RelayCommand(StopMonitoringAsync, () => IsMonitoring || IsPaused);
        QuickTestCommand = new RelayCommand(() => RunQuickTestAsync(), () => !IsMonitoring && !IsQuickTestRunning);

        coordinator.Activity += (_, args) => Dispatch(() => AddActivity(args.Message));
        coordinator.MeasurementRecorded += (_, args) => Dispatch(() => AddMeasurement(args.Measurement));
        coordinator.SpeedTestRecorded += (_, args) => Dispatch(() => AddSpeedTest(args.SpeedTest));
        coordinator.SessionChanged += (_, args) => Dispatch(() => ApplySession(args.Session));

        quickTestRunner.Activity += (_, args) => Dispatch(() => AddActivity(args.Message));
        quickTestRunner.MeasurementRecorded += (_, args) => Dispatch(() => AddMeasurement(args.Measurement));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> RecentActivity { get; } = [];

    public ICommand StartCommand { get; }

    public ICommand PauseCommand { get; }

    public ICommand ResumeCommand { get; }

    public ICommand StopCommand { get; }

    public ICommand QuickTestCommand { get; }

    public bool IsMonitoring { get; private set; }

    public bool IsPaused { get; private set; }

    public bool IsQuickTestRunning { get; private set; }

    public string Status
    {
        get => status;
        private set => SetField(ref status, value);
    }

    public string ElapsedActiveTime
    {
        get => elapsedActiveTime;
        private set => SetField(ref elapsedActiveTime, value);
    }

    public string RemainingTime
    {
        get => remainingTime;
        private set => SetField(ref remainingTime, value);
    }

    public string LastMeasurementAge
    {
        get => lastMeasurementAge;
        private set => SetField(ref lastMeasurementAge, value);
    }

    public string Latency
    {
        get => latency;
        private set => SetField(ref latency, value);
    }

    public string PacketLoss
    {
        get => packetLoss;
        private set => SetField(ref packetLoss, value);
    }

    public string Jitter
    {
        get => jitter;
        private set => SetField(ref jitter, value);
    }

    public string RouterHealth
    {
        get => routerHealth;
        private set => SetField(ref routerHealth, value);
    }

    public string InternetHealth
    {
        get => internetHealth;
        private set => SetField(ref internetHealth, value);
    }

    public string DnsHealth
    {
        get => dnsHealth;
        private set => SetField(ref dnsHealth, value);
    }

    public string HttpsHealth
    {
        get => httpsHealth;
        private set => SetField(ref httpsHealth, value);
    }

    public string CurrentOperation
    {
        get => currentOperation;
        private set => SetField(ref currentOperation, value);
    }

    public async Task InitializeAsync()
    {
        await store.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        await store.MarkRunningSessionsInterruptedAsync(clock.UtcNow, CancellationToken.None).ConfigureAwait(false);
        var sessions = await store.GetSessionsAsync(CancellationToken.None).ConfigureAwait(false);
        Dispatch(() =>
        {
            AddActivity(sessions.Count == 0 ? "No previous sessions found." : $"Previous sessions available: {sessions.Count}.");
            Status = "Ready";
        });
    }

    public Task StartMonitoringAsync()
        => StartMonitoringAsync(new MonitoringOptions
        {
            ProfileName = "Manual Monitoring",
            ActiveDuration = TimeSpan.FromMinutes(10),
            Intervals = MonitoringIntervals.EvidenceDefaults with { SpeedTest = TimeSpan.FromMinutes(5) }
        });

    public async Task StartMonitoringAsync(MonitoringOptions options)
    {
        monitoringCancellation = new CancellationTokenSource();
        measurements.Clear();
        speedTests.Clear();
        IsMonitoring = true;
        IsPaused = false;
        RaiseState();
        AddActivity("Start monitoring requested.");
        await coordinator.StartAsync(options, monitoringCancellation.Token).ConfigureAwait(false);
    }

    public Task PauseAsync()
    {
        coordinator.Pause();
        IsMonitoring = false;
        IsPaused = true;
        RaiseState();
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        coordinator.Resume();
        IsMonitoring = true;
        IsPaused = false;
        RaiseState();
        return Task.CompletedTask;
    }

    public async Task StopMonitoringAsync()
    {
        IsMonitoring = false;
        IsPaused = false;
        RaiseState();
        await coordinator.StopAsync(CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<QuickTestResult> RunQuickTestAsync(QuickTestOptions? options = null, MonitoringTargets? targets = null)
    {
        if (IsQuickTestRunning)
        {
            throw new InvalidOperationException("Quick Test is already running.");
        }

        quickTestCancellation = new CancellationTokenSource();
        IsQuickTestRunning = true;
        Status = "Quick test running";
        RaiseState();
        AddActivity("Quick Test started.");
        try
        {
            var result = await quickTestRunner.RunAsync(options ?? new QuickTestOptions(), targets ?? MonitoringTargets.Defaults, quickTestCancellation.Token).ConfigureAwait(false);
            Dispatch(() =>
            {
                speedTests.AddRange(result.SpeedTests);
                latestSessionId = result.Session.Id;
                Status = "Quick test complete";
                CurrentOperation = result.Summary;
                AddActivity(result.Summary);
            });
            return result;
        }
        finally
        {
            Dispatch(() =>
            {
                IsQuickTestRunning = false;
                RaiseState();
            });
        }
    }

    public async Task ExportLatestAsync(string path)
    {
        if (latestSessionId is null)
        {
            return;
        }

        var sessions = await store.GetSessionsAsync(CancellationToken.None).ConfigureAwait(false);
        var sessionMeasurements = await store.GetMeasurementsAsync(latestSessionId.Value, CancellationToken.None).ConfigureAwait(false);
        var sessionSpeeds = await store.GetSpeedTestsAsync(latestSessionId.Value, CancellationToken.None).ConfigureAwait(false);
        await JsonMeasurementExporter.ExportAsync(path, sessions, sessionMeasurements, sessionSpeeds, CancellationToken.None).ConfigureAwait(false);
    }

    private void ApplySession(MonitoringSession session)
    {
        latestSessionId = session.Id;
        Status = session.Status switch
        {
            SessionStatus.Running => "Monitoring",
            SessionStatus.Paused => "Paused",
            SessionStatus.Completed => "Complete",
            SessionStatus.Stopped => "Stopped",
            SessionStatus.Interrupted => "Interrupted",
            _ => "Ready"
        };
        ElapsedActiveTime = session.ActiveDuration.ToString(@"hh\:mm\:ss");
        RemainingTime = session.Status == SessionStatus.Running ? "Active" : "Not running";
    }

    private void AddMeasurement(ProbeMeasurement measurement)
    {
        measurements.Add(measurement);
        latestSessionId = measurement.SessionId;
        LastMeasurementAge = "0 seconds ago";
        CurrentOperation = $"{measurement.Method} {measurement.TargetName}: {(measurement.Succeeded ? "success" : measurement.FailureCategory ?? "failed")}";
        if (measurement is { Succeeded: true, LatencyMilliseconds: not null } && measurement.Method is ProbeMethod.Icmp or ProbeMethod.TcpConnect or ProbeMethod.Dns or ProbeMethod.Https)
        {
            Latency = $"{measurement.LatencyMilliseconds.Value:0} ms";
        }

        var losses = PacketLossSummary.ByIcmpTarget(measurements);
        var externalLoss = losses.Where(loss => !string.Equals(loss.TargetName, "Local Gateway", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (externalLoss.Length > 0)
        {
            var sent = externalLoss.Sum(loss => loss.Sent);
            var lost = externalLoss.Sum(loss => loss.Lost);
            PacketLoss = sent == 0 ? "-- %" : $"{lost * 100.0 / sent:0.0}%";
        }

        var jitterBySeries = JitterCalculator.MeanAbsoluteDifferenceBySeries(measurements);
        var currentKey = new LatencySeriesKey(measurement.TargetName, measurement.Method);
        if (jitterBySeries.TryGetValue(currentKey, out var latestJitter) && !double.IsNaN(latestJitter))
        {
            Jitter = $"{latestJitter:0.0} ms";
        }

        RouterHealth = LastTargetHealth("Local Gateway", ProbeMethod.Icmp);
        InternetHealth = LatestMethodHealth(ProbeMethod.Icmp, excludeGateway: true);
        DnsHealth = LatestMethodHealth(ProbeMethod.Dns, excludeGateway: false);
        HttpsHealth = LatestMethodHealth(ProbeMethod.Https, excludeGateway: false);
        AddActivity(CurrentOperation);
    }

    private void AddSpeedTest(SpeedTestMeasurement speedTest)
    {
        speedTests.Add(speedTest);
        AddActivity(speedTest.Succeeded
            ? $"{speedTest.Direction} estimate: {speedTest.MegabitsPerSecond:0.0} Mbps."
            : $"{speedTest.Direction} estimate unavailable: {speedTest.FailureCategory}.");
    }

    private string LastTargetHealth(string targetName, ProbeMethod method)
    {
        var latest = measurements.LastOrDefault(measurement => measurement.Method == method && string.Equals(measurement.TargetName, targetName, StringComparison.OrdinalIgnoreCase));
        return latest is null ? "Unknown" : latest.Succeeded ? "Healthy" : "Problem detected";
    }

    private string LatestMethodHealth(ProbeMethod method, bool excludeGateway)
    {
        var latest = measurements.LastOrDefault(measurement => measurement.Method == method && (!excludeGateway || !string.Equals(measurement.TargetName, "Local Gateway", StringComparison.OrdinalIgnoreCase)));
        return latest is null ? "Unknown" : latest.Succeeded ? "Healthy" : "Problem detected";
    }

    private void AddActivity(string message)
    {
        RecentActivity.Insert(0, $"{DateTime.Now:HH:mm:ss} - {message}");
        while (RecentActivity.Count > 80)
        {
            RecentActivity.RemoveAt(RecentActivity.Count - 1);
        }
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(IsMonitoring));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsQuickTestRunning));
        ((RelayCommand)StartCommand).RaiseCanExecuteChanged();
        ((RelayCommand)PauseCommand).RaiseCanExecuteChanged();
        ((RelayCommand)ResumeCommand).RaiseCanExecuteChanged();
        ((RelayCommand)StopCommand).RaiseCanExecuteChanged();
        ((RelayCommand)QuickTestCommand).RaiseCanExecuteChanged();
    }

    private void Dispatch(Action action) => dispatch(action);

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
