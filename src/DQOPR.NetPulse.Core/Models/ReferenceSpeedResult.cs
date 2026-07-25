namespace DQOPR.NetPulse.Core.Models;

public sealed record ReferenceSpeedResult(
    Guid Id,
    Guid? SessionId,
    DateTimeOffset ObservedAt,
    string Provider,
    double? DownloadMegabitsPerSecond,
    double? UploadMegabitsPerSecond,
    double? LatencyMilliseconds,
    string? Notes);
