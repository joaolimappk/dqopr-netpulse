namespace DQOPR.NetPulse.Core.Models;

public sealed record ProbeMeasurement(
    Guid SessionId,
    DateTimeOffset ObservedAt,
    ProbeMethod Method,
    string TargetName,
    bool Succeeded,
    double? LatencyMilliseconds,
    string? FailureCategory = null,
    string? FailureMessage = null,
    string? TargetHost = null,
    string? AddressFamily = null,
    string? ProbeStreamId = null,
    int? Sequence = null,
    string MethodologyVersion = MeasurementMethodology.CurrentVersion);
