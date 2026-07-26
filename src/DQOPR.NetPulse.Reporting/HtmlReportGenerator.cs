using System.Globalization;
using System.Net;
using System.Text;
using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Diagnostics.Statistics;

namespace DQOPR.NetPulse.Reporting;

public static class HtmlReportGenerator
{
    public static async Task GenerateAsync(
        string path,
        MonitoringSession session,
        IReadOnlyList<ProbeMeasurement> measurements,
        IReadOnlyList<SpeedTestMeasurement> speedTests,
        IReadOnlyList<NetworkInterfaceEvent> networkEvents,
        IReadOnlyList<ManualMarker> markers,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var icmp = measurements.Where(m => m.Method == ProbeMethod.Icmp).ToArray();
        var internetIcmpSuccesses = measurements.Where(m => m is { Method: ProbeMethod.Icmp, Succeeded: true, LatencyMilliseconds: not null } && !IsGateway(m)).ToArray();
        var packetLoss = PacketLossSummary.ByIcmpTarget(measurements);
        var timezone = DateTimeOffset.Now.Offset.ToString();

        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>DQOPR NetPulse Report</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#172026}table{border-collapse:collapse;width:100%;margin:12px 0}td,th{border:1px solid #cbd5df;padding:6px;text-align:left}.bar{display:inline-block;background:#0b6b6b;height:10px}.warn{color:#7a4b00}</style>");
        html.AppendLine("</head><body>");
        html.AppendLine("<h1>DQOPR NetPulse Report</h1>");
        html.AppendLine("<p class=\"warn\">The built-in throughput test is an estimate and is not an ISP-certified speed test.</p>");
        html.AppendLine("<h2>Session Summary</h2><table>");
        Row(html, "Session", session.Id.ToString());
        Row(html, "Status", session.Status.ToString());
        Row(html, "Start UTC", session.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        Row(html, "Start local", Local(session.StartedAt));
        Row(html, "End UTC", session.EndedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "");
        Row(html, "End local", session.EndedAt is null ? "" : Local(session.EndedAt.Value));
        Row(html, "Local timezone offset", timezone);
        Row(html, "Active duration", session.ActiveDuration.ToString());
        Row(html, "Paused duration", session.PausedDuration.ToString());
        Row(html, "Methodology", session.MethodologyVersion);
        Row(html, "Measurements", measurements.Count.ToString(CultureInfo.InvariantCulture));
        html.AppendLine("</table>");

        html.AppendLine("<h2>Methodology</h2>");
        html.AppendLine("<p>Router latency, internet latency, and internet jitter are calculated only from ICMP rows. DNS, TCP, HTTPS, and throughput timings are reported separately. Jitter is the mean absolute difference between consecutive successful ICMP RTT samples that share session, target, address family, and probe stream, and it requires at least three successful samples.</p>");

        html.AppendLine("<h2>ICMP Latency Statistics</h2><table><tr><th>Target</th><th>Host</th><th>Family</th><th>Samples</th><th>Successful</th><th>Failed</th><th>Min</th><th>Median</th><th>Mean</th><th>P95</th><th>Max</th><th>Jitter MAD</th></tr>");
        foreach (var group in icmp.GroupBy(LatencySeriesKey.From))
        {
            var stats = JitterCalculator.CalculateIcmpStatistics(measurements, group.Key);
            html.Append("<tr><td>").Append(E(group.Key.TargetName)).Append("</td><td>")
                .Append(E(group.Key.TargetHost)).Append("</td><td>")
                .Append(E(group.Key.AddressFamily)).Append("</td><td>")
                .Append(stats.SampleCount).Append("</td><td>")
                .Append(stats.SuccessfulSampleCount).Append("</td><td>")
                .Append(stats.FailedSampleCount).Append("</td><td>")
                .Append(E(Ms(stats.MinimumRttMilliseconds))).Append("</td><td>")
                .Append(E(Ms(stats.MedianRttMilliseconds))).Append("</td><td>")
                .Append(E(Ms(stats.MeanRttMilliseconds))).Append("</td><td>")
                .Append(E(Ms(stats.P95RttMilliseconds))).Append("</td><td>")
                .Append(E(Ms(stats.MaximumRttMilliseconds))).Append("</td><td>")
                .Append(E(Ms(stats.MeanAbsoluteSuccessiveDifferenceMilliseconds))).AppendLine("</td></tr>");
        }

        Row(html, "Internet average latency", internetIcmpSuccesses.Length == 0 ? "unavailable" : $"{internetIcmpSuccesses.Average(m => m.LatencyMilliseconds!.Value):0.0} ms ICMP");
        Row(html, "Internet maximum latency", internetIcmpSuccesses.Length == 0 ? "unavailable" : $"{internetIcmpSuccesses.Max(m => m.LatencyMilliseconds!.Value):0.0} ms ICMP");
        html.AppendLine("</table>");

        html.AppendLine("<h2>ICMP Packet Loss</h2><table><tr><th>Target</th><th>Sent</th><th>Received</th><th>Lost</th><th>Loss</th></tr>");
        foreach (var loss in packetLoss)
        {
            html.Append("<tr><td>").Append(E(loss.TargetName)).Append("</td><td>").Append(loss.Sent).Append("</td><td>").Append(loss.Received).Append("</td><td>").Append(loss.Lost).Append("</td><td>").Append($"{loss.LossPercent:0.0}%").AppendLine("</td></tr>");
        }

        html.AppendLine("</table>");
        SectionTable(html, "DNS/TCP/HTTPS Results", measurements.Where(m => m.Method is ProbeMethod.Dns or ProbeMethod.TcpConnect or ProbeMethod.Https));
        html.AppendLine("<h2>Speed Results</h2><table><tr><th>Time UTC</th><th>Time Local</th><th>Direction</th><th>Status</th><th>Result</th><th>Bytes</th><th>Transfer</th><th>Warmup</th><th>Streams</th><th>HTTP</th><th>Provider</th><th>Endpoint</th><th>Methodology</th><th>Failure</th></tr>");
        foreach (var speed in speedTests)
        {
            html.Append("<tr><td>").Append(E(speed.ObservedAt.ToString("O", CultureInfo.InvariantCulture))).Append("</td><td>")
                .Append(E(Local(speed.ObservedAt))).Append("</td><td>")
                .Append(E(speed.Direction)).Append("</td><td>")
                .Append(E(speed.ResultStatus)).Append("</td><td>")
                .Append(E(DisplaySpeed(speed))).Append("</td><td>")
                .Append(speed.BytesTransferred.ToString(CultureInfo.InvariantCulture)).Append("</td><td>")
                .Append(E(speed.TransferDuration?.ToString() ?? "")).Append("</td><td>")
                .Append(E(speed.WarmupDuration?.ToString() ?? "")).Append("</td><td>")
                .Append(speed.ParallelStreamCount).Append("</td><td>")
                .Append(E(speed.HttpVersion)).Append("</td><td>")
                .Append(E(speed.Provider)).Append("</td><td>")
                .Append(E(speed.Endpoint)).Append("</td><td>")
                .Append(E(speed.MethodologyVersion)).Append("</td><td>")
                .Append(E($"{speed.FailureCategory} {speed.FailureMessage}".Trim())).AppendLine("</td></tr>");
        }

        html.AppendLine("</table>");
        html.AppendLine("<h2>Incident/Event Timeline</h2><table><tr><th>Time</th><th>Type</th><th>Details</th></tr>");
        foreach (var marker in markers)
        {
            html.Append("<tr><td>").Append(E(Local(marker.ObservedAt))).Append("</td><td>manual marker</td><td>").Append(E(marker.Note)).AppendLine("</td></tr>");
        }

        foreach (var evt in networkEvents)
        {
            html.Append("<tr><td>").Append(E(Local(evt.ObservedAt))).Append("</td><td>").Append(E(evt.EventType)).Append("</td><td>").Append(E($"{evt.InterfaceName} {evt.Gateway} {evt.Details}")).AppendLine("</td></tr>");
        }

        html.AppendLine("</table>");
        html.AppendLine("<h2>Latency Chart</h2>");
        foreach (var measurement in internetIcmpSuccesses.TakeLast(80))
        {
            var width = Math.Min(100, Math.Max(1, measurement.LatencyMilliseconds!.Value));
            html.Append("<div><span class=\"bar\" style=\"width:").Append(width.ToString("0", CultureInfo.InvariantCulture)).Append("px\"></span> ")
                .Append(E($"{measurement.ObservedAt:HH:mm:ss} {measurement.Method} {measurement.TargetName} {measurement.LatencyMilliseconds:0.0} ms")).AppendLine("</div>");
        }

        html.AppendLine("</body></html>");
        await File.WriteAllTextAsync(path, html.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private static void SectionTable(StringBuilder html, string title, IEnumerable<ProbeMeasurement> measurements)
    {
        html.Append("<h2>").Append(E(title)).AppendLine("</h2><table><tr><th>Time Local</th><th>Time UTC</th><th>Method</th><th>Target</th><th>Host</th><th>Result</th><th>Duration</th></tr>");
        foreach (var measurement in measurements)
        {
            html.Append("<tr><td>").Append(E(Local(measurement.ObservedAt))).Append("</td><td>")
                .Append(E(measurement.ObservedAt.ToString("O", CultureInfo.InvariantCulture))).Append("</td><td>")
                .Append(E(measurement.Method.ToString())).Append("</td><td>").Append(E(measurement.TargetName)).Append("</td><td>")
                .Append(E(measurement.TargetHost)).Append("</td><td>")
                .Append(E(measurement.Succeeded ? "success" : measurement.FailureCategory ?? "failure")).Append("</td><td>")
                .Append(E(measurement.LatencyMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? "")).AppendLine("</td></tr>");
        }

        html.AppendLine("</table>");
    }

    private static void Row(StringBuilder html, string label, string value)
        => html.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).AppendLine("</td></tr>");

    private static bool IsGateway(ProbeMeasurement measurement)
        => string.Equals(measurement.TargetName, "Local Gateway", StringComparison.OrdinalIgnoreCase);

    private static string DisplaySpeed(SpeedTestMeasurement speed)
        => speed is { Succeeded: true, MegabitsPerSecond: not null } && (speed.ResultStatus is SpeedResultStatus.Valid or SpeedResultStatus.Degraded or SpeedResultStatus.DegradedUploadEndpointMayBeLimiting)
            ? $"{speed.MegabitsPerSecond:0.0} Mbps"
            : "unavailable";

    private static string Local(DateTimeOffset value)
        => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    private static string Ms(double? value)
        => value is null ? "" : $"{value:0.0} ms";

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
