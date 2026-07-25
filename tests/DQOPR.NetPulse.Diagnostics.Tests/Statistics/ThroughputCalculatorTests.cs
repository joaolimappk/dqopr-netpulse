using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Diagnostics.Statistics;

namespace DQOPR.NetPulse.Diagnostics.Tests.Statistics;

public sealed class ThroughputCalculatorTests
{
    [Fact]
    public void CalculatesMbpsFromActualBytesAndTransferDuration()
    {
        var mbps = ThroughputCalculator.MegabitsPerSecond(25_000_000, TimeSpan.FromSeconds(10));

        Assert.Equal(20.0, mbps);
    }

    [Fact]
    public void UsesTransferDurationSoWarmupIsExcludedByCaller()
    {
        var mbpsWithWarmupIncluded = ThroughputCalculator.MegabitsPerSecond(100_000_000, TimeSpan.FromSeconds(11));
        var mbpsWithWarmupExcluded = ThroughputCalculator.MegabitsPerSecond(100_000_000, TimeSpan.FromSeconds(10));

        Assert.True(mbpsWithWarmupExcluded > mbpsWithWarmupIncluded);
        Assert.Equal(80.0, mbpsWithWarmupExcluded);
    }

    [Fact]
    public void ClassifiesPartialStreamFailureAsDegraded()
    {
        var status = ThroughputCalculator.Classify(anySucceeded: true, anyFailed: true, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(8));

        Assert.Equal(SpeedResultStatus.Degraded, status);
    }

    [Fact]
    public void ClassifiesShortTransferAsInsufficientDuration()
    {
        var status = ThroughputCalculator.Classify(anySucceeded: true, anyFailed: false, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8));

        Assert.Equal(SpeedResultStatus.InsufficientDuration, status);
    }

    [Fact]
    public void ClassifiesUploadEndpointFailureSeparately()
    {
        var status = ThroughputCalculator.Classify(anySucceeded: false, anyFailed: true, TimeSpan.Zero, TimeSpan.FromSeconds(8), uploadEndpointUnavailable: true);

        Assert.Equal(SpeedResultStatus.UploadEndpointUnavailable, status);
    }

    [Fact]
    public void ClassifiesCancellationSeparately()
    {
        var status = ThroughputCalculator.Classify(anySucceeded: false, anyFailed: false, TimeSpan.Zero, TimeSpan.FromSeconds(8), canceled: true);

        Assert.Equal(SpeedResultStatus.TestCanceled, status);
    }
}
