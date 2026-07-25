using DQOPR.NetPulse.Core.QuickTest;

namespace DQOPR.NetPulse.Core.Tests.QuickTest;

public sealed class QuickTestOptionsTests
{
    [Fact]
    public void DefaultsUseMeaningfulProbeBurst()
    {
        var options = new QuickTestOptions();

        options.Validate();

        Assert.InRange(options.ProbeBurstCount, 10, 20);
    }

    [Fact]
    public void RejectsSingleProbeQuickTest()
    {
        var options = new QuickTestOptions { ProbeBurstCount = 1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }
}
