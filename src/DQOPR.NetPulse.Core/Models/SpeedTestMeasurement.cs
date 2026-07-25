namespace DQOPR.NetPulse.Core.Models;

public sealed record SpeedTestMeasurement(
    Guid SessionId,
    DateTimeOffset ObservedAt,
    string Direction,
    bool Succeeded,
    double? MegabitsPerSecond,
    long BytesTransferred,
    TimeSpan ActiveDuration,
    string Provider,
    string? Endpoint,
    string? FailureCategory,
    string? FailureMessage);
