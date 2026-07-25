using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Core.Storage;

public interface INetPulseStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task CreateSessionAsync(MonitoringSession session, CancellationToken cancellationToken);

    Task UpdateSessionAsync(MonitoringSession session, CancellationToken cancellationToken);

    Task<IReadOnlyList<MonitoringSession>> GetSessionsAsync(CancellationToken cancellationToken);

    Task MarkRunningSessionsInterruptedAsync(DateTimeOffset observedAt, CancellationToken cancellationToken);

    Task SaveMeasurementAsync(ProbeMeasurement measurement, CancellationToken cancellationToken);

    Task SaveSpeedTestAsync(SpeedTestMeasurement measurement, CancellationToken cancellationToken);

    Task SaveNetworkInterfaceEventAsync(NetworkInterfaceEvent networkEvent, CancellationToken cancellationToken);

    Task SaveManualMarkerAsync(ManualMarker marker, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProbeMeasurement>> GetMeasurementsAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SpeedTestMeasurement>> GetSpeedTestsAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<NetworkInterfaceEvent>> GetNetworkInterfaceEventsAsync(Guid sessionId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ManualMarker>> GetManualMarkersAsync(Guid sessionId, CancellationToken cancellationToken);

    Task DeleteSessionAsync(Guid sessionId, CancellationToken cancellationToken);
}
