using System.Text.Json;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DQOPR.NetPulse.App.ViewModels;
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

        var quickTest = await window.ViewModel.RunQuickTestAsync(
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
        window.ViewModel.SelectedSession = window.ViewModel.Sessions.FirstOrDefault(session => session.Id == quickTest.Session.Id)
            ?? throw new InvalidOperationException($"Quick Test session {quickTest.Session.Id} was not present in History.");
        await window.ViewModel.OpenSelectedSessionAsync().ConfigureAwait(true);
        AssertSessionDetailsLoaded(window.ViewModel, quickTest.Session.Id);
        SaveTabScreenshot(window, 0, options.OutputDirectory, "dashboard.png");
        SaveTabScreenshot(window, 1, options.OutputDirectory, "history.png");
        SaveTabScreenshot(window, 2, options.OutputDirectory, "session-details.png");
        SaveDetailTabScreenshot(window, 0, options.OutputDirectory, "session-details-timeline.png");
        SaveDetailTabScreenshot(window, 1, options.OutputDirectory, "session-details-icmp.png");
        SaveDetailTabScreenshot(window, 2, options.OutputDirectory, "session-details-connectivity.png");
        SaveDetailTabScreenshot(window, 3, options.OutputDirectory, "session-details-speed-tests.png");
        SaveDetailTabScreenshot(window, 4, options.OutputDirectory, "session-details-events-markers.png");
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
                    selectedSessionId = window.ViewModel.SelectedSession?.Id,
                    detailCounts = new
                    {
                        timelineRows = window.ViewModel.DetailTimelineRows.Count,
                        icmpSummaryRows = window.ViewModel.DetailIcmpSummaryRows.Count,
                        icmpRows = window.ViewModel.DetailIcmpRows.Count,
                        connectivityRows = window.ViewModel.DetailConnectivityRows.Count,
                        speedTestRows = window.ViewModel.DetailSpeedTestRows.Count,
                        eventRows = window.ViewModel.DetailEventRows.Count,
                        latencyChartPoints = window.ViewModel.LatencyChartCount,
                        jitterChartPoints = window.ViewModel.JitterChartCount,
                        packetLossChartPoints = window.ViewModel.PacketLossChartCount,
                        speedChartPoints = window.ViewModel.SpeedChartCount
                    },
                    screenshotFiles = screenshots,
                    exportFiles = generatedExports
                },
                new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(true);
    }

    private static void AssertSessionDetailsLoaded(DashboardViewModel viewModel, Guid expectedSessionId)
    {
        if (viewModel.SelectedSession?.Id != expectedSessionId)
        {
            throw new InvalidOperationException($"Session Details loaded the wrong session. Expected {expectedSessionId}, got {viewModel.SelectedSession?.Id}.");
        }

        var failures = new List<string>();
        if (viewModel.DetailTimelineRows.Count == 0)
        {
            failures.Add("Timeline has no rows.");
        }

        if (viewModel.DetailIcmpSummaryRows.Count == 0 || viewModel.DetailIcmpRows.Count == 0)
        {
            failures.Add("ICMP tab has no rows.");
        }

        if (viewModel.DetailConnectivityRows.Count == 0)
        {
            failures.Add("DNS/TCP/HTTPS tab has no rows.");
        }

        if (viewModel.DetailSpeedTestRows.Count == 0)
        {
            failures.Add("Speed Tests tab has no rows.");
        }

        if (viewModel.DetailEventRows.Count == 0)
        {
            failures.Add("Events and Markers tab has no rows.");
        }

        if (viewModel.LatencyChartCount == 0)
        {
            failures.Add("Latency chart has no points.");
        }

        if (viewModel.JitterChartCount == 0)
        {
            failures.Add("Jitter chart has no points.");
        }

        if (viewModel.PacketLossChartCount == 0)
        {
            failures.Add("Packet-loss chart has no points.");
        }

        if (viewModel.SpeedChartCount == 0)
        {
            failures.Add("Download/upload chart has no points.");
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException("Session Details smoke validation failed: " + string.Join(" ", failures));
        }
    }

    private static void SaveTabScreenshot(MainWindow window, int selectedTabIndex, string outputDirectory, string fileName)
    {
        window.ViewModel.SelectedTabIndex = selectedTabIndex;
        window.UpdateLayout();
        SaveScreenshot(window, Path.Combine(outputDirectory, fileName));
    }

    private static void SaveDetailTabScreenshot(MainWindow window, int selectedDetailTabIndex, string outputDirectory, string fileName)
    {
        window.ViewModel.SelectedTabIndex = 2;
        window.ViewModel.SelectedDetailTabIndex = selectedDetailTabIndex;
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
