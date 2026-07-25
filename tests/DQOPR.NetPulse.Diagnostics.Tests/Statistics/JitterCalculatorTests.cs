using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Diagnostics.Statistics;

namespace DQOPR.NetPulse.Diagnostics.Tests.Statistics;

public sealed class JitterCalculatorTests
{
    [Fact]
    public void JitterIsCalculatedPerTargetAndMethod()
    {
        var sessionId = Guid.NewGuid();
        var start = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        ProbeMeasurement Measurement(int seconds, ProbeMethod method, string target, double latency)
            => new(sessionId, start.AddSeconds(seconds), method, target, true, latency);

        var measurements = new[]
        {
            Measurement(0, ProbeMethod.Icmp, "gateway", 10),
            Measurement(1, ProbeMethod.Icmp, "gateway", 20),
            Measurement(2, ProbeMethod.Icmp, "gateway", 17),
            Measurement(0, ProbeMethod.Icmp, "quad9", 50),
            Measurement(1, ProbeMethod.Icmp, "quad9", 70),
            Measurement(0, ProbeMethod.Https, "quad9", 100),
            Measurement(1, ProbeMethod.Https, "quad9", 140)
        };

        var jitter = JitterCalculator.MeanAbsoluteDifferenceBySeries(measurements);

        Assert.Equal(6.5, jitter[new LatencySeriesKey("gateway", ProbeMethod.Icmp)]);
        Assert.Equal(20, jitter[new LatencySeriesKey("quad9", ProbeMethod.Icmp)]);
        Assert.Equal(40, jitter[new LatencySeriesKey("quad9", ProbeMethod.Https)]);
    }

    [Fact]
    public void OneSampleSeriesReturnsNanInsteadOfBorrowingAnotherTarget()
    {
        var sessionId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var measurements = new[]
        {
            new ProbeMeasurement(sessionId, timestamp, ProbeMethod.Icmp, "gateway", true, 10),
            new ProbeMeasurement(sessionId, timestamp, ProbeMethod.Icmp, "cloudflare", true, 40),
            new ProbeMeasurement(sessionId, timestamp.AddSeconds(1), ProbeMethod.Icmp, "cloudflare", true, 42)
        };

        var jitter = JitterCalculator.MeanAbsoluteDifferenceBySeries(measurements);

        Assert.True(double.IsNaN(jitter[new LatencySeriesKey("gateway", ProbeMethod.Icmp)]));
    }
}
