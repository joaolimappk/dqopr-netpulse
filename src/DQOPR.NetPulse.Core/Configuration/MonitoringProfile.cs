namespace DQOPR.NetPulse.Core.Configuration;

public sealed record MonitoringProfile(
    string Name,
    TimeSpan? ActiveDuration,
    MonitoringIntervals Intervals,
    bool GenerateReportAtCompletion)
{
    public static MonitoringProfile TenMinuteTest(TimeSpan speedTestInterval) => new(
        Name: "10-Minute Test",
        ActiveDuration: TimeSpan.FromMinutes(10),
        Intervals: MonitoringIntervals.EvidenceDefaults with { SpeedTest = speedTestInterval },
        GenerateReportAtCompletion: false);

    public static MonitoringProfile OneHourEvidenceTest { get; } = new(
        Name: "1-Hour ISP Evidence Test",
        ActiveDuration: TimeSpan.FromHours(1),
        Intervals: MonitoringIntervals.EvidenceDefaults,
        GenerateReportAtCompletion: true);
}
