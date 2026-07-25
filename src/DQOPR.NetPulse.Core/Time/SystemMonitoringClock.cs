using System.Diagnostics;

namespace DQOPR.NetPulse.Core.Time;

public sealed class SystemMonitoringClock : IMonitoringClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public long GetTimestamp() => Stopwatch.GetTimestamp();

    public TimeSpan GetElapsedTime(long startingTimestamp) => Stopwatch.GetElapsedTime(startingTimestamp);

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);
}
