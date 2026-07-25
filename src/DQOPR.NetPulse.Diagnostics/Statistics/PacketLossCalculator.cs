using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Diagnostics.Statistics;

public static class PacketLossCalculator
{
    public static double IcmpPacketLossPercent(IEnumerable<ProbeMeasurement> measurements, string? targetName = null)
    {
        var icmp = measurements
            .Where(measurement => measurement.Method == ProbeMethod.Icmp)
            .Where(measurement => targetName is null || string.Equals(measurement.TargetName, targetName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (icmp.Length == 0)
        {
            return double.NaN;
        }

        var failed = icmp.Count(measurement => !measurement.Succeeded);
        return failed * 100.0 / icmp.Length;
    }

    public static double FailureRatePercent(IEnumerable<ProbeMeasurement> measurements, ProbeMethod method)
    {
        if (method == ProbeMethod.Icmp)
        {
            throw new ArgumentException("Use IcmpPacketLossPercent for packet-oriented ICMP loss.", nameof(method));
        }

        var selected = measurements.Where(measurement => measurement.Method == method).ToArray();
        if (selected.Length == 0)
        {
            return double.NaN;
        }

        var failed = selected.Count(measurement => !measurement.Succeeded);
        return failed * 100.0 / selected.Length;
    }
}
