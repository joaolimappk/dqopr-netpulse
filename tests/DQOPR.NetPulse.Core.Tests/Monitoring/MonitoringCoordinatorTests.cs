using DQOPR.NetPulse.Core.Configuration;
using DQOPR.NetPulse.Core.Monitoring;

namespace DQOPR.NetPulse.Core.Tests.Monitoring;

public sealed class MonitoringCoordinatorTests
{
    [Fact]
    public async Task StartPreventsDuplicateMonitoringSessions()
    {
        var clock = new FakeClock { BlockDelays = true };
        var coordinator = new MonitoringCoordinator(new FakeProbeService(), new FakeEnvironment(), new FakeStore(), clock);

        await coordinator.StartAsync(new MonitoringOptions { ActiveDuration = TimeSpan.FromHours(1) }, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartAsync(new MonitoringOptions(), CancellationToken.None));
        await coordinator.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task QuickTestUsesConfiguredProbeBurstForEachIcmpTarget()
    {
        var probes = new FakeProbeService();
        var store = new FakeStore();
        var runner = new QuickTestRunner(probes, new FakeEnvironment(), store, new FakeClock());
        var targets = new MonitoringTargets(
            IcmpTargets:
            [
                new("Cloudflare", Core.Models.TargetPurpose.ExternalIcmp, "1.1.1.1"),
                new("Quad9", Core.Models.TargetPurpose.ExternalIcmp, "9.9.9.9")
            ],
            TcpTargets: [],
            DnsHostname: "example.com",
            HttpsUri: new Uri("https://www.example.com/"),
            DownloadUri: new Uri("https://speed.cloudflare.com/__down?bytes=1"),
            UploadUri: new Uri("https://speed.cloudflare.com/__up"));

        await runner.RunAsync(new() { ProbeBurstCount = 20, ProbeSpacing = TimeSpan.FromMilliseconds(100) }, targets, CancellationToken.None);

        Assert.Equal(60, probes.IcmpProbeCount);
        Assert.Equal(60, store.Measurements.Count(measurement => measurement.Method == Core.Models.ProbeMethod.Icmp));
    }

    [Fact]
    public async Task QuickTestReportsPartialWhenUploadIsUnavailable()
    {
        var probes = new FakeProbeService
        {
            SpeedTestFactory = (sessionId, downloadUri, uploadUri) =>
            [
                new(sessionId, DateTimeOffset.UtcNow, "download", true, 50, 50_000_000, TimeSpan.FromSeconds(8), "fake", downloadUri.ToString(), null, null),
                new(sessionId, DateTimeOffset.UtcNow, "upload", false, null, 0, TimeSpan.FromSeconds(8), "fake", uploadUri.ToString(), "HttpRequestException", "Upload failed.", Core.Models.SpeedResultStatus.UploadEndpointUnavailable)
            ]
        };
        var runner = new QuickTestRunner(probes, new FakeEnvironment(), new FakeStore(), new FakeClock());

        var result = await runner.RunAsync(new() { ProbeBurstCount = 20, ProbeSpacing = TimeSpan.FromMilliseconds(100) }, MonitoringTargets.Defaults, CancellationToken.None);

        Assert.Contains("Partial test", result.Summary, StringComparison.Ordinal);
        Assert.Contains("upload", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CoordinatorRunsIndependentProbeCategories()
    {
        var probes = new FakeProbeService();
        var store = new FakeStore();
        var coordinator = new MonitoringCoordinator(probes, new FakeEnvironment(), store, new FakeClock());

        await coordinator.StartAsync(
            new MonitoringOptions
            {
                ActiveDuration = TimeSpan.FromMilliseconds(500),
                Intervals = new MonitoringIntervals(
                    Icmp: TimeSpan.FromMilliseconds(100),
                    TcpConnect: TimeSpan.FromMilliseconds(200),
                    Dns: TimeSpan.FromMilliseconds(300),
                    Https: TimeSpan.FromMilliseconds(400),
                    InterfaceSnapshot: TimeSpan.FromSeconds(1),
                    RouteSnapshot: TimeSpan.FromSeconds(1),
                    PublicIp: TimeSpan.FromSeconds(1),
                    SpeedTest: TimeSpan.FromMilliseconds(450))
            },
            CancellationToken.None);

        await Task.Delay(50);
        await coordinator.StopAsync(CancellationToken.None);

        Assert.Contains(store.Measurements, measurement => measurement.Method == Core.Models.ProbeMethod.Icmp);
        Assert.Contains(store.Measurements, measurement => measurement.Method == Core.Models.ProbeMethod.TcpConnect);
        Assert.Contains(store.Measurements, measurement => measurement.Method == Core.Models.ProbeMethod.Dns);
        Assert.Contains(store.Measurements, measurement => measurement.Method == Core.Models.ProbeMethod.Https);
        Assert.NotEmpty(store.SpeedTests);
    }
}
