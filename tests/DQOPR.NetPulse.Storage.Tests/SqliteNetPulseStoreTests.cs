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
            await store.SaveMeasurementAsync(new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow, ProbeMethod.Icmp, "gateway", true, 5), CancellationToken.None);
            await store.SaveSpeedTestAsync(new SpeedTestMeasurement(session.Id, DateTimeOffset.UtcNow, "download", true, 100, 1_000_000, TimeSpan.FromSeconds(1), "test", "https://example.test", null, null), CancellationToken.None);
            await store.SaveNetworkInterfaceEventAsync(new NetworkInterfaceEvent(session.Id, DateTimeOffset.UtcNow, "snapshot", "Ethernet", "192.168.1.1", "test"), CancellationToken.None);
            await store.SaveManualMarkerAsync(new ManualMarker(Guid.NewGuid(), session.Id, DateTimeOffset.UtcNow, "Internet felt bad."), CancellationToken.None);
            await store.UpdateSessionAsync(session with { Status = SessionStatus.Stopped, EndedAt = DateTimeOffset.UtcNow }, CancellationToken.None);

            var sessions = await store.GetSessionsAsync(CancellationToken.None);
            var measurements = await store.GetMeasurementsAsync(session.Id, CancellationToken.None);
            var speeds = await store.GetSpeedTestsAsync(session.Id, CancellationToken.None);
            var events = await store.GetNetworkInterfaceEventsAsync(session.Id, CancellationToken.None);
            var markers = await store.GetManualMarkersAsync(session.Id, CancellationToken.None);

            Assert.Equal(SessionStatus.Stopped, Assert.Single(sessions).Status);
            Assert.Equal(ProbeMethod.Icmp, Assert.Single(measurements).Method);
            Assert.Equal("download", Assert.Single(speeds).Direction);
            Assert.Equal("Ethernet", Assert.Single(events).InterfaceName);
            Assert.Equal("Internet felt bad.", Assert.Single(markers).Note);

            await store.DeleteSessionAsync(session.Id, CancellationToken.None);

            Assert.Empty(await store.GetSessionsAsync(CancellationToken.None));
            Assert.Empty(await store.GetMeasurementsAsync(session.Id, CancellationToken.None));
            Assert.Empty(await store.GetSpeedTestsAsync(session.Id, CancellationToken.None));
            Assert.Empty(await store.GetNetworkInterfaceEventsAsync(session.Id, CancellationToken.None));
            Assert.Empty(await store.GetManualMarkersAsync(session.Id, CancellationToken.None));
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
