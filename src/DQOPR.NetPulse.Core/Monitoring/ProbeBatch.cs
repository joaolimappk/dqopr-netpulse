using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Core.Monitoring;

public sealed record ProbeBatch(
    IReadOnlyList<ProbeMeasurement> Measurements,
    IReadOnlyList<SpeedTestMeasurement> SpeedTests)
{
    public static ProbeBatch Empty { get; } = new([], []);
}
