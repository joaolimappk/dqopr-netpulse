namespace DQOPR.NetPulse.Core.Models;

public sealed record MonitoringSession(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string ProfileName,
    TimeSpan ActiveDuration,
    TimeSpan PausedDuration,
    SessionStatus Status,
    string MethodologyVersion = MeasurementMethodology.CurrentVersion);

public enum SessionStatus
{
    Created,
    Running,
    Paused,
    Completed,
    Stopped,
    Interrupted
}
