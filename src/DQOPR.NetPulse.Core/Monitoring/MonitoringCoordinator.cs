using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Core.Scheduling;
using DQOPR.NetPulse.Core.Storage;
using DQOPR.NetPulse.Core.Time;

namespace DQOPR.NetPulse.Core.Monitoring;

public sealed class MonitoringCoordinator(
    INetworkProbeService probes,
    INetworkEnvironmentService environment,
    INetPulseStore store,
    IMonitoringClock clock)
{
    private readonly object gate = new();
    private readonly INetworkProbeService probes = probes;
    private readonly INetworkEnvironmentService environment = environment;
    private readonly INetPulseStore store = store;
    private readonly IMonitoringClock clock = clock;
    private CancellationTokenSource? runCancellation;
    private Task? runTask;
    private SessionTimer? timer;
    private MonitoringSession? session;
    private TargetDefinition? gatewayTarget;
    private int sequence;
    private bool hasActiveRun;

    public event EventHandler<ActivityEventArgs>? Activity;

    public event EventHandler<MeasurementEventArgs>? MeasurementRecorded;

    public event EventHandler<SpeedTestEventArgs>? SpeedTestRecorded;

    public event EventHandler<SessionEventArgs>? SessionChanged;

    public MonitoringSession? CurrentSession => session;

    public async Task<MonitoringSession> StartAsync(MonitoringOptions options, CancellationToken cancellationToken)
    {
        MonitoringSession newSession;
        SessionTimer newTimer;
        CancellationTokenSource newRunCancellation;

        lock (gate)
        {
            if (hasActiveRun)
            {
                throw new InvalidOperationException("Monitoring is already running.");
            }

            hasActiveRun = true;
            newRunCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            newTimer = new SessionTimer(clock);
            newSession = new MonitoringSession(Guid.NewGuid(), clock.UtcNow, null, options.ProfileName, TimeSpan.Zero, TimeSpan.Zero, SessionStatus.Running);
            runCancellation = newRunCancellation;
            timer = newTimer;
            session = newSession;
            sequence = 0;
        }

        try
        {
            await store.CreateSessionAsync(newSession, cancellationToken).ConfigureAwait(false);
            newTimer.Start();
            OnSessionChanged(newSession);
            var task = RunAsync(options, newSession, newTimer, newRunCancellation.Token);
            lock (gate)
            {
                runTask = task;
            }

            return newSession;
        }
        catch
        {
            lock (gate)
            {
                hasActiveRun = false;
            }

            throw;
        }
    }

    public void Pause()
    {
        if (session is null || timer is null || session.Status != SessionStatus.Running)
        {
            return;
        }

        timer.Pause();
        session = session with { Status = SessionStatus.Paused, ActiveDuration = timer.ActiveElapsed, PausedDuration = timer.PausedDuration };
        OnActivity("Monitoring paused.");
        OnSessionChanged(session);
    }

    public void Resume()
    {
        if (session is null || timer is null || session.Status != SessionStatus.Paused)
        {
            return;
        }

        timer.Resume();
        session = session with { Status = SessionStatus.Running, ActiveDuration = timer.ActiveElapsed, PausedDuration = timer.PausedDuration };
        OnActivity("Monitoring resumed.");
        OnSessionChanged(session);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        runCancellation?.Cancel();
        if (runTask is not null)
        {
            await runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(MonitoringOptions options, MonitoringSession initialSession, SessionTimer activeTimer, CancellationToken cancellationToken)
    {
        var currentSession = initialSession;
        var schedule = BuildSchedule(options, clock.UtcNow);
        var completedCycles = 0;

        try
        {
            await DetectInterfaceAsync(currentSession.Id, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                currentSession = session ?? currentSession;
                if (currentSession.Status == SessionStatus.Paused)
                {
                    await clock.DelayAsync(options.SchedulerTick, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (options.ActiveDuration is not null && activeTimer.ActiveElapsed >= options.ActiveDuration)
                {
                    currentSession = await CompleteAsync(currentSession, activeTimer, SessionStatus.Completed, cancellationToken).ConfigureAwait(false);
                    return;
                }

                if (options.CycleLimit is not null && completedCycles >= options.CycleLimit)
                {
                    currentSession = await CompleteAsync(currentSession, activeTimer, SessionStatus.Completed, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var due = schedule.DueAt(clock.UtcNow);
                if (due.Count == 0)
                {
                    await clock.DelayAsync(options.SchedulerTick, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                foreach (var operation in due)
                {
                    await RunOperationAsync(currentSession.Id, options, operation, cancellationToken).ConfigureAwait(false);
                    schedule.Advance(operation.Name, clock.UtcNow);
                }

                completedCycles++;
                currentSession = currentSession with { ActiveDuration = activeTimer.ActiveElapsed, PausedDuration = activeTimer.PausedDuration };
                session = currentSession;
                OnSessionChanged(currentSession);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stop is a normal lifecycle path.
        }
        finally
        {
            if (session is not null && session.Status is SessionStatus.Running or SessionStatus.Paused)
            {
                session = session with
                {
                    EndedAt = clock.UtcNow,
                    Status = SessionStatus.Stopped,
                    ActiveDuration = activeTimer.ActiveElapsed,
                    PausedDuration = activeTimer.PausedDuration
                };
                await store.UpdateSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
                OnActivity("Monitoring stopped.");
                OnSessionChanged(session);
            }

            lock (gate)
            {
                hasActiveRun = false;
            }
        }
    }

    private static MonitoringSchedule BuildSchedule(MonitoringOptions options, DateTimeOffset startAt)
    {
        var intervals = options.Intervals;
        return new MonitoringSchedule(
        [
            new ScheduledOperation("icmp", intervals.Icmp, TimeSpan.FromSeconds(2), startAt),
            new ScheduledOperation("tcp", intervals.TcpConnect, TimeSpan.FromSeconds(5), startAt),
            new ScheduledOperation("dns", intervals.Dns, TimeSpan.FromSeconds(5), startAt),
            new ScheduledOperation("https", intervals.Https, TimeSpan.FromSeconds(10), startAt),
            new ScheduledOperation("route", intervals.RouteSnapshot, TimeSpan.FromSeconds(5), startAt),
            new ScheduledOperation("speed-test", intervals.SpeedTest, TimeSpan.FromMinutes(2), startAt)
        ]);
    }

    private async Task RunOperationAsync(Guid sessionId, MonitoringOptions options, ScheduledOperation operation, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(operation.Timeout);

        switch (operation.Name)
        {
            case "icmp":
                foreach (var target in GetIcmpTargets(options))
                {
                    OnActivity($"Pinging {target.Name}.");
                    await SaveMeasurementAsync(await probes.ProbeIcmpAsync(sessionId, target, Interlocked.Increment(ref sequence), operation.Timeout, timeout.Token).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                }

                break;
            case "tcp":
                foreach (var target in options.Targets.TcpTargets)
                {
                    OnActivity($"Testing TCP connection to {target.Name}.");
                    await SaveMeasurementAsync(await probes.ProbeTcpAsync(sessionId, target, operation.Timeout, timeout.Token).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                }

                break;
            case "dns":
                OnActivity($"Resolving {options.Targets.DnsHostname}.");
                await SaveMeasurementAsync(await probes.ProbeDnsAsync(sessionId, options.Targets.DnsHostname, operation.Timeout, timeout.Token).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                break;
            case "https":
                OnActivity($"Testing website connectivity to {options.Targets.HttpsUri.Host}.");
                await SaveMeasurementAsync(await probes.ProbeHttpsAsync(sessionId, options.Targets.HttpsUri, operation.Timeout, timeout.Token).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                break;
            case "route":
                await DetectInterfaceAsync(sessionId, cancellationToken).ConfigureAwait(false);
                break;
            case "speed-test":
                OnActivity("Running download and upload throughput estimate.");
                foreach (var speedTest in await probes.RunSpeedTestAsync(sessionId, options.Targets.DownloadUri, options.Targets.UploadUri, operation.Timeout, timeout.Token).ConfigureAwait(false))
                {
                    await store.SaveSpeedTestAsync(speedTest, cancellationToken).ConfigureAwait(false);
                    SpeedTestRecorded?.Invoke(this, new SpeedTestEventArgs(speedTest));
                }

                break;
        }
    }

    private async Task DetectInterfaceAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        OnActivity("Detecting active network adapter.");
        var snapshot = await environment.GetActiveInterfaceAsync(cancellationToken).ConfigureAwait(false);
        gatewayTarget = string.IsNullOrWhiteSpace(snapshot.Gateway)
            ? null
            : new TargetDefinition("Local Gateway", TargetPurpose.LocalGateway, snapshot.Gateway);
        await store.SaveNetworkInterfaceEventAsync(
            new NetworkInterfaceEvent(sessionId, snapshot.ObservedAt, "snapshot", snapshot.InterfaceName, snapshot.Gateway, snapshot.Description),
            cancellationToken).ConfigureAwait(false);
        OnActivity(snapshot.Gateway is null ? "No default gateway detected." : $"Default gateway detected: {snapshot.Gateway}.");
    }

    private async Task SaveMeasurementAsync(ProbeMeasurement measurement, CancellationToken cancellationToken)
    {
        await store.SaveMeasurementAsync(measurement, cancellationToken).ConfigureAwait(false);
        MeasurementRecorded?.Invoke(this, new MeasurementEventArgs(measurement));
    }

    private async Task<MonitoringSession> CompleteAsync(MonitoringSession currentSession, SessionTimer activeTimer, SessionStatus status, CancellationToken cancellationToken)
    {
        var completed = currentSession with
        {
            EndedAt = clock.UtcNow,
            Status = status,
            ActiveDuration = activeTimer.ActiveElapsed,
            PausedDuration = activeTimer.PausedDuration
        };
        session = completed;
        await store.UpdateSessionAsync(completed, cancellationToken).ConfigureAwait(false);
        OnActivity("Monitoring completed.");
        OnSessionChanged(completed);
        return completed;
    }

    private void OnActivity(string message) => Activity?.Invoke(this, new ActivityEventArgs(message));

    private void OnSessionChanged(MonitoringSession monitoringSession) => SessionChanged?.Invoke(this, new SessionEventArgs(monitoringSession));

    private IReadOnlyList<TargetDefinition> GetIcmpTargets(MonitoringOptions options)
    {
        return gatewayTarget is null
            ? options.Targets.IcmpTargets
            : new[] { gatewayTarget }.Concat(options.Targets.IcmpTargets).ToArray();
    }
}
