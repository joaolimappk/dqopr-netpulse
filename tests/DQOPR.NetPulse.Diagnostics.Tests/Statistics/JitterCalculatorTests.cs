using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Diagnostics.Statistics;

namespace DQOPR.NetPulse.Diagnostics.Tests.Statistics;

public sealed class JitterCalculatorTests
{
    [Fact]
    public void JitterIsCalculatedOnlyWithinTheSameIcmpProbeStream()
    {
        var sessionId = Guid.NewGuid();
        var measurements = new[]
        {
            Icmp(sessionId, 0, "gateway", "192.168.1.1", "IPv4", "gateway", true, 10),
            Icmp(sessionId, 1, "gateway", "192.168.1.1", "IPv4", "gateway", true, 20),
            Icmp(sessionId, 2, "gateway", "192.168.1.1", "IPv4", "gateway", true, 17),
            Icmp(sessionId, 0, "quad9", "9.9.9.9", "IPv4", "quad9-v4", true, 50),
            Icmp(sessionId, 1, "quad9", "9.9.9.9", "IPv4", "quad9-v4", true, 70),
            Icmp(sessionId, 2, "quad9", "9.9.9.9", "IPv4", "quad9-v4", true, 80),
            Icmp(sessionId, 0, "quad9", "2620:fe::fe", "IPv6", "quad9-v6", true, 100),
            Icmp(sessionId, 1, "quad9", "2620:fe::fe", "IPv6", "quad9-v6", true, 140),
            Icmp(sessionId, 2, "quad9", "2620:fe::fe", "IPv6", "quad9-v6", true, 170),
            new ProbeMeasurement(sessionId, At(0), ProbeMethod.Https, "quad9", true, 100, TargetHost: "dns.quad9.net", AddressFamily: "hostname", ProbeStreamId: "https")
        };

        var jitter = JitterCalculator.MeanAbsoluteDifferenceBySeries(measurements);

        Assert.Equal(6.5, jitter[Key(sessionId, "gateway", "192.168.1.1", "IPv4", "gateway")]);
        Assert.Equal(15, jitter[Key(sessionId, "quad9", "9.9.9.9", "IPv4", "quad9-v4")]);
        Assert.Equal(35, jitter[Key(sessionId, "quad9", "2620:fe::fe", "IPv6", "quad9-v6")]);
        Assert.Equal(3, jitter.Count);
    }

    [Fact]
    public void JitterDoesNotBorrowFromDifferentSessions()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var measurements = new[]
        {
            Icmp(first, 0, "cloudflare", "1.1.1.1", "IPv4", "same-target", true, 10),
            Icmp(first, 1, "cloudflare", "1.1.1.1", "IPv4", "same-target", true, 20),
            Icmp(second, 0, "cloudflare", "1.1.1.1", "IPv4", "same-target", true, 100),
            Icmp(second, 1, "cloudflare", "1.1.1.1", "IPv4", "same-target", true, 120),
            Icmp(second, 2, "cloudflare", "1.1.1.1", "IPv4", "same-target", true, 150)
        };

        var jitter = JitterCalculator.MeanAbsoluteDifferenceBySeries(measurements);

        Assert.True(double.IsNaN(jitter[Key(first, "cloudflare", "1.1.1.1", "IPv4", "same-target")]));
        Assert.Equal(25, jitter[Key(second, "cloudflare", "1.1.1.1", "IPv4", "same-target")]);
    }

    [Fact]
    public void InsufficientSuccessfulSamplesReturnsNan()
    {
        var sessionId = Guid.NewGuid();
        var measurements = new[]
        {
            Icmp(sessionId, 0, "cloudflare", "1.1.1.1", "IPv4", "stream", true, 10),
            Icmp(sessionId, 1, "cloudflare", "1.1.1.1", "IPv4", "stream", true, 11)
        };

        var jitter = JitterCalculator.MeanAbsoluteDifferenceBySeries(measurements);

        Assert.True(double.IsNaN(jitter[Key(sessionId, "cloudflare", "1.1.1.1", "IPv4", "stream")]));
    }

    [Fact]
    public void FailedProbesCountAsLossButNotRttOrJitterSamples()
    {
        var sessionId = Guid.NewGuid();
        var key = Key(sessionId, "cloudflare", "1.1.1.1", "IPv4", "stream");
        var measurements = new[]
        {
            Icmp(sessionId, 0, "cloudflare", "1.1.1.1", "IPv4", "stream", true, 10),
            Icmp(sessionId, 1, "cloudflare", "1.1.1.1", "IPv4", "stream", false, null),
            Icmp(sessionId, 2, "cloudflare", "1.1.1.1", "IPv4", "stream", true, 13),
            Icmp(sessionId, 3, "cloudflare", "1.1.1.1", "IPv4", "stream", true, 19)
        };

        var stats = JitterCalculator.CalculateIcmpStatistics(measurements, key);

        Assert.Equal(4, stats.SampleCount);
        Assert.Equal(3, stats.SuccessfulSampleCount);
        Assert.Equal(1, stats.FailedSampleCount);
        Assert.Equal(4.5, stats.MeanAbsoluteSuccessiveDifferenceMilliseconds);
    }

    [Fact]
    public void StatisticsIncludeOutliersWithoutDiscardingThem()
    {
        var sessionId = Guid.NewGuid();
        var key = Key(sessionId, "cloudflare", "1.1.1.1", "IPv4", "stream");
        var measurements = new[]
        {
            Icmp(sessionId, 0, "cloudflare", "1.1.1.1", "IPv4", "stream", true, 10),
            Icmp(sessionId, 1, "cloudflare", "1.1.1.1", "IPv4", "stream", true, 11),
            Icmp(sessionId, 2, "cloudflare", "1.1.1.1", "IPv4", "stream", true, 12),
            Icmp(sessionId, 3, "cloudflare", "1.1.1.1", "IPv4", "stream", true, 200),
            Icmp(sessionId, 4, "cloudflare", "1.1.1.1", "IPv4", "stream", true, 13)
        };

        var stats = JitterCalculator.CalculateIcmpStatistics(measurements, key);

        Assert.Equal(10, stats.MinimumRttMilliseconds);
        Assert.Equal(12, stats.MedianRttMilliseconds);
        Assert.Equal(49.2, stats.MeanRttMilliseconds);
        Assert.Equal(162.6, stats.P95RttMilliseconds!.Value, precision: 1);
        Assert.Equal(200, stats.MaximumRttMilliseconds);
        Assert.Equal(94.25, stats.MeanAbsoluteSuccessiveDifferenceMilliseconds);
    }

    private static ProbeMeasurement Icmp(Guid sessionId, int seconds, string target, string host, string family, string stream, bool succeeded, double? latency)
        => new(sessionId, At(seconds), ProbeMethod.Icmp, target, succeeded, latency, succeeded ? null : "Timeout", succeeded ? null : "No reply.", host, family, stream, seconds + 1);

    private static DateTimeOffset At(int seconds) => new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero).AddSeconds(seconds);

    private static LatencySeriesKey Key(Guid sessionId, string target, string host, string family, string stream)
        => new(sessionId, ProbeMethod.Icmp, target, host, family, stream);
}
