using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Core.Monitoring;
using DQOPR.NetPulse.Core.Storage;
using DQOPR.NetPulse.Core.Time;

namespace DQOPR.NetPulse.Core.Tests.Monitoring;

internal sealed class FakeClock : IMonitoringClock
{
    private long timestamp;

    public DateTimeOffset UtcNow { get; private set; } = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    public bool BlockDelays { get; set; }

    public long GetTimestamp() => timestamp;

    public TimeSpan GetElapsedTime(long startingTimestamp) => TimeSpan.FromMilliseconds(timestamp - startingTimestamp);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Advance(delay);
        return BlockDelays ? Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken) : Task.CompletedTask;
    }

    public void Advance(TimeSpan elapsed)
    {
        timestamp += (long)elapsed.TotalMilliseconds;
        UtcNow += elapsed;
    }
}

internal sealed class FakeStore : INetPulseStore
{
    public List<MonitoringSession> Sessions { get; } = [];

    public List<ProbeMeasurement> Measurements { get; } = [];

    public List<SpeedTestMeasurement> SpeedTests { get; } = [];

    public List<NetworkInterfaceEvent> NetworkInterfaceEvents { get; } = [];

    public List<ManualMarker> ManualMarkers { get; } = [];

    public List<ReferenceSpeedResult> ReferenceSpeedResults { get; } = [];

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CreateSessionAsync(MonitoringSession session, CancellationToken cancellationToken)
    {
        Sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task UpdateSessionAsync(MonitoringSession session, CancellationToken cancellationToken)
    {
        Sessions.RemoveAll(existing => existing.Id == session.Id);
        Sessions.Add(session);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MonitoringSession>> GetSessionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MonitoringSession>>(Sessions);

    public Task MarkRunningSessionsInterruptedAsync(DateTimeOffset observedAt, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SaveMeasurementAsync(ProbeMeasurement measurement, CancellationToken cancellationToken)
    {
        Measurements.Add(measurement);
        return Task.CompletedTask;
    }

    public Task SaveSpeedTestAsync(SpeedTestMeasurement measurement, CancellationToken cancellationToken)
    {
        SpeedTests.Add(measurement);
        return Task.CompletedTask;
    }

    public Task SaveNetworkInterfaceEventAsync(NetworkInterfaceEvent networkEvent, CancellationToken cancellationToken)
    {
        NetworkInterfaceEvents.Add(networkEvent);
        return Task.CompletedTask;
    }

    public Task SaveManualMarkerAsync(ManualMarker marker, CancellationToken cancellationToken)
    {
        ManualMarkers.Add(marker);
        return Task.CompletedTask;
    }

    public Task SaveReferenceSpeedResultAsync(ReferenceSpeedResult result, CancellationToken cancellationToken)
    {
        ReferenceSpeedResults.Add(result);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProbeMeasurement>> GetMeasurementsAsync(Guid sessionId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ProbeMeasurement>>(Measurements.Where(measurement => measurement.SessionId == sessionId).ToArray());

    public Task<IReadOnlyList<SpeedTestMeasurement>> GetSpeedTestsAsync(Guid sessionId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SpeedTestMeasurement>>(SpeedTests.Where(speed => speed.SessionId == sessionId).ToArray());

    public Task<IReadOnlyList<NetworkInterfaceEvent>> GetNetworkInterfaceEventsAsync(Guid sessionId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<NetworkInterfaceEvent>>(NetworkInterfaceEvents.Where(networkEvent => networkEvent.SessionId == sessionId).ToArray());

    public Task<IReadOnlyList<ManualMarker>> GetManualMarkersAsync(Guid sessionId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ManualMarker>>(ManualMarkers.Where(marker => marker.SessionId == sessionId).ToArray());

    public Task<IReadOnlyList<ReferenceSpeedResult>> GetReferenceSpeedResultsAsync(Guid? sessionId, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<ReferenceSpeedResult>>(ReferenceSpeedResults.Where(result => sessionId is null || result.SessionId == sessionId).ToArray());

    public Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        Sessions.RemoveAll(session => session.Id == sessionId);
        Measurements.RemoveAll(measurement => measurement.SessionId == sessionId);
        SpeedTests.RemoveAll(speed => speed.SessionId == sessionId);
        NetworkInterfaceEvents.RemoveAll(networkEvent => networkEvent.SessionId == sessionId);
        ManualMarkers.RemoveAll(marker => marker.SessionId == sessionId);
        ReferenceSpeedResults.RemoveAll(result => result.SessionId == sessionId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeProbeService : INetworkProbeService
{
    public int IcmpProbeCount { get; private set; }

    public Func<Guid, Uri, Uri, IReadOnlyList<SpeedTestMeasurement>>? SpeedTestFactory { get; set; }

    public Task<ProbeMeasurement> ProbeIcmpAsync(Guid sessionId, TargetDefinition target, int sequence, TimeSpan timeout, CancellationToken cancellationToken)
    {
        IcmpProbeCount++;
        return Task.FromResult(new ProbeMeasurement(
            sessionId,
            DateTimeOffset.UtcNow,
            ProbeMethod.Icmp,
            target.Name,
            true,
            10 + sequence,
            TargetHost: target.Host,
            AddressFamily: target.Host.Contains(':', StringComparison.Ordinal) ? "IPv6" : "IPv4",
            ProbeStreamId: $"{sessionId}:icmp:{target.Name}:{target.Host}",
            Sequence: sequence));
    }

    public Task<ProbeMeasurement> ProbeTcpAsync(Guid sessionId, TargetDefinition target, TimeSpan timeout, CancellationToken cancellationToken)
        => Task.FromResult(new ProbeMeasurement(sessionId, DateTimeOffset.UtcNow, ProbeMethod.TcpConnect, target.Name, true, 15));

    public Task<ProbeMeasurement> ProbeDnsAsync(Guid sessionId, string hostname, TimeSpan timeout, CancellationToken cancellationToken)
        => Task.FromResult(new ProbeMeasurement(sessionId, DateTimeOffset.UtcNow, ProbeMethod.Dns, hostname, true, 20));

    public Task<ProbeMeasurement> ProbeHttpsAsync(Guid sessionId, Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
        => Task.FromResult(new ProbeMeasurement(sessionId, DateTimeOffset.UtcNow, ProbeMethod.Https, uri.Host, true, 25));

    public Task<IReadOnlyList<SpeedTestMeasurement>> RunSpeedTestAsync(Guid sessionId, Uri downloadUri, Uri uploadUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        IReadOnlyList<SpeedTestMeasurement> results = SpeedTestFactory?.Invoke(sessionId, downloadUri, uploadUri) ??
        [
            new SpeedTestMeasurement(sessionId, DateTimeOffset.UtcNow, "download", true, 50, 1_000_000, TimeSpan.FromSeconds(8), "fake", downloadUri.ToString(), null, null),
            new SpeedTestMeasurement(sessionId, DateTimeOffset.UtcNow, "upload", true, 10, 250_000, TimeSpan.FromSeconds(8), "fake", uploadUri.ToString(), null, null)
        ];
        return Task.FromResult(results);
    }
}

internal sealed class FakeEnvironment : INetworkEnvironmentService
{
    public Task<NetworkInterfaceSnapshot> GetActiveInterfaceAsync(CancellationToken cancellationToken)
        => Task.FromResult(new NetworkInterfaceSnapshot(DateTimeOffset.UtcNow, "Ethernet", "Fake adapter", "192.168.1.1", "192.168.1.10", true, "Ethernet"));
}
