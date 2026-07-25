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
        var successes = measurements.Where(m => m is { Succeeded: true, LatencyMilliseconds: not null }).ToArray();
        var packetLoss = PacketLossSummary.ByIcmpTarget(measurements);

        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><title>DQOPR NetPulse Report</title>");
        html.AppendLine("<style>body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#172026}table{border-collapse:collapse;width:100%;margin:12px 0}td,th{border:1px solid #cbd5df;padding:6px;text-align:left}.bar{display:inline-block;background:#0b6b6b;height:10px}.warn{color:#7a4b00}</style>");
        html.AppendLine("</head><body>");
        html.AppendLine("<h1>DQOPR NetPulse Report</h1>");
        html.AppendLine("<p class=\"warn\">The built-in throughput test is an estimate and is not an ISP-certified speed test.</p>");
        html.AppendLine("<h2>Session Summary</h2><table>");
        Row(html, "Session", session.Id.ToString());
        Row(html, "Status", session.Status.ToString());
        Row(html, "Start", session.StartedAt.ToString("O", CultureInfo.InvariantCulture));
        Row(html, "End", session.EndedAt?.ToString("O", CultureInfo.InvariantCulture) ?? "");
        Row(html, "Active duration", session.ActiveDuration.ToString());
        Row(html, "Paused duration", session.PausedDuration.ToString());
        Row(html, "Measurements", measurements.Count.ToString(CultureInfo.InvariantCulture));
        html.AppendLine("</table>");

        html.AppendLine("<h2>Methodology</h2>");
        html.AppendLine("<p>ICMP packet loss is calculated from ICMP samples only. TCP, DNS, HTTPS, and throughput failures are reported separately. Jitter uses mean absolute difference between consecutive successful samples for the same target and method.</p>");

        html.AppendLine("<h2>Latency Statistics</h2><table><tr><th>Metric</th><th>Value</th></tr>");
        Row(html, "Average latency", successes.Length == 0 ? "unavailable" : $"{successes.Average(m => m.LatencyMilliseconds!.Value):0.0} ms");
        Row(html, "Maximum latency", successes.Length == 0 ? "unavailable" : $"{successes.Max(m => m.LatencyMilliseconds!.Value):0.0} ms");
        Row(html, "ICMP samples", icmp.Length.ToString(CultureInfo.InvariantCulture));
        html.AppendLine("</table>");

        html.AppendLine("<h2>ICMP Packet Loss</h2><table><tr><th>Target</th><th>Sent</th><th>Received</th><th>Lost</th><th>Loss</th></tr>");
        foreach (var loss in packetLoss)
        {
            html.Append("<tr><td>").Append(E(loss.TargetName)).Append("</td><td>").Append(loss.Sent).Append("</td><td>").Append(loss.Received).Append("</td><td>").Append(loss.Lost).Append("</td><td>").Append($"{loss.LossPercent:0.0}%").AppendLine("</td></tr>");
        }

        html.AppendLine("</table>");
        SectionTable(html, "DNS/TCP/HTTPS Results", measurements.Where(m => m.Method is ProbeMethod.Dns or ProbeMethod.TcpConnect or ProbeMethod.Https));
        html.AppendLine("<h2>Speed Results</h2><table><tr><th>Time</th><th>Direction</th><th>Result</th><th>Provider</th></tr>");
        foreach (var speed in speedTests)
        {
            html.Append("<tr><td>").Append(E(speed.ObservedAt.ToString("O", CultureInfo.InvariantCulture))).Append("</td><td>").Append(E(speed.Direction)).Append("</td><td>").Append(speed.Succeeded ? $"{speed.MegabitsPerSecond:0.0} Mbps" : E(speed.FailureCategory)).Append("</td><td>").Append(E(speed.Provider)).AppendLine("</td></tr>");
        }

        html.AppendLine("</table>");
        html.AppendLine("<h2>Incident/Event Timeline</h2><table><tr><th>Time</th><th>Type</th><th>Details</th></tr>");
        foreach (var marker in markers)
        {
            html.Append("<tr><td>").Append(E(marker.ObservedAt.ToString("O", CultureInfo.InvariantCulture))).Append("</td><td>manual marker</td><td>").Append(E(marker.Note)).AppendLine("</td></tr>");
        }

        foreach (var evt in networkEvents)
        {
            html.Append("<tr><td>").Append(E(evt.ObservedAt.ToString("O", CultureInfo.InvariantCulture))).Append("</td><td>").Append(E(evt.EventType)).Append("</td><td>").Append(E($"{evt.InterfaceName} {evt.Gateway} {evt.Details}")).AppendLine("</td></tr>");
        }

        html.AppendLine("</table>");
        html.AppendLine("<h2>Latency Chart</h2>");
        foreach (var measurement in successes.TakeLast(80))
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
        html.Append("<h2>").Append(E(title)).AppendLine("</h2><table><tr><th>Time</th><th>Method</th><th>Target</th><th>Result</th><th>Latency</th></tr>");
        foreach (var measurement in measurements)
        {
            html.Append("<tr><td>").Append(E(measurement.ObservedAt.ToString("O", CultureInfo.InvariantCulture))).Append("</td><td>")
                .Append(E(measurement.Method.ToString())).Append("</td><td>").Append(E(measurement.TargetName)).Append("</td><td>")
                .Append(E(measurement.Succeeded ? "success" : measurement.FailureCategory ?? "failure")).Append("</td><td>")
                .Append(E(measurement.LatencyMilliseconds?.ToString("0.0", CultureInfo.InvariantCulture) ?? "")).AppendLine("</td></tr>");
        }

        html.AppendLine("</table>");
    }

    private static void Row(StringBuilder html, string label, string value)
        => html.Append("<tr><th>").Append(E(label)).Append("</th><td>").Append(E(value)).AppendLine("</td></tr>");

    private static string E(string? value) => WebUtility.HtmlEncode(value ?? "");
}
