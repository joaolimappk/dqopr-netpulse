using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly IMonitoringClock clock;
    private readonly ApplicationSettingsStore settingsStore;
    private readonly Action<Action> dispatch;
    private readonly Action<string, string> showMessage;
    private readonly Func<string, string, bool> confirmAction;
    private readonly Action requestClose;
    private readonly List<ProbeMeasurement> measurements = [];
    private readonly List<SpeedTestMeasurement> speedTests = [];
    private CancellationTokenSource? monitoringCancellation;
    private CancellationTokenSource? quickTestCancellation;
    private ApplicationSettings settings;
    private string status = "Ready";
    private string elapsedActiveTime = "00:00:00";
    private string remainingTime = "Not started";
    private string lastMeasurementAge = "none";
    private string routerLatency = "-- ms";
    private string internetLatency = "Waiting for samples";
    private string internetLatencyTarget = "No ICMP target";
    private string packetLoss = "-- %";
    private string internetJitter = "Insufficient samples";
    private string routerHealth = "Unknown";
    private string internetHealth = "Unknown";
    private string dnsHealth = "Unknown";
    private string tcpHealth = "Unknown";
    private string httpsHealth = "Unknown";
    private string currentOperation = "Waiting to start.";
    private string currentInterface = "Unknown";
    private string defaultGateway = "Unknown";
    private string latestDownload = "Unavailable";
    private string latestUpload = "Unavailable";
    private string feedback = "";
    private string exportDirectory = "";
    private string lastGeneratedFile = "";
    private string referenceProvider = "";
    private string referenceDownload = "";
    private string referenceUpload = "";
    private string referenceLatency = "";
    private string referenceNotes = "";
    private string referenceFeedback = "";
    private int selectedTabIndex;
    private Guid? latestSessionId;
    private SessionSummaryViewModel? selectedSession;
    private string settingsValidation = "";
    private bool initialized;

    public DashboardViewModel(
        MonitoringCoordinator coordinator,
        QuickTestRunner quickTestRunner,
        INetPulseStore store,
        IMonitoringClock clock,
        ApplicationSettingsStore settingsStore,
        ApplicationSettings defaultSettings,
        Action<Action> dispatch,
        Action<string, string> showMessage,
        Func<string, string, bool> confirmAction,
        Action requestClose)
    {
        this.coordinator = coordinator;
        this.quickTestRunner = quickTestRunner;
        this.store = store;
        this.clock = clock;
        this.settingsStore = settingsStore;
        settings = defaultSettings;
        DraftSettings = defaultSettings with { };
        this.dispatch = dispatch;
        this.showMessage = showMessage;
        this.confirmAction = confirmAction;
        this.requestClose = requestClose;
        exportDirectory = defaultSettings.ExportDirectory;

        StartCommand = new RelayCommand(StartMonitoringAsync, () => !IsMonitoring && !IsQuickTestRunning);
        PauseCommand = new RelayCommand(PauseAsync, () => IsMonitoring);
        ResumeCommand = new RelayCommand(ResumeAsync, () => IsPaused);
        StopCommand = new RelayCommand(StopMonitoringAsync, () => IsMonitoring || IsPaused);
        QuickTestCommand = new RelayCommand(() => RunQuickTestAsync(), () => !IsMonitoring && !IsQuickTestRunning);
        MarkerCommand = new RelayCommand(AddManualMarkerAsync, () => latestSessionId is not null);
        RefreshCommand = new RelayCommand(RefreshHistoryAsync);
        OpenSessionCommand = new RelayCommand(OpenSelectedSessionAsync, () => SelectedSession is not null);
        DeleteSelectedSessionCommand = new RelayCommand(DeleteSelectedSessionAsync, () => SelectedSession is not null);
        ExportCurrentSessionCommand = new RelayCommand(() => ExportSelectedSessionAsync("all"), () => SelectedSession is not null || latestSessionId is not null);
        ExportCsvCommand = new RelayCommand(() => ExportSelectedSessionAsync("csv"), () => SelectedSession is not null || latestSessionId is not null);
        ExportJsonCommand = new RelayCommand(() => ExportSelectedSessionAsync("json"), () => SelectedSession is not null || latestSessionId is not null);
        GenerateHtmlReportCommand = new RelayCommand(() => ExportSelectedSessionAsync("html"), () => SelectedSession is not null || latestSessionId is not null);
        OpenExportFolderCommand = new RelayCommand(() => OpenFolderAsync(ExportDirectory));
        OpenLastExportCommand = new RelayCommand(OpenLastGeneratedFileAsync, () => !string.IsNullOrWhiteSpace(LastGeneratedFile) && File.Exists(LastGeneratedFile));
        SaveReferenceResultCommand = new RelayCommand(SaveReferenceResultAsync);
        SaveSettingsCommand = new RelayCommand(SaveSettingsAsync);
        CancelSettingsCommand = new RelayCommand(CancelSettingsAsync);
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaultsAsync);
        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnosticsAsync);
        ClearActivityCommand = new RelayCommand(() => { ActivityLog.Clear(); return Task.CompletedTask; });
        CopyAllActivityCommand = new RelayCommand(CopyAllActivityAsync);
        SaveActivityLogCommand = new RelayCommand(SaveActivityLogAsync);
        ShowDashboardCommand = NavigationCommand(0);
        ShowHistoryCommand = NavigationCommand(1);
        ShowDetailsCommand = NavigationCommand(2);
        ShowReportsCommand = NavigationCommand(3);
        ShowSettingsCommand = NavigationCommand(4);
        ShowActivityLogCommand = NavigationCommand(5);
        ShowAboutCommand = NavigationCommand(6);
        OpenDocumentationCommand = new RelayCommand(() => OpenUrlAsync("https://github.com/joaolimappk/dqopr-netpulse"));
        ReportIssueCommand = new RelayCommand(() => OpenUrlAsync("https://github.com/joaolimappk/dqopr-netpulse/issues"));
        OpenDataFolderCommand = new RelayCommand(() => OpenFolderAsync(AppDataFolder));
        OpenLogsFolderCommand = new RelayCommand(() => OpenFolderAsync(LogsFolder));
        ExitCommand = new RelayCommand(() => { requestClose(); return Task.CompletedTask; });

        coordinator.Activity += (_, args) => Dispatch(() => AddActivity(args.Message));
        coordinator.MeasurementRecorded += (_, args) => Dispatch(() => AddMeasurement(args.Measurement));
        coordinator.SpeedTestRecorded += (_, args) => Dispatch(() => AddSpeedTest(args.SpeedTest));
        coordinator.SessionChanged += (_, args) => Dispatch(() => ApplySession(args.Session));
        coordinator.NetworkInterfaceEventRecorded += (_, args) => Dispatch(() => AddNetworkEvent(args.NetworkEvent));

        quickTestRunner.Activity += (_, args) => Dispatch(() => AddActivity(args.Message));
        quickTestRunner.MeasurementRecorded += (_, args) => Dispatch(() => AddMeasurement(args.Measurement));
        quickTestRunner.NetworkInterfaceEventRecorded += (_, args) => Dispatch(() => AddNetworkEvent(args.NetworkEvent));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> RecentActivity { get; } = [];

    public ObservableCollection<ActivityLogEntry> ActivityLog { get; } = [];

    public ObservableCollection<SessionSummaryViewModel> Sessions { get; } = [];

    public ObservableCollection<ProbeMeasurement> DetailMeasurements { get; } = [];

    public ObservableCollection<ProbeMeasurement> DetailConnectivityMeasurements { get; } = [];

    public ObservableCollection<SpeedTestMeasurement> DetailSpeedTests { get; } = [];

    public ObservableCollection<NetworkInterfaceEvent> DetailNetworkEvents { get; } = [];

    public ObservableCollection<ManualMarker> DetailMarkers { get; } = [];

    public ObservableCollection<TargetPacketLoss> DetailPacketLoss { get; } = [];

    public PointCollection LatencyChartPoints { get; } = [];

    public PointCollection JitterChartPoints { get; } = [];

    public PointCollection PacketLossChartPoints { get; } = [];

    public PointCollection SpeedChartPoints { get; } = [];

    public ICommand StartCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand QuickTestCommand { get; }
    public ICommand MarkerCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenSessionCommand { get; }
    public ICommand DeleteSelectedSessionCommand { get; }
    public ICommand ExportCurrentSessionCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand GenerateHtmlReportCommand { get; }
    public ICommand OpenExportFolderCommand { get; }
    public ICommand OpenLastExportCommand { get; }
    public ICommand SaveReferenceResultCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand CancelSettingsCommand { get; }
    public ICommand RestoreDefaultsCommand { get; }
    public ICommand CopyDiagnosticsCommand { get; }
    public ICommand ClearActivityCommand { get; }
    public ICommand CopyAllActivityCommand { get; }
    public ICommand SaveActivityLogCommand { get; }
    public ICommand ShowDashboardCommand { get; }
    public ICommand ShowHistoryCommand { get; }
    public ICommand ShowDetailsCommand { get; }
    public ICommand ShowReportsCommand { get; }
    public ICommand ShowSettingsCommand { get; }
    public ICommand ShowActivityLogCommand { get; }
    public ICommand ShowAboutCommand { get; }
    public ICommand OpenDocumentationCommand { get; }
    public ICommand ReportIssueCommand { get; }
    public ICommand OpenDataFolderCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand ExitCommand { get; }

    public bool IsMonitoring { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsQuickTestRunning { get; private set; }

    public string Version => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    public string RuntimeInfo => $"{RuntimeInformationDescription} | {Environment.OSVersion}";

    public string RepositoryUrl => "https://github.com/joaolimappk/dqopr-netpulse";

    public bool StartMinimizedEnabled => settings.StartMinimized;

    public bool MinimizeToTrayEnabled => settings.MinimizeToTray;

    public string AppDataFolder => Path.GetDirectoryName(settings.DatabasePath) ?? Environment.CurrentDirectory;

    public string LogsFolder => Path.Combine(AppDataFolder, "logs");

    public string Status { get => status; private set => SetField(ref status, value); }
    public string ElapsedActiveTime { get => elapsedActiveTime; private set => SetField(ref elapsedActiveTime, value); }
    public string RemainingTime { get => remainingTime; private set => SetField(ref remainingTime, value); }
    public string LastMeasurementAge { get => lastMeasurementAge; private set => SetField(ref lastMeasurementAge, value); }
    public string RouterLatency { get => routerLatency; private set => SetField(ref routerLatency, value); }
    public string InternetLatency { get => internetLatency; private set => SetField(ref internetLatency, value); }
    public string InternetLatencyTarget { get => internetLatencyTarget; private set => SetField(ref internetLatencyTarget, value); }
    public string PacketLoss { get => packetLoss; private set => SetField(ref packetLoss, value); }
    public string InternetJitter { get => internetJitter; private set => SetField(ref internetJitter, value); }
    public string RouterHealth { get => routerHealth; private set => SetField(ref routerHealth, value); }
    public string InternetHealth { get => internetHealth; private set => SetField(ref internetHealth, value); }
    public string DnsHealth { get => dnsHealth; private set => SetField(ref dnsHealth, value); }
    public string TcpHealth { get => tcpHealth; private set => SetField(ref tcpHealth, value); }
    public string HttpsHealth { get => httpsHealth; private set => SetField(ref httpsHealth, value); }
    public string CurrentOperation { get => currentOperation; private set => SetField(ref currentOperation, value); }
    public string CurrentInterface { get => currentInterface; private set => SetField(ref currentInterface, value); }
    public string DefaultGateway { get => defaultGateway; private set => SetField(ref defaultGateway, value); }
    public string LatestDownload { get => latestDownload; private set => SetField(ref latestDownload, value); }
    public string LatestUpload { get => latestUpload; private set => SetField(ref latestUpload, value); }
    public string Feedback { get => feedback; private set => SetField(ref feedback, value); }
    public string ExportDirectory { get => exportDirectory; set => SetField(ref exportDirectory, value); }
    public string LastGeneratedFile { get => lastGeneratedFile; private set => SetField(ref lastGeneratedFile, value); }
    public string ReferenceProvider { get => referenceProvider; set => SetField(ref referenceProvider, value); }
    public string ReferenceDownload { get => referenceDownload; set => SetField(ref referenceDownload, value); }
    public string ReferenceUpload { get => referenceUpload; set => SetField(ref referenceUpload, value); }
    public string ReferenceLatency { get => referenceLatency; set => SetField(ref referenceLatency, value); }
    public string ReferenceNotes { get => referenceNotes; set => SetField(ref referenceNotes, value); }
    public string ReferenceFeedback { get => referenceFeedback; private set => SetField(ref referenceFeedback, value); }
    public string SettingsValidation { get => settingsValidation; private set => SetField(ref settingsValidation, value); }

    public int SelectedTabIndex
    {
        get => selectedTabIndex;
        set => SetField(ref selectedTabIndex, value);
    }

    public SessionSummaryViewModel? SelectedSession
    {
        get => selectedSession;
        set
        {
            if (SetField(ref selectedSession, value))
            {
                RaiseState();
            }
        }
    }

    public ApplicationSettings DraftSettings { get; private set; }

    public async Task InitializeAsync()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        await store.InitializeAsync(CancellationToken.None).ConfigureAwait(false);
        settings = await settingsStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);
        DraftSettings = settings with { };
        ExportDirectory = settings.ExportDirectory;
        await store.MarkRunningSessionsInterruptedAsync(clock.UtcNow, CancellationToken.None).ConfigureAwait(false);
        Directory.CreateDirectory(settings.ExportDirectory);
        Directory.CreateDirectory(LogsFolder);
        Dispatch(() =>
        {
            OnPropertyChanged(nameof(DraftSettings));
            OnPropertyChanged(nameof(StartMinimizedEnabled));
            OnPropertyChanged(nameof(MinimizeToTrayEnabled));
            AddActivity("Application initialized.");
        });
        await RefreshHistoryAsync().ConfigureAwait(false);
    }

    public Task StartMonitoringAsync()
        => StartMonitoringAsync(new MonitoringOptions
        {
            ProfileName = "Manual Monitoring",
            ActiveDuration = settings.MonitoringDuration,
            Intervals = settings.ToIntervals(),
            Targets = BuildTargets(settings)
        });

    public async Task StartMonitoringAsync(MonitoringOptions options)
    {
        try
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
        catch (Exception ex)
        {
            Dispatch(() =>
            {
                ShowSafeError("Monitoring could not start.", ex);
                IsMonitoring = false;
                RaiseState();
            });
        }
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
        if (settings.ConfirmBeforeStopping && (IsMonitoring || IsPaused) && !confirmAction("Stop monitoring", "Stop the current monitoring session?"))
        {
            return;
        }

        IsMonitoring = false;
        IsPaused = false;
        RaiseState();
        await coordinator.StopAsync(CancellationToken.None).ConfigureAwait(false);
        await RefreshHistoryAsync().ConfigureAwait(false);
    }

    public async Task<QuickTestResult> RunQuickTestAsync(QuickTestOptions? options = null, MonitoringTargets? targets = null)
    {
        if (IsQuickTestRunning)
        {
            throw new InvalidOperationException("Quick Test is already running.");
        }

        quickTestCancellation = new CancellationTokenSource();
        measurements.Clear();
        speedTests.Clear();
        RouterLatency = "-- ms";
        InternetLatency = "Waiting for samples";
        InternetLatencyTarget = "No ICMP target";
        InternetJitter = "Insufficient samples";
        PacketLoss = "-- %";
        LatestDownload = "Unavailable";
        LatestUpload = "Unavailable";
        IsQuickTestRunning = true;
        Status = "Quick test running";
        RaiseState();
        AddActivity("Quick Test started.");
        try
        {
            var result = await quickTestRunner.RunAsync(options ?? new QuickTestOptions { ProbeTimeout = settings.ProbeTimeout }, targets ?? BuildTargets(settings), quickTestCancellation.Token).ConfigureAwait(false);
            Dispatch(() =>
            {
                foreach (var speedTest in result.SpeedTests)
                {
                    AddSpeedTest(speedTest);
                }

                latestSessionId = result.Session.Id;
                Status = result.SpeedTests.Any(speed => speed.ResultStatus is not (SpeedResultStatus.Valid or SpeedResultStatus.Degraded))
                    ? "Quick test partial"
                    : "Quick test complete";
                CurrentOperation = result.Summary;
                AddActivity(result.Summary);
            });
            await RefreshHistoryAsync().ConfigureAwait(false);
            return result;
        }
        catch (Exception ex)
        {
            ShowSafeError("Quick Test failed.", ex);
            throw;
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
        var sessionId = SelectedSession?.Id ?? latestSessionId;
        if (sessionId is null)
        {
            return;
        }

        var sessions = await store.GetSessionsAsync(CancellationToken.None).ConfigureAwait(false);
        var sessionMeasurements = await store.GetMeasurementsAsync(sessionId.Value, CancellationToken.None).ConfigureAwait(false);
        var sessionSpeeds = await store.GetSpeedTestsAsync(sessionId.Value, CancellationToken.None).ConfigureAwait(false);
        await JsonMeasurementExporter.ExportAsync(path, sessions, sessionMeasurements, sessionSpeeds, CancellationToken.None).ConfigureAwait(false);
        Dispatch(() => AddActivity($"Export completed: {path}"));
    }

    public async Task RefreshHistoryAsync()
    {
        var sessions = await store.GetSessionsAsync(CancellationToken.None).ConfigureAwait(false);
        var summaries = new List<SessionSummaryViewModel>();
        foreach (var session in sessions)
        {
            var sessionMeasurements = await store.GetMeasurementsAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
            var sessionSpeeds = await store.GetSpeedTestsAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
            var events = await store.GetNetworkInterfaceEventsAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
            summaries.Add(SessionSummaryViewModel.From(session, sessionMeasurements, sessionSpeeds, events));
        }

        Dispatch(() =>
        {
            Sessions.Clear();
            foreach (var summary in summaries)
            {
                Sessions.Add(summary);
            }

            SelectedSession ??= Sessions.FirstOrDefault();
            AddActivity($"History refreshed: {Sessions.Count} session(s).");
        });
    }

    public async Task OpenSelectedSessionAsync()
    {
        if (SelectedSession is null)
        {
            Feedback = "Select a session first.";
            return;
        }

        var session = (await store.GetSessionsAsync(CancellationToken.None).ConfigureAwait(false)).FirstOrDefault(item => item.Id == SelectedSession.Id);
        if (session is null)
        {
            Dispatch(() => { Feedback = "The selected session is no longer available."; });
            await RefreshHistoryAsync().ConfigureAwait(false);
            return;
        }

        var sessionMeasurements = await store.GetMeasurementsAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
        var sessionSpeeds = await store.GetSpeedTestsAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
        var events = await store.GetNetworkInterfaceEventsAsync(session.Id, CancellationToken.None).ConfigureAwait(false);
        var markers = await store.GetManualMarkersAsync(session.Id, CancellationToken.None).ConfigureAwait(false);

        Dispatch(() =>
        {
            DetailMeasurements.ReplaceWith(sessionMeasurements);
            DetailConnectivityMeasurements.ReplaceWith(sessionMeasurements.Where(measurement => measurement.Method is ProbeMethod.Dns or ProbeMethod.TcpConnect or ProbeMethod.Https));
            DetailSpeedTests.ReplaceWith(sessionSpeeds);
            DetailNetworkEvents.ReplaceWith(events);
            DetailMarkers.ReplaceWith(markers);
            DetailPacketLoss.ReplaceWith(PacketLossSummary.ByIcmpTarget(sessionMeasurements));
            BuildCharts(sessionMeasurements, sessionSpeeds);
            SelectedTabIndex = 2;
            Feedback = $"Opened session {session.Id}.";
            AddActivity($"Opened session {session.Id}.");
        });
    }

    public async Task DeleteSelectedSessionAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        if (!confirmAction("Delete session", $"Delete session {SelectedSession.Id}? This removes its measurements, speed tests, events, and markers."))
        {
            return;
        }

        await store.DeleteSessionAsync(SelectedSession.Id, CancellationToken.None).ConfigureAwait(false);
        Dispatch(() =>
        {
            AddActivity($"Deleted session {SelectedSession.Id}.");
            SelectedSession = null;
        });
        await RefreshHistoryAsync().ConfigureAwait(false);
    }

    public async Task ExportSelectedSessionAsync(string kind)
    {
        var sessionId = SelectedSession?.Id ?? latestSessionId;
        if (sessionId is null)
        {
            Feedback = "Select a session first.";
            return;
        }

        var sessions = await store.GetSessionsAsync(CancellationToken.None).ConfigureAwait(false);
        var session = sessions.FirstOrDefault(item => item.Id == sessionId.Value);
        if (session is null)
        {
            Dispatch(() => { Feedback = "The selected session is no longer available for export."; });
            await RefreshHistoryAsync().ConfigureAwait(false);
            return;
        }

        var sessionMeasurements = await store.GetMeasurementsAsync(sessionId.Value, CancellationToken.None).ConfigureAwait(false);
        var sessionSpeeds = await store.GetSpeedTestsAsync(sessionId.Value, CancellationToken.None).ConfigureAwait(false);
        var events = await store.GetNetworkInterfaceEventsAsync(sessionId.Value, CancellationToken.None).ConfigureAwait(false);
        var markers = await store.GetManualMarkersAsync(sessionId.Value, CancellationToken.None).ConfigureAwait(false);
        var referenceResults = await store.GetReferenceSpeedResultsAsync(sessionId.Value, CancellationToken.None).ConfigureAwait(false);

        var outputDirectory = string.IsNullOrWhiteSpace(ExportDirectory) ? settings.ExportDirectory : ExportDirectory;
        Directory.CreateDirectory(outputDirectory);
        var generated = new List<string>();
        if (kind is "csv" or "all")
        {
            var path = Path.Combine(outputDirectory, $"netpulse-{sessionId:N}.csv");
            await CsvSessionExporter.ExportMeasurementsAsync(path, sessionMeasurements, sessionSpeeds, CancellationToken.None).ConfigureAwait(false);
            generated.Add(path);
        }

        if (kind is "json" or "all")
        {
            var path = Path.Combine(outputDirectory, $"netpulse-{sessionId:N}.json");
            await JsonMeasurementExporter.ExportAsync(path, sessions, sessionMeasurements, sessionSpeeds, CancellationToken.None).ConfigureAwait(false);
            generated.Add(path);
        }

        if (kind is "html" or "all")
        {
            var path = Path.Combine(outputDirectory, $"netpulse-{sessionId:N}.html");
            await HtmlReportGenerator.GenerateAsync(path, session, sessionMeasurements, sessionSpeeds, events, markers, CancellationToken.None).ConfigureAwait(false);
            generated.Add(path);
        }

        if (kind is "all")
        {
            var path = Path.Combine(outputDirectory, $"netpulse-diagnostics-{sessionId:N}.json");
            await DiagnosticBundleExporter.ExportAsync(path, session, sessionMeasurements, sessionSpeeds, events, markers, referenceResults, CancellationToken.None).ConfigureAwait(false);
            generated.Add(path);
        }

        Dispatch(() =>
        {
            LastGeneratedFile = generated.LastOrDefault() ?? "";
            Feedback = $"Export completed in {outputDirectory}.";
            AddActivity(Feedback);
            RaiseState();
        });
    }

    public async Task AddManualMarkerAsync()
    {
        if (latestSessionId is null)
        {
            Feedback = "No active or recent session is available for a marker.";
            return;
        }

        var marker = new ManualMarker(Guid.NewGuid(), latestSessionId.Value, clock.UtcNow, "Internet felt bad now.");
        await store.SaveManualMarkerAsync(marker, CancellationToken.None).ConfigureAwait(false);
        DetailMarkers.Add(marker);
        AddActivity("Manual issue marker saved.");
    }

    public async Task SaveReferenceResultAsync()
    {
        var sessionId = SelectedSession?.Id ?? latestSessionId;
        var reference = new ReferenceSpeedResult(
            Guid.NewGuid(),
            sessionId,
            clock.UtcNow,
            string.IsNullOrWhiteSpace(ReferenceProvider) ? "External reference speed test" : ReferenceProvider.Trim(),
            ParseOptionalDouble(ReferenceDownload),
            ParseOptionalDouble(ReferenceUpload),
            ParseOptionalDouble(ReferenceLatency),
            string.IsNullOrWhiteSpace(ReferenceNotes) ? null : ReferenceNotes.Trim());

        await store.SaveReferenceSpeedResultAsync(reference, CancellationToken.None).ConfigureAwait(false);
        var comparison = sessionId is null ? "No NetPulse session selected for comparison." : await BuildReferenceComparisonAsync(sessionId.Value, reference).ConfigureAwait(false);
        Dispatch(() =>
        {
            ReferenceFeedback = $"Reference saved. {comparison}";
            Feedback = ReferenceFeedback;
            AddActivity(ReferenceFeedback);
        });
    }

    public async Task SaveSettingsAsync()
    {
        var errors = DraftSettings.Validate();
        if (errors.Count > 0)
        {
            SettingsValidation = string.Join(Environment.NewLine, errors);
            return;
        }

        var saved = DraftSettings with { };
        await settingsStore.SaveAsync(saved, CancellationToken.None).ConfigureAwait(false);
        Dispatch(() =>
        {
            settings = saved;
            DraftSettings = settings with { };
            ExportDirectory = settings.ExportDirectory;
            SettingsValidation = "Settings saved.";
            AddActivity("Settings saved.");
            OnPropertyChanged(nameof(AppDataFolder));
            OnPropertyChanged(nameof(LogsFolder));
            OnPropertyChanged(nameof(StartMinimizedEnabled));
            OnPropertyChanged(nameof(MinimizeToTrayEnabled));
        });
    }

    public Task CancelSettingsAsync()
    {
        DraftSettings = settings with { };
        SettingsValidation = "Changes discarded.";
        OnPropertyChanged(nameof(DraftSettings));
        return Task.CompletedTask;
    }

    public Task RestoreDefaultsAsync()
    {
        DraftSettings = ApplicationSettings.Defaults(settings.DatabasePath, settings.ExportDirectory);
        SettingsValidation = "Defaults restored. Press Save to persist them.";
        OnPropertyChanged(nameof(DraftSettings));
        return Task.CompletedTask;
    }

    private Task CopyDiagnosticsAsync()
    {
        System.Windows.Clipboard.SetText($"DQOPR NetPulse {Version}{Environment.NewLine}{RuntimeInfo}{Environment.NewLine}Database: {settings.DatabasePath}");
        Feedback = "Diagnostic information copied.";
        return Task.CompletedTask;
    }

    private Task CopyAllActivityAsync()
    {
        System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, ActivityLog.Select(item => $"{item.Timestamp:O} {item.Message}")));
        Feedback = "Activity log copied.";
        return Task.CompletedTask;
    }

    private async Task SaveActivityLogAsync()
    {
        Directory.CreateDirectory(LogsFolder);
        var path = Path.Combine(LogsFolder, $"netpulse-activity-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var lines = ActivityLog.Select(item => $"{item.Timestamp:O} {item.Message}").ToArray();
        await File.WriteAllLinesAsync(path, lines).ConfigureAwait(false);
        Dispatch(() => { Feedback = $"Activity log saved: {path}"; });
    }

    private Task OpenLastGeneratedFileAsync()
    {
        if (!File.Exists(LastGeneratedFile))
        {
            Feedback = "No generated file is available to open.";
            RaiseState();
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo(LastGeneratedFile) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private void ApplySession(MonitoringSession session)
    {
        latestSessionId = session.Id;
        IsMonitoring = session.Status == SessionStatus.Running;
        IsPaused = session.Status == SessionStatus.Paused;
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
        if (session.Status is SessionStatus.Completed or SessionStatus.Stopped or SessionStatus.Interrupted)
        {
            _ = RefreshHistoryAsync();
        }

        RaiseState();
    }

    private void AddMeasurement(ProbeMeasurement measurement)
    {
        measurements.Add(measurement);
        latestSessionId = measurement.SessionId;
        LastMeasurementAge = "0 seconds ago";
        CurrentOperation = $"{measurement.Method} {measurement.TargetName}: {(measurement.Succeeded ? "success" : measurement.FailureCategory ?? "failed")}";
        RefreshScopedLatencyCards(measurement);

        RouterHealth = LastTargetHealth("Local Gateway", ProbeMethod.Icmp);
        InternetHealth = LatestMethodHealth(ProbeMethod.Icmp, excludeGateway: true);
        DnsHealth = LatestMethodHealth(ProbeMethod.Dns, excludeGateway: false);
        TcpHealth = LatestMethodHealth(ProbeMethod.TcpConnect, excludeGateway: false);
        HttpsHealth = LatestMethodHealth(ProbeMethod.Https, excludeGateway: false);
        AddActivity(CurrentOperation);
    }

    private void AddSpeedTest(SpeedTestMeasurement speedTest)
    {
        speedTests.Add(speedTest);
        var display = FormatSpeed(speedTest);
        if (speedTest.Direction.Equals("download", StringComparison.OrdinalIgnoreCase))
        {
            LatestDownload = display;
        }

        if (speedTest.Direction.Equals("upload", StringComparison.OrdinalIgnoreCase))
        {
            LatestUpload = display;
        }

        AddActivity(IsDisplayableSpeed(speedTest)
            ? $"{speedTest.Direction} estimate: {speedTest.MegabitsPerSecond:0.0} Mbps ({speedTest.ResultStatus}, {speedTest.Provider})."
            : $"{speedTest.Direction} estimate unavailable: {speedTest.ResultStatus} {speedTest.FailureCategory}.");
    }

    private void RefreshScopedLatencyCards(ProbeMeasurement latestMeasurement)
    {
        if (latestMeasurement.Method != ProbeMethod.Icmp)
        {
            return;
        }

        var gateway = measurements.LastOrDefault(IsGatewayIcmp);
        if (gateway is not null)
        {
            var stats = JitterCalculator.CalculateIcmpStatistics(measurements, LatencySeriesKey.From(gateway));
            RouterLatency = FormatMedianLatency(stats);
        }

        var internet = measurements.LastOrDefault(measurement => measurement.Method == ProbeMethod.Icmp && !IsGateway(measurement));
        if (internet is null)
        {
            return;
        }

        var internetStats = JitterCalculator.CalculateIcmpStatistics(measurements, LatencySeriesKey.From(internet));
        InternetLatencyTarget = TargetDisplay(internet);
        InternetLatency = $"{FormatMedianLatency(internetStats)} to {InternetLatencyTarget}";
        InternetJitter = internetStats.MeanAbsoluteSuccessiveDifferenceMilliseconds is null
            ? $"Insufficient samples for {InternetLatencyTarget}"
            : $"{internetStats.MeanAbsoluteSuccessiveDifferenceMilliseconds:0.0} ms to {InternetLatencyTarget}";

        var loss = PacketLossSummary.ByIcmpTarget(measurements)
            .FirstOrDefault(item => string.Equals(item.TargetName, internet.TargetName, StringComparison.OrdinalIgnoreCase));
        PacketLoss = loss is null ? "-- %" : $"{loss.LossPercent:0.0}% to {InternetLatencyTarget}";
    }

    private void AddNetworkEvent(NetworkInterfaceEvent networkEvent)
    {
        CurrentInterface = networkEvent.InterfaceName ?? "Unknown";
        DefaultGateway = networkEvent.Gateway ?? "Unknown";
        AddActivity($"Interface event: {networkEvent.EventType} {networkEvent.InterfaceName} {networkEvent.Gateway}");
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
        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == false)
        {
            Dispatch(() => AddActivityCore(message));
            return;
        }

        AddActivityCore(message);
    }

    private void AddActivityCore(string message)
    {
        var entry = new ActivityLogEntry(DateTimeOffset.Now, message);
        ActivityLog.Insert(0, entry);
        RecentActivity.Insert(0, $"{entry.Timestamp:HH:mm:ss} - {message}");
        while (RecentActivity.Count > 80)
        {
            RecentActivity.RemoveAt(RecentActivity.Count - 1);
        }
    }

    private void BuildCharts(IReadOnlyList<ProbeMeasurement> sessionMeasurements, IReadOnlyList<SpeedTestMeasurement> sessionSpeeds)
    {
        LatencyChartPoints.Clear();
        JitterChartPoints.Clear();
        PacketLossChartPoints.Clear();
        SpeedChartPoints.Clear();

        var latency = sessionMeasurements.Where(item => item is { Method: ProbeMethod.Icmp, Succeeded: true, LatencyMilliseconds: not null } && !IsGateway(item)).TakeLast(80).ToArray();
        for (var index = 0; index < latency.Length; index++)
        {
            LatencyChartPoints.Add(new System.Windows.Point(index * 6, 120 - Math.Min(115, latency[index].LatencyMilliseconds!.Value)));
        }

        var jitterValues = JitterCalculator.MeanAbsoluteDifferenceBySeries(sessionMeasurements).Values.Where(value => !double.IsNaN(value)).Take(80).ToArray();
        for (var index = 0; index < jitterValues.Length; index++)
        {
            JitterChartPoints.Add(new System.Windows.Point(index * 6, 120 - Math.Min(115, jitterValues[index])));
        }

        var lossValues = PacketLossSummary.ByIcmpTarget(sessionMeasurements).ToArray();
        for (var index = 0; index < lossValues.Length; index++)
        {
            PacketLossChartPoints.Add(new System.Windows.Point(index * 40, 120 - Math.Min(115, lossValues[index].LossPercent)));
        }

        var speeds = sessionSpeeds.Where(IsDisplayableSpeed).TakeLast(80).ToArray();
        for (var index = 0; index < speeds.Length; index++)
        {
            SpeedChartPoints.Add(new System.Windows.Point(index * 12, 120 - Math.Min(115, speeds[index].MegabitsPerSecond!.Value)));
        }
    }

    private MonitoringTargets BuildTargets(ApplicationSettings currentSettings)
    {
        return new MonitoringTargets(
            IcmpTargets: ParseIcmpTargets(currentSettings.IcmpTargets),
            TcpTargets: ParseTcpTargets(currentSettings.TcpEndpoints),
            DnsHostname: currentSettings.DnsHostname,
            HttpsUri: new Uri(currentSettings.HttpsEndpoint),
            DownloadUri: new Uri(currentSettings.DownloadEndpoint),
            UploadUri: new Uri(currentSettings.UploadEndpoint));
    }

    private static IReadOnlyList<TargetDefinition> ParseIcmpTargets(string text)
        => text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item =>
            {
                var parts = item.Split('=', 2, StringSplitOptions.TrimEntries);
                return new TargetDefinition(parts.Length == 2 ? parts[0] : item, TargetPurpose.ExternalIcmp, parts.Length == 2 ? parts[1] : item);
            })
            .ToArray();

    private static IReadOnlyList<TargetDefinition> ParseTcpTargets(string text)
        => text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item =>
            {
                var parts = item.Split('=', 2, StringSplitOptions.TrimEntries);
                var label = parts.Length == 2 ? parts[0] : item;
                var hostPort = (parts.Length == 2 ? parts[1] : item).Split(':', 2, StringSplitOptions.TrimEntries);
                return new TargetDefinition(label, TargetPurpose.TcpConnect, hostPort[0], hostPort.Length == 2 && int.TryParse(hostPort[1], out var port) ? port : 443);
            })
            .ToArray();

    private static bool IsGatewayIcmp(ProbeMeasurement measurement)
        => measurement.Method == ProbeMethod.Icmp && IsGateway(measurement);

    private static bool IsGateway(ProbeMeasurement measurement)
        => string.Equals(measurement.TargetName, "Local Gateway", StringComparison.OrdinalIgnoreCase);

    private static string TargetDisplay(ProbeMeasurement measurement)
        => string.IsNullOrWhiteSpace(measurement.TargetHost) || string.Equals(measurement.TargetName, measurement.TargetHost, StringComparison.OrdinalIgnoreCase)
            ? measurement.TargetName
            : $"{measurement.TargetName} ({measurement.TargetHost})";

    private static string FormatMedianLatency(IcmpLatencyStatistics statistics)
        => statistics.MedianRttMilliseconds is null ? "Unavailable" : $"{statistics.MedianRttMilliseconds:0.0} ms";

    internal static bool IsDisplayableSpeed(SpeedTestMeasurement speedTest)
        => speedTest is { Succeeded: true, MegabitsPerSecond: not null }
            && (speedTest.ResultStatus is SpeedResultStatus.Valid or SpeedResultStatus.Degraded)
            && speedTest.MethodologyVersion == MeasurementMethodology.CurrentVersion;

    private static string FormatSpeed(SpeedTestMeasurement speedTest)
        => IsDisplayableSpeed(speedTest)
            ? $"{speedTest.MegabitsPerSecond:0.0} Mbps ({speedTest.ResultStatus})"
            : $"Unavailable ({speedTest.ResultStatus})";

    private async Task<string> BuildReferenceComparisonAsync(Guid sessionId, ReferenceSpeedResult reference)
    {
        var sessionSpeeds = await store.GetSpeedTestsAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
        var sessionMeasurements = await store.GetMeasurementsAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
        var download = sessionSpeeds.LastOrDefault(speed => speed.Direction.Equals("download", StringComparison.OrdinalIgnoreCase) && IsDisplayableSpeed(speed));
        var upload = sessionSpeeds.LastOrDefault(speed => speed.Direction.Equals("upload", StringComparison.OrdinalIgnoreCase) && IsDisplayableSpeed(speed));
        var internetIcmp = sessionMeasurements
            .Where(measurement => measurement is { Method: ProbeMethod.Icmp, Succeeded: true, LatencyMilliseconds: not null } && !IsGateway(measurement))
            .ToArray();
        double? medianLatency = internetIcmp.Length == 0 ? null : Median(internetIcmp.Select(measurement => measurement.LatencyMilliseconds!.Value));

        return string.Join(" ", new[]
        {
            ErrorText("download", download?.MegabitsPerSecond, reference.DownloadMegabitsPerSecond, "%"),
            ErrorText("upload", upload?.MegabitsPerSecond, reference.UploadMegabitsPerSecond, "%"),
            DifferenceText("latency", medianLatency, reference.LatencyMilliseconds)
        }.Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static double? ParseOptionalDouble(string text)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
           double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            ? value
            : null;

    private static string ErrorText(string label, double? netPulse, double? reference, string suffix)
    {
        if (netPulse is null || reference is null || reference == 0)
        {
            return "";
        }

        var error = (netPulse.Value - reference.Value) / reference.Value * 100.0;
        return $"{label} error {error:+0.0;-0.0;0.0}{suffix}.";
    }

    private static string DifferenceText(string label, double? netPulse, double? reference)
        => netPulse is null || reference is null ? "" : $"{label} difference {netPulse.Value - reference.Value:+0.0;-0.0;0.0} ms.";

    private static double Median(IEnumerable<double> values)
    {
        var ordered = values.OrderBy(value => value).ToArray();
        if (ordered.Length == 0)
        {
            return double.NaN;
        }

        var mid = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[mid - 1] + ordered[mid]) / 2.0 : ordered[mid];
    }

    private ICommand NavigationCommand(int tabIndex) => new RelayCommand(() => { SelectedTabIndex = tabIndex; return Task.CompletedTask; });

    private Task OpenUrlAsync(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private Task OpenFolderAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private void ShowSafeError(string title, Exception ex)
    {
        AddActivity($"Application error: {title} {ex.GetType().Name}");
        Feedback = title;
        showMessage(title, ex.Message);
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(IsMonitoring));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(IsQuickTestRunning));
        foreach (var command in new[] { StartCommand, PauseCommand, ResumeCommand, StopCommand, QuickTestCommand, MarkerCommand, OpenSessionCommand, DeleteSelectedSessionCommand, ExportCurrentSessionCommand, ExportCsvCommand, ExportJsonCommand, GenerateHtmlReportCommand, OpenLastExportCommand, SaveReferenceResultCommand })
        {
            ((RelayCommand)command).RaiseCanExecuteChanged();
        }
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

    private static string RuntimeInformationDescription => $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}";
}

