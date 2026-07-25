using DQOPR.NetPulse.Core.Configuration;

namespace DQOPR.NetPulse.Core.Tests.Configuration;

public sealed class ApplicationSettingsTests
{
    [Fact]
    public void ValidateRejectsMissingRequiredValues()
    {
        var settings = ApplicationSettings.Defaults("", "")
            with
        {
            MonitoringDuration = TimeSpan.Zero,
            IcmpTargets = "",
            DnsHostname = "",
            HttpsEndpoint = "http://example.com"
        };

        var errors = settings.Validate();

        Assert.Contains(errors, error => error.Contains("Monitoring duration", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("ICMP target", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("DNS hostname", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("https://", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Database path", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Export directory", StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultsUseNonRateLimitedDownloadEndpoint()
    {
        var settings = ApplicationSettings.Defaults("netpulse.sqlite3", "exports");

        Assert.Equal("https://cachefly.cachefly.net/100mb.test", settings.DownloadEndpoint);
        Assert.DoesNotContain("speed.cloudflare.com/__down", settings.DownloadEndpoint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StorePersistsEditableSettings()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpulse-settings-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "settings.json");
        var defaults = ApplicationSettings.Defaults(Path.Combine(directory, "netpulse.db"), Path.Combine(directory, "exports"));
        var store = new ApplicationSettingsStore(path, defaults);
        var settings = defaults with
        {
            MonitoringDuration = TimeSpan.FromMinutes(20),
            SpeedTestInterval = TimeSpan.FromMinutes(4),
            IcmpTargets = "Cloudflare=1.1.1.1"
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(20), loaded.MonitoringDuration);
        Assert.Equal(TimeSpan.FromMinutes(4), loaded.SpeedTestInterval);
        Assert.Equal("Cloudflare=1.1.1.1", loaded.IcmpTargets);
    }
}
