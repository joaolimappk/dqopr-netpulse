namespace DQOPR.NetPulse.Core.Scheduling;

public sealed record ScheduledOperation(
    string Name,
    TimeSpan Interval,
    TimeSpan Timeout,
    DateTimeOffset NextRunAt);
