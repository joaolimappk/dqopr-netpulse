using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Networking.Tests;

public sealed class NetworkingAssemblyTests
{
    [Fact]
    public void ProbeMethodContractIncludesSpeedTestMethods()
    {
        Assert.Contains(ProbeMethod.SpeedTestDownload, Enum.GetValues<ProbeMethod>());
        Assert.Contains(ProbeMethod.SpeedTestUpload, Enum.GetValues<ProbeMethod>());
    }
}
