using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Core.Monitoring;

public sealed record QuickTestResult(
    MonitoringSession Session,
    IReadOnlyList<ProbeMeasurement> Measurements,
    IReadOnlyList<SpeedTestMeasurement> SpeedTests,
    string Summary);
