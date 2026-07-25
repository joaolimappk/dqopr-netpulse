using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Diagnostics.Statistics;

public sealed record TargetPacketLoss(
    string TargetName,
    int Sent,
    int Received,
    int Lost,
    double LossPercent);

public static class PacketLossSummary
{
    public static IReadOnlyList<TargetPacketLoss> ByIcmpTarget(IEnumerable<ProbeMeasurement> measurements)
    {
        return measurements
            .Where(measurement => measurement.Method == ProbeMethod.Icmp)
            .GroupBy(measurement => measurement.TargetName)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var sent = group.Count();
                var received = group.Count(measurement => measurement.Succeeded);
                var lost = sent - received;
                return new TargetPacketLoss(group.Key, sent, received, lost, sent == 0 ? double.NaN : lost * 100.0 / sent);
            })
            .ToArray();
    }
}