public sealed record ActivityLogEntry(DateTimeOffset Timestamp, string Message);

public sealed record SessionSummaryViewModel(
    Guid Id,
    string Start,
    string End,
    TimeSpan Duration,
    string Status,
    string Interface,
    string Gateway,
    int Measurements,
    string PacketLoss,
    string AverageLatency,
    string MaximumLatency,
    string Download,
    string Upload,
    string Methodology)
{
    public static SessionSummaryViewModel From(MonitoringSession session, IReadOnlyList<ProbeMeasurement> measurements, IReadOnlyList<SpeedTestMeasurement> speedTests, IReadOnlyList<NetworkInterfaceEvent> events)
    {
        var successfulInternetIcmp = measurements.Where(item => item is { Method: ProbeMethod.Icmp, Succeeded: true, LatencyMilliseconds: not null } && !IsGateway(item)).ToArray();
        var loss = PacketLossSummary.ByIcmpTarget(measurements).ToArray();
        var sent = loss.Sum(item => item.Sent);
        var lost = loss.Sum(item => item.Lost);
        var latestEvent = events.LastOrDefault();
        var download = speedTests.LastOrDefault(item => item.Direction.Equals("download", StringComparison.OrdinalIgnoreCase) && DashboardViewModel.IsDisplayableSpeed(item));
        var upload = speedTests.LastOrDefault(item => item.Direction.Equals("upload", StringComparison.OrdinalIgnoreCase) && DashboardViewModel.IsDisplayableSpeed(item));

        return new SessionSummaryViewModel(
            session.Id,
            FormatLocal(session.StartedAt),
            session.EndedAt is null ? "" : FormatLocal(session.EndedAt.Value),
            session.ActiveDuration,
            session.Status.ToString(),
            latestEvent?.InterfaceName ?? "Unknown",
            latestEvent?.Gateway ?? "Unknown",
            measurements.Count,
            sent == 0 ? "n/a" : $"{lost * 100.0 / sent:0.0}%",
            successfulInternetIcmp.Length == 0 ? "n/a" : $"{successfulInternetIcmp.Average(item => item.LatencyMilliseconds!.Value):0.0} ms ICMP",
            successfulInternetIcmp.Length == 0 ? "n/a" : $"{successfulInternetIcmp.Max(item => item.LatencyMilliseconds!.Value):0.0} ms ICMP",
            download?.MegabitsPerSecond is null ? SpeedFallback(speedTests, "download") : $"{download.MegabitsPerSecond:0.0} Mbps",
            upload?.MegabitsPerSecond is null ? SpeedFallback(speedTests, "upload") : $"{upload.MegabitsPerSecond:0.0} Mbps",
            session.MethodologyVersion);
    }

    private static string FormatLocal(DateTimeOffset value)
        => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture);

    private static string SpeedFallback(IReadOnlyList<SpeedTestMeasurement> speedTests, string direction)
    {
        var latest = speedTests.LastOrDefault(item => item.Direction.Equals(direction, StringComparison.OrdinalIgnoreCase));
        return latest is null ? "n/a" : latest.ResultStatus == SpeedResultStatus.LegacyEstimate ? "Legacy estimate" : latest.ResultStatus;
    }

    private static bool IsGateway(ProbeMeasurement measurement)
        => string.Equals(measurement.TargetName, "Local Gateway", StringComparison.OrdinalIgnoreCase);
}

internal static class ObservableExtensions
{
    public static void ReplaceWith<T>(this ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
