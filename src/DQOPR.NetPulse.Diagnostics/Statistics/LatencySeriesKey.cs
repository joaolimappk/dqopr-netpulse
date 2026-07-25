using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Diagnostics.Statistics;

public sealed record LatencySeriesKey(
    Guid SessionId,
    ProbeMethod Method,
    string TargetName,
    string? TargetHost,
    string? AddressFamily,
    string? ProbeStreamId)
{
    public static LatencySeriesKey From(ProbeMeasurement measurement) => new(
        measurement.SessionId,
        measurement.Method,
        measurement.TargetName,
        measurement.TargetHost,
        measurement.AddressFamily,
        measurement.ProbeStreamId);
}
