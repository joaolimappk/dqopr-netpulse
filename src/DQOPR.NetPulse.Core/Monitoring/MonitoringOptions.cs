using DQOPR.NetPulse.Core.Configuration;

namespace DQOPR.NetPulse.Core.Monitoring;

public sealed record MonitoringOptions
{
    public string ProfileName { get; init; } = "Manual Monitoring";

    public TimeSpan? ActiveDuration { get; init; } = TimeSpan.FromMinutes(10);

    public int? CycleLimit { get; init; }

    public MonitoringIntervals Intervals { get; init; } = MonitoringIntervals.EvidenceDefaults;

    public MonitoringTargets Targets { get; init; } = MonitoringTargets.Defaults;

    public TimeSpan SchedulerTick { get; init; } = TimeSpan.FromMilliseconds(250);
}
