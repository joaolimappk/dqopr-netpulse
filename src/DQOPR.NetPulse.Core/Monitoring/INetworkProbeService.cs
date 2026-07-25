using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Core.Monitoring;

public interface INetworkProbeService
{
    Task<ProbeMeasurement> ProbeIcmpAsync(Guid sessionId, TargetDefinition target, int sequence, TimeSpan timeout, CancellationToken cancellationToken);

    Task<ProbeMeasurement> ProbeTcpAsync(Guid sessionId, TargetDefinition target, TimeSpan timeout, CancellationToken cancellationToken);

    Task<ProbeMeasurement> ProbeDnsAsync(Guid sessionId, string hostname, TimeSpan timeout, CancellationToken cancellationToken);

    Task<ProbeMeasurement> ProbeHttpsAsync(Guid sessionId, Uri uri, TimeSpan timeout, CancellationToken cancellationToken);

    Task<IReadOnlyList<SpeedTestMeasurement>> RunSpeedTestAsync(Guid sessionId, Uri downloadUri, Uri uploadUri, TimeSpan timeout, CancellationToken cancellationToken);
}
