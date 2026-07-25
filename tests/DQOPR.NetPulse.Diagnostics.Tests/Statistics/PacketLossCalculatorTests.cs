using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Diagnostics.Statistics;

namespace DQOPR.NetPulse.Diagnostics.Tests.Statistics;

public sealed class PacketLossCalculatorTests
{
    [Fact]
    public void PacketLossUsesIcmpOnly()
    {
        var sessionId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var measurements = new[]
        {
            new ProbeMeasurement(sessionId, timestamp, ProbeMethod.Icmp, "gateway", true, 10),
            new ProbeMeasurement(sessionId, timestamp.AddSeconds(1), ProbeMethod.Icmp, "gateway", false, null, "timeout"),
            new ProbeMeasurement(sessionId, timestamp.AddSeconds(2), ProbeMethod.Dns, "configured resolver", false, null, "nxdomain"),
            new ProbeMeasurement(sessionId, timestamp.AddSeconds(3), ProbeMethod.Https, "example", false, null, "tls")
        };

        var loss = PacketLossCalculator.IcmpPacketLossPercent(measurements, "gateway");

        Assert.Equal(50, loss);
    }

    [Fact]
    public void DnsFailureRateIsSeparateFromPacketLoss()
    {
        var sessionId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var measurements = new[]
        {
            new ProbeMeasurement(sessionId, timestamp, ProbeMethod.Icmp, "gateway", true, 10),
            new ProbeMeasurement(sessionId, timestamp.AddSeconds(1), ProbeMethod.Dns, "configured resolver", false, null, "timeout"),
            new ProbeMeasurement(sessionId, timestamp.AddSeconds(2), ProbeMethod.Dns, "configured resolver", true, 30)
        };

        Assert.Equal(0, PacketLossCalculator.IcmpPacketLossPercent(measurements));
        Assert.Equal(50, PacketLossCalculator.FailureRatePercent(measurements, ProbeMethod.Dns));
    }
}
