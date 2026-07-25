using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Storage.Repositories;
using Microsoft.Data.Sqlite;

namespace DQOPR.NetPulse.Storage.Tests;

public sealed class SqliteNetPulseStoreTests
{
    [Fact]
    public async Task SavesAndRetrievesSessionMeasurementsAndSpeedTests()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netpulse-store-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

        try
        {
            var store = new SqliteNetPulseStore(connectionString);
            await store.InitializeAsync(CancellationToken.None);
            var session = new MonitoringSession(Guid.NewGuid(), DateTimeOffset.UtcNow, null, "test", TimeSpan.Zero, TimeSpan.Zero, SessionStatus.Running);
            await store.CreateSessionAsync(session, CancellationToken.None);
            await store.SaveMeasurementAsync(new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow, ProbeMethod.Icmp, "gateway", true, 5, TargetHost: "192.168.1.1", AddressFamily: "IPv4", ProbeStreamId: "stream-1", Sequence: 1), CancellationToken.None);
            await store.SaveSpeedTestAsync(new SpeedTestMeasurement(session.Id, DateTimeOffset.UtcNow, "download", true, 100, 100_000_000, TimeSpan.FromSeconds(8), "test", "https://example.test", null, null, SpeedResultStatus.Valid, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1), 4, "2.0", MeasurementMethodology.CurrentVersion, "{\"streams\":[]}"), CancellationToken.None);
            await store.SaveNetworkInterfaceEventAsync(new NetworkInterfaceEvent(session.Id, DateTimeOffset.UtcNow, "snapshot", "Ethernet", "192.168.1.1", "test"), CancellationToken.None);
            await store.SaveManualMarkerAsync(new ManualMarker(Guid.NewGuid(), session.Id, DateTimeOffset.UtcNow, "Internet felt bad."), CancellationToken.None);
            await store.SaveReferenceSpeedResultAsync(new ReferenceSpeedResult(Guid.NewGuid(), session.Id, DateTimeOffset.UtcNow, "reference", 200, 20, 12, "manual validation"), CancellationToken.None);
            await store.UpdateSessionAsync(session with { Status = SessionStatus.Stopped, EndedAt = DateTimeOffset.UtcNow }, CancellationToken.None);

            var sessions = await store.GetSessionsAsync(CancellationToken.None);
            var measurements = await store.GetMeasurementsAsync(session.Id, CancellationToken.None);
            var speeds = await store.GetSpeedTestsAsync(session.Id, CancellationToken.None);
            var events = await store.GetNetworkInterfaceEventsAsync(session.Id, CancellationToken.None);
            var markers = await store.GetManualMarkersAsync(session.Id, CancellationToken.None);
            var references = await store.GetReferenceSpeedResultsAsync(session.Id, CancellationToken.None);

            Assert.Equal(SessionStatus.Stopped, Assert.Single(sessions).Status);
            var measurement = Assert.Single(measurements);
            Assert.Equal(ProbeMethod.Icmp, measurement.Method);
            Assert.Equal("192.168.1.1", measurement.TargetHost);
            Assert.Equal("IPv4", measurement.AddressFamily);
            Assert.Equal("stream-1", measurement.ProbeStreamId);
            Assert.Equal(1, measurement.Sequence);
            Assert.Equal(MeasurementMethodology.CurrentVersion, measurement.MethodologyVersion);
            var speed = Assert.Single(speeds);
            Assert.Equal("download", speed.Direction);
            Assert.Equal(SpeedResultStatus.Valid, speed.ResultStatus);
            Assert.Equal(4, speed.ParallelStreamCount);
            Assert.Equal("2.0", speed.HttpVersion);
            Assert.Equal(MeasurementMethodology.CurrentVersion, speed.MethodologyVersion);
            Assert.NotNull(speed.DiagnosticJson);
            Assert.Equal("Ethernet", Assert.Single(events).InterfaceName);
            Assert.Equal("Internet felt bad.", Assert.Single(markers).Note);
            Assert.Equal("reference", Assert.Single(references).Provider);

            await store.DeleteSessionAsync(session.Id, CancellationToken.None);

            Assert.Empty(await store.GetSessionsAsync(CancellationToken.None));
            Assert.Empty(await store.GetMeasurementsAsync(session.Id, CancellationToken.None));
            Assert.Empty(await store.GetSpeedTestsAsync(session.Id, CancellationToken.None));
            Assert.Empty(await store.GetNetworkInterfaceEventsAsync(session.Id, CancellationToken.None));
            Assert.Empty(await store.GetManualMarkersAsync(session.Id, CancellationToken.None));
            Assert.Empty(await store.GetReferenceSpeedResultsAsync(session.Id, CancellationToken.None));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var candidate in new[] { path, $"{path}-wal", $"{path}-shm" })
            {
                if (File.Exists(candidate))
                {
                    File.Delete(candidate);
                }
            }
        }
    }
}
