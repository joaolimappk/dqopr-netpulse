using System.Text.Json;
using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Diagnostics.Statistics;

namespace DQOPR.NetPulse.Reporting;

public static class DiagnosticBundleExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static async Task ExportAsync(
        string path,
        MonitoringSession session,
        IReadOnlyList<ProbeMeasurement> measurements,
        IReadOnlyList<SpeedTestMeasurement> speedTests,
        IReadOnlyList<NetworkInterfaceEvent> networkEvents,
        IReadOnlyList<ManualMarker> markers,
        IReadOnlyList<ReferenceSpeedResult> referenceResults,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var icmpStats = measurements
            .Where(measurement => measurement.Method == ProbeMethod.Icmp)
            .GroupBy(LatencySeriesKey.From)
            .Select(group => JitterCalculator.CalculateIcmpStatistics(measurements, group.Key))
            .ToArray();

        var payload = new
        {
            exportedAtUtc = DateTimeOffset.UtcNow,
            exportedAtLocal = DateTimeOffset.Now,
            timezone = DateTimeOffset.Now.Offset.ToString(),
            methodologyVersion = MeasurementMethodology.CurrentVersion,
            session,
            rawIcmpSamples = measurements.Where(measurement => measurement.Method == ProbeMethod.Icmp),
            connectivityMeasurements = measurements.Where(measurement => measurement.Method is ProbeMethod.Dns or ProbeMethod.TcpConnect or ProbeMethod.Https),
            icmpStatistics = icmpStats,
            speedTests = speedTests.Select(speed => new
            {
                speed.SessionId,
                observedAtUtc = speed.ObservedAt,
                observedAtLocal = speed.ObservedAt.ToLocalTime(),
                speed.Direction,
                speed.Succeeded,
                speed.ResultStatus,
                speed.MegabitsPerSecond,
                speed.BytesTransferred,
                speed.ActiveDuration,
                speed.SetupDuration,
                speed.TransferDuration,
                speed.WarmupDuration,
                speed.ParallelStreamCount,
                speed.HttpVersion,
                speed.Provider,
                speed.Endpoint,
                speed.FailureCategory,
                speed.FailureMessage,
                speed.MethodologyVersion,
                speed.DiagnosticJson
            }),
            networkEvents,
            markers,
            referenceResults,
            notes = new[]
            {
                "No request headers, response headers, credentials, or payload bytes are included.",
                "Throughput diagnostics contain endpoint names, stream byte counts, durations, HTTP version, and safe exception category/message only."
            }
        };

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, Options, cancellationToken).ConfigureAwait(false);
    }
}
