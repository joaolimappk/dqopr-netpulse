namespace DQOPR.NetPulse.Core.Scheduling;

public static class ScheduleCalculator
{
    public static DateTimeOffset NextRunAfter(DateTimeOffset previousDueAt, DateTimeOffset completedAt, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");
        }

        var next = previousDueAt + interval;
        while (next <= completedAt)
        {
            next += interval;
        }

        return next;
    }

    public static IReadOnlyList<TimeSpan> RunOffsetsWithin(TimeSpan activeDuration, TimeSpan interval)
    {
        if (activeDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(activeDuration), "Active duration must be positive.");
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be positive.");
        }

        var offsets = new List<TimeSpan>();
        for (var offset = TimeSpan.Zero; offset < activeDuration; offset += interval)
        {
            offsets.Add(offset);
        }

        return offsets;
    }
}
