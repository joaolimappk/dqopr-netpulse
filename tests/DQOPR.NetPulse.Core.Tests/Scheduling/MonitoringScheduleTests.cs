using DQOPR.NetPulse.Core.Configuration;
using DQOPR.NetPulse.Core.Scheduling;

namespace DQOPR.NetPulse.Core.Tests.Scheduling;

public sealed class MonitoringScheduleTests
{
    [Fact]
    public void EvidenceProfileRegistersIndependentSpeedTestSchedule()
    {
        var start = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var schedule = MonitoringSchedule.FromProfile(MonitoringProfile.TenMinuteTest(TimeSpan.FromMinutes(5)), start);

        var speedTest = Assert.Single(schedule.Operations, operation => operation.Name == "speed-test");
        Assert.Equal(TimeSpan.FromMinutes(5), speedTest.Interval);
        Assert.Equal(TimeSpan.FromSeconds(45), speedTest.Timeout);
        Assert.Equal(start, speedTest.NextRunAt);
        Assert.Contains(schedule.Operations, operation => operation.Name == "icmp" && operation.Interval == TimeSpan.FromSeconds(2));
    }
}
