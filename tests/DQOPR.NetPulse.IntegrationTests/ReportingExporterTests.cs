using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Reporting;

namespace DQOPR.NetPulse.IntegrationTests;

public sealed class ReportingExporterTests
{
    [Fact]
    public async Task ExportersCreateCsvJsonAndHtmlEvidenceFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netpulse-reporting-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var session = new MonitoringSession(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow, "test", TimeSpan.FromMinutes(10), TimeSpan.Zero, SessionStatus.Completed);
        var measurements = new[]
        {
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-5), ProbeMethod.Icmp, "Cloudflare", true, 12),
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-4), ProbeMethod.Icmp, "Cloudflare", false, null, "Timeout", "No reply."),
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-3), ProbeMethod.Dns, "example.com", true, 20),
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-2), ProbeMethod.TcpConnect, "Cloudflare HTTPS", true, 18),
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-1), ProbeMethod.Https, "example.com", true, 30)
        };
        var speeds = new[]
        {
            new SpeedTestMeasurement(session.Id, DateTimeOffset.UtcNow, "download", true, 80, 10_000_000, TimeSpan.FromSeconds(1), "test", "https://example.test/download", null, null),
            new SpeedTestMeasurement(session.Id, DateTimeOffset.UtcNow, "upload", true, 12, 2_000_000, TimeSpan.FromSeconds(1), "test", "https://example.test/upload", null, null)
        };
        var events = new[] { new NetworkInterfaceEvent(session.Id, DateTimeOffset.UtcNow, "snapshot", "Ethernet", "192.168.1.1", "test") };
        var markers = new[] { new ManualMarker(Guid.NewGuid(), session.Id, DateTimeOffset.UtcNow, "Internet felt bad.") };
        var csv = Path.Combine(directory, "session.csv");
        var json = Path.Combine(directory, "session.json");
        var html = Path.Combine(directory, "session.html");

        await CsvSessionExporter.ExportMeasurementsAsync(csv, measurements, speeds, CancellationToken.None);
        await JsonMeasurementExporter.ExportAsync(json, [session], measurements, speeds, CancellationToken.None);
        await HtmlReportGenerator.GenerateAsync(html, session, measurements, speeds, events, markers, CancellationToken.None);

        Assert.Contains("probe", await File.ReadAllTextAsync(csv));
        Assert.Contains("\"Cloudflare\"", await File.ReadAllTextAsync(csv));
        Assert.Contains("speedTests", await File.ReadAllTextAsync(json));
        var report = await File.ReadAllTextAsync(html);
        Assert.Contains("not an ISP-certified speed test", report, StringComparison.Ordinal);
        Assert.Contains("ICMP Packet Loss", report, StringComparison.Ordinal);
        Assert.Contains("Internet felt bad.", report, StringComparison.Ordinal);
    }
}
