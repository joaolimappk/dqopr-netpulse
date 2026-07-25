using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Core.Monitoring;

public sealed class MeasurementEventArgs(ProbeMeasurement measurement) : EventArgs
{
    public ProbeMeasurement Measurement { get; } = measurement;
}

public sealed class SpeedTestEventArgs(SpeedTestMeasurement speedTest) : EventArgs
{
    public SpeedTestMeasurement SpeedTest { get; } = speedTest;
}

public sealed class ActivityEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}

public sealed class SessionEventArgs(MonitoringSession session) : EventArgs
{
    public MonitoringSession Session { get; } = session;
}
