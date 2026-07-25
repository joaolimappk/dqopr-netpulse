using DQOPR.NetPulse.Core.Scheduling;

namespace DQOPR.NetPulse.Core.Tests.Scheduling;

public sealed class ScheduleCalculatorTests
{
    [Fact]
    public void TenMinuteSessionWithFiveMinuteSpeedIntervalRunsTwoSpeedTests()
    {
        var offsets = ScheduleCalculator.RunOffsetsWithin(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5));

        Assert.Equal([TimeSpan.Zero, TimeSpan.FromMinutes(5)], offsets);
    }

    [Fact]
    public void NextRunSkipsMissedIntervalsWithoutBlockingOtherSchedules()
    {
        var firstDue = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var completedLate = firstDue + TimeSpan.FromSeconds(13);

        var next = ScheduleCalculator.NextRunAfter(firstDue, completedLate, TimeSpan.FromSeconds(5));

        Assert.Equal(firstDue + TimeSpan.FromSeconds(15), next);
    }
}
