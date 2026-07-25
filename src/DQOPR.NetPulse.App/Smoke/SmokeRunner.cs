using System.Text.Json;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DQOPR.NetPulse.Core.Configuration;
using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Core.Monitoring;
using DQOPR.NetPulse.Core.QuickTest;

namespace DQOPR.NetPulse.App.Smoke;

public static class SmokeRunner
{
    public static async Task RunAsync(MainWindow window, SmokeOptions options)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        await window.ViewModel.InitializeAsync().ConfigureAwait(true);

        var smokeTargets = new MonitoringTargets(
            IcmpTargets: [new TargetDefinition("Cloudflare", TargetPurpose.ExternalIcmp, "1.1.1.1")],
            TcpTargets: [new TargetDefinition("Cloudflare HTTPS", TargetPurpose.TcpConnect, "cloudflare.com", 443)],
            DnsHostname: "example.com",
            HttpsUri: new Uri("https://www.example.com/"),
            DownloadUri: new Uri("https://cachefly.cachefly.net/100mb.test"),
            UploadUri: new Uri("https://speed.cloudflare.com/__up"));

        await window.ViewModel.StartMonitoringAsync(new MonitoringOptions
        {
            ProfileName = "CI Smoke Monitoring",
            ActiveDuration = TimeSpan.FromSeconds(options.DurationSeconds),
            Targets = smokeTargets,
            SchedulerTick = TimeSpan.FromMilliseconds(100),
            Intervals = new MonitoringIntervals(
                Icmp: TimeSpan.FromSeconds(2),
                TcpConnect: TimeSpan.FromSeconds(3),
                Dns: TimeSpan.FromSeconds(3),
                Https: TimeSpan.FromSeconds(4),
                InterfaceSnapshot: TimeSpan.FromSeconds(5),
                RouteSnapshot: TimeSpan.FromSeconds(5),
                PublicIp: TimeSpan.FromSeconds(30),
                SpeedTest: TimeSpan.FromSeconds(20))
        }).ConfigureAwait(true);

        await Task.Delay(TimeSpan.FromSeconds(Math.Min(6, options.DurationSeconds)), CancellationToken.None).ConfigureAwait(true);
        SaveScreenshot(window, Path.Combine(options.OutputDirectory, "active-monitoring.png"));

        await window.ViewModel.PauseAsync().ConfigureAwait(true);
        await Task.Delay(500).ConfigureAwait(true);
        await window.ViewModel.ResumeAsync().ConfigureAwait(true);
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, options.DurationSeconds - 6))).ConfigureAwait(true);
        await window.ViewModel.StopMonitoringAsync().ConfigureAwait(true);

        await window.ViewModel.RunQuickTestAsync(
            new QuickTestOptions
            {
                ProbeBurstCount = 20,
                ProbeSpacing = TimeSpan.FromMilliseconds(250),
                ProbeTimeout = TimeSpan.FromMilliseconds(750),
                IncludeDownloadEstimate = true,
                IncludeUploadEstimate = true
            },
            smokeTargets).ConfigureAwait(true);

        SaveTabScreenshot(window, 0, options.OutputDirectory, "quick-test-complete.png");
        await window.ViewModel.RefreshHistoryAsync().ConfigureAwait(true);
        await window.ViewModel.OpenSelectedSessionAsync().ConfigureAwait(true);
        SaveTabScreenshot(window, 0, options.OutputDirectory, "dashboard.png");
        SaveTabScreenshot(window, 1, options.OutputDirectory, "history.png");
        SaveTabScreenshot(window, 2, options.OutputDirectory, "session-details.png");
        SaveTabScreenshot(window, 3, options.OutputDirectory, "reports.png");
        SaveTabScreenshot(window, 4, options.OutputDirectory, "settings.png");
        SaveTabScreenshot(window, 5, options.OutputDirectory, "activity-log.png");
        SaveTabScreenshot(window, 6, options.OutputDirectory, "about.png");

        await window.ViewModel.ExportSelectedSessionAsync("all").ConfigureAwait(true);
        await window.ViewModel.ExportLatestAsync(Path.Combine(options.OutputDirectory, "measurements-export.json")).ConfigureAwait(true);

        var generatedExports = Directory.GetFiles(options.OutputDirectory, "netpulse-*.*")
            .Select(Path.GetFileName)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var screenshots = Directory.GetFiles(options.OutputDirectory, "*.png")
            .Select(file => new { name = Path.GetFileName(file), bytes = new FileInfo(file).Length })
            .OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await File.WriteAllTextAsync(
            Path.Combine(options.OutputDirectory, "smoke_metadata.json"),
            JsonSerializer.Serialize(
                new
                {
                    generatedAt = DateTimeOffset.UtcNow,
                    environment = "GitHub Windows runner smoke test or local smoke mode",
                    screenshots = "WPF RenderTargetBitmap, not an attended desktop screenshot",
                    database = options.DatabasePath,
                    monitoringDurationSeconds = options.DurationSeconds,
                    screenshotFiles = screenshots,
                    exportFiles = generatedExports
                },
                new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(true);
    }

    private static void SaveTabScreenshot(MainWindow window, int selectedTabIndex, string outputDirectory, string fileName)
    {
        window.ViewModel.SelectedTabIndex = selectedTabIndex;
        window.UpdateLayout();
        SaveScreenshot(window, Path.Combine(outputDirectory, fileName));
    }

    private static void SaveScreenshot(MainWindow window, string path)
    {
        window.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
