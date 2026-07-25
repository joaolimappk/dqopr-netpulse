using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Core.QuickTest;
using DQOPR.NetPulse.Core.Storage;
using DQOPR.NetPulse.Core.Time;

namespace DQOPR.NetPulse.Core.Monitoring;

public sealed class QuickTestRunner(
    INetworkProbeService probes,
    INetworkEnvironmentService environment,
    INetPulseStore store,
    IMonitoringClock clock)
{
    public event EventHandler<ActivityEventArgs>? Activity;

    public event EventHandler<MeasurementEventArgs>? MeasurementRecorded;

    public async Task<QuickTestResult> RunAsync(QuickTestOptions options, MonitoringTargets targets, CancellationToken cancellationToken)
    {
        options.Validate();
        await store.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var session = new MonitoringSession(Guid.NewGuid(), clock.UtcNow, null, "Quick Test", TimeSpan.Zero, TimeSpan.Zero, SessionStatus.Running);
        await store.CreateSessionAsync(session, cancellationToken).ConfigureAwait(false);

        var measurements = new List<ProbeMeasurement>();
        var speeds = new List<SpeedTestMeasurement>();
        var started = clock.GetTimestamp();

        try
        {
            OnActivity("Detecting connection.");
            var snapshot = await environment.GetActiveInterfaceAsync(cancellationToken).ConfigureAwait(false);
            await store.SaveNetworkInterfaceEventAsync(new NetworkInterfaceEvent(session.Id, snapshot.ObservedAt, "quick-test-snapshot", snapshot.InterfaceName, snapshot.Gateway, snapshot.Description), cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(snapshot.Gateway))
            {
                var gatewayTarget = new TargetDefinition("Local Gateway", TargetPurpose.LocalGateway, snapshot.Gateway);
                await RunIcmpBurstAsync(session.Id, gatewayTarget, options, measurements, cancellationToken).ConfigureAwait(false);
            }

            foreach (var target in targets.IcmpTargets)
            {
                await RunIcmpBurstAsync(session.Id, target, options, measurements, cancellationToken).ConfigureAwait(false);
            }

            foreach (var target in targets.TcpTargets)
            {
                OnActivity($"Testing TCP connection to {target.Name}.");
                await AddMeasurementAsync(await probes.ProbeTcpAsync(session.Id, target, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false), measurements, cancellationToken).ConfigureAwait(false);
            }

            OnActivity($"Testing DNS resolution for {targets.DnsHostname}.");
            await AddMeasurementAsync(await probes.ProbeDnsAsync(session.Id, targets.DnsHostname, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false), measurements, cancellationToken).ConfigureAwait(false);

            OnActivity($"Testing website connectivity to {targets.HttpsUri.Host}.");
            await AddMeasurementAsync(await probes.ProbeHttpsAsync(session.Id, targets.HttpsUri, TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false), measurements, cancellationToken).ConfigureAwait(false);

            if (options.IncludeDownloadEstimate || options.IncludeUploadEstimate)
            {
                OnActivity("Measuring download and upload throughput.");
                foreach (var speed in await probes.RunSpeedTestAsync(session.Id, targets.DownloadUri, targets.UploadUri, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false))
                {
                    await store.SaveSpeedTestAsync(speed, cancellationToken).ConfigureAwait(false);
                    speeds.Add(speed);
                }
            }

            session = session with
            {
                EndedAt = clock.UtcNow,
                Status = SessionStatus.Completed,
                ActiveDuration = clock.GetElapsedTime(started)
            };
            await store.UpdateSessionAsync(session, cancellationToken).ConfigureAwait(false);
            OnActivity("Quick Test completed.");

            return new QuickTestResult(session, measurements, speeds, "Quick Test completed. A Quick Test is a snapshot and may miss intermittent problems.");
        }
        catch
        {
            session = session with { EndedAt = clock.UtcNow, Status = SessionStatus.Interrupted, ActiveDuration = clock.GetElapsedTime(started) };
            await store.UpdateSessionAsync(session, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task RunIcmpBurstAsync(Guid sessionId, TargetDefinition target, QuickTestOptions options, ICollection<ProbeMeasurement> measurements, CancellationToken cancellationToken)
    {
        OnActivity($"Pinging {target.Name}.");
        for (var index = 0; index < options.ProbeBurstCount; index++)
        {
            var measurement = await probes.ProbeIcmpAsync(sessionId, target, index + 1, options.ProbeTimeout, cancellationToken).ConfigureAwait(false);
            await AddMeasurementAsync(measurement, measurements, cancellationToken).ConfigureAwait(false);
            await clock.DelayAsync(options.ProbeSpacing, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task AddMeasurementAsync(ProbeMeasurement measurement, ICollection<ProbeMeasurement> measurements, CancellationToken cancellationToken)
    {
        await store.SaveMeasurementAsync(measurement, cancellationToken).ConfigureAwait(false);
        measurements.Add(measurement);
        MeasurementRecorded?.Invoke(this, new MeasurementEventArgs(measurement));
    }

    private void OnActivity(string message) => Activity?.Invoke(this, new ActivityEventArgs(message));
}
