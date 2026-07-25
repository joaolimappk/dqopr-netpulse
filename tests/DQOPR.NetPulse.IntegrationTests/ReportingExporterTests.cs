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
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-5), ProbeMethod.Icmp, "Cloudflare", true, 12, TargetHost: "1.1.1.1", AddressFamily: "IPv4", ProbeStreamId: "cf-v4", Sequence: 1),
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-4), ProbeMethod.Icmp, "Cloudflare", false, null, "Timeout", "No reply.", "1.1.1.1", "IPv4", "cf-v4", 2),
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-3), ProbeMethod.Icmp, "Cloudflare", true, 14, TargetHost: "1.1.1.1", AddressFamily: "IPv4", ProbeStreamId: "cf-v4", Sequence: 3),
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-2), ProbeMethod.Icmp, "Cloudflare", true, 16, TargetHost: "1.1.1.1", AddressFamily: "IPv4", ProbeStreamId: "cf-v4", Sequence: 4),
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-3), ProbeMethod.Dns, "example.com", true, 20),
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-2), ProbeMethod.TcpConnect, "Cloudflare HTTPS", true, 18),
            new ProbeMeasurement(session.Id, DateTimeOffset.UtcNow.AddSeconds(-1), ProbeMethod.Https, "example.com", true, 30)
        };
        var speeds = new[]
        {
            new SpeedTestMeasurement(session.Id, DateTimeOffset.UtcNow, "download", true, 80, 100_000_000, TimeSpan.FromSeconds(10), "test", "https://example.test/download", null, null, SpeedResultStatus.Valid, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1), 4, "2.0", MeasurementMethodology.CurrentVersion, "{\"streams\":[]}"),
            new SpeedTestMeasurement(session.Id, DateTimeOffset.UtcNow, "upload", false, null, 0, TimeSpan.FromSeconds(10), "test", "https://example.test/upload", "HttpRequestException", "Upload failed.", SpeedResultStatus.UploadEndpointUnavailable)
        };
        var events = new[] { new NetworkInterfaceEvent(session.Id, DateTimeOffset.UtcNow, "snapshot", "Ethernet", "192.168.1.1", "test") };
        var markers = new[] { new ManualMarker(Guid.NewGuid(), session.Id, DateTimeOffset.UtcNow, "Internet felt bad.") };
        var csv = Path.Combine(directory, "session.csv");
        var json = Path.Combine(directory, "session.json");
        var html = Path.Combine(directory, "session.html");
        var diagnostics = Path.Combine(directory, "diagnostics.json");

        await CsvSessionExporter.ExportMeasurementsAsync(csv, measurements, speeds, CancellationToken.None);
        await JsonMeasurementExporter.ExportAsync(json, [session], measurements, speeds, CancellationToken.None);
        await HtmlReportGenerator.GenerateAsync(html, session, measurements, speeds, events, markers, CancellationToken.None);
        await DiagnosticBundleExporter.ExportAsync(diagnostics, session, measurements, speeds, events, markers, [], CancellationToken.None);

        var csvText = await File.ReadAllTextAsync(csv);
        Assert.Contains("observed_at_local", csvText);
        Assert.Contains("\"cf-v4\"", csvText);
        Assert.Contains("Upload endpoint unavailable", csvText);
        var jsonText = await File.ReadAllTextAsync(json);
        Assert.Contains("speedTests", jsonText);
        Assert.Contains("MethodologyVersion", jsonText);
        var report = await File.ReadAllTextAsync(html);
        Assert.Contains("not an ISP-certified speed test", report, StringComparison.Ordinal);
        Assert.Contains("ICMP Packet Loss", report, StringComparison.Ordinal);
        Assert.Contains("ICMP Latency Statistics", report, StringComparison.Ordinal);
        Assert.Contains("Upload endpoint unavailable", report, StringComparison.Ordinal);
        Assert.Contains("Internet felt bad.", report, StringComparison.Ordinal);
        Assert.Contains("rawIcmpSamples", await File.ReadAllTextAsync(diagnostics));
    }
}
