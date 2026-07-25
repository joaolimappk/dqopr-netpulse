namespace DQOPR.NetPulse.Core.Models;

public sealed record ProbeMeasurement(
    Guid SessionId,
    DateTimeOffset ObservedAt,
    ProbeMethod Method,
    string TargetName,
    bool Succeeded,
    double? LatencyMilliseconds,
    string? FailureCategory = null,
    string? FailureMessage = null);
