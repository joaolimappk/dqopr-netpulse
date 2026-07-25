using DQOPR.NetPulse.Core.Configuration;

namespace DQOPR.NetPulse.Core.Scheduling;

public sealed class MonitoringSchedule
{
    private readonly Dictionary<string, ScheduledOperation> operations;

    public MonitoringSchedule(IEnumerable<ScheduledOperation> operations)
    {
        this.operations = operations.ToDictionary(operation => operation.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<ScheduledOperation> Operations => operations.Values;

    public IReadOnlyCollection<ScheduledOperation> DueAt(DateTimeOffset now)
        => operations.Values
            .Where(operation => operation.NextRunAt <= now)
            .OrderBy(operation => operation.NextRunAt)
            .ThenBy(operation => operation.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public ScheduledOperation Advance(string operationName, DateTimeOffset completedAt)
    {
        if (!operations.TryGetValue(operationName, out var operation))
        {
            throw new KeyNotFoundException($"Scheduled operation '{operationName}' is not registered.");
        }

        var advanced = operation with
        {
            NextRunAt = ScheduleCalculator.NextRunAfter(operation.NextRunAt, completedAt, operation.Interval)
        };

        operations[operationName] = advanced;
        return advanced;
    }

    public static MonitoringSchedule FromProfile(MonitoringProfile profile, DateTimeOffset startAt)
    {
        var intervals = profile.Intervals;

        return new MonitoringSchedule(
        [
            new ScheduledOperation("icmp", intervals.Icmp, TimeSpan.FromSeconds(2), startAt),
            new ScheduledOperation("tcp", intervals.TcpConnect, TimeSpan.FromSeconds(5), startAt),
            new ScheduledOperation("dns", intervals.Dns, TimeSpan.FromSeconds(5), startAt),
            new ScheduledOperation("https", intervals.Https, TimeSpan.FromSeconds(10), startAt),
            new ScheduledOperation("interface", intervals.InterfaceSnapshot, TimeSpan.FromSeconds(5), startAt),
            new ScheduledOperation("route", intervals.RouteSnapshot, TimeSpan.FromSeconds(20), startAt),
            new ScheduledOperation("public-ip", intervals.PublicIp, TimeSpan.FromSeconds(10), startAt),
            new ScheduledOperation("speed-test", intervals.SpeedTest, TimeSpan.Zero, startAt)
        ]);
    }
}
