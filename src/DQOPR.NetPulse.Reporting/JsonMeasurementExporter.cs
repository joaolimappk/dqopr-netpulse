using System.Text.Json;
using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Reporting;

public static class JsonMeasurementExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static async Task ExportAsync(
        string path,
        IReadOnlyList<MonitoringSession> sessions,
        IReadOnlyList<ProbeMeasurement> measurements,
        IReadOnlyList<SpeedTestMeasurement> speedTests,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var payload = new
        {
            exportedAt = DateTimeOffset.UtcNow,
            note = "Anonymized CI smoke export. Do not treat GitHub runner measurements as a user ISP path.",
            sessions,
            measurements = measurements.Select(measurement => new
            {
                measurement.SessionId,
                measurement.ObservedAt,
                measurement.Method,
                measurement.TargetName,
                measurement.Succeeded,
                measurement.LatencyMilliseconds,
                measurement.FailureCategory
            }),
            speedTests = speedTests.Select(speed => new
            {
                speed.SessionId,
                speed.ObservedAt,
                speed.Direction,
                speed.Succeeded,
                speed.MegabitsPerSecond,
                speed.BytesTransferred,
                speed.ActiveDuration,
                speed.Provider,
                Endpoint = MaskEndpoint(speed.Endpoint),
                speed.FailureCategory
            })
        };

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, Options, cancellationToken).ConfigureAwait(false);
    }

    private static string? MaskEndpoint(string? endpoint)
    {
        if (endpoint is null || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
        {
            return endpoint;
        }

        return $"{uri.Scheme}://{uri.Host}/...";
    }
}
