namespace DQOPR.NetPulse.Core.Time;

public interface IMonitoringClock
{
    DateTimeOffset UtcNow { get; }

    long GetTimestamp();

    TimeSpan GetElapsedTime(long startingTimestamp);

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
