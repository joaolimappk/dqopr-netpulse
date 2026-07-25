using DQOPR.NetPulse.Core.QuickTest;

namespace DQOPR.NetPulse.Core.Tests.QuickTest;

public sealed class QuickTestOptionsTests
{
    [Fact]
    public void DefaultsUseMeaningfulProbeBurst()
    {
        var options = new QuickTestOptions();

        options.Validate();

        Assert.Equal(20, options.ProbeBurstCount);
    }

    [Fact]
    public void RejectsSingleProbeQuickTest()
    {
        var options = new QuickTestOptions { ProbeBurstCount = 1 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }

    [Fact]
    public void RejectsNineteenProbeQuickTest()
    {
        var options = new QuickTestOptions { ProbeBurstCount = 19 };

        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
    }
}
