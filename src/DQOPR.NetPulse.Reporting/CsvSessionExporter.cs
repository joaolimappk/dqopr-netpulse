using System.Globalization;
using System.Text;
using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Reporting;

public static class CsvSessionExporter
{
    public static async Task ExportMeasurementsAsync(string path, IReadOnlyList<ProbeMeasurement> measurements, IReadOnlyList<SpeedTestMeasurement> speedTests, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        var builder = new StringBuilder();
        builder.AppendLine("type,observed_at,method_or_direction,target_or_provider,succeeded,latency_ms,mbps,bytes_transferred,failure_category,failure_message");

        foreach (var measurement in measurements)
        {
            builder.Append("probe,");
            builder.Append(Csv(measurement.ObservedAt.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(Csv(measurement.Method.ToString())).Append(',');
            builder.Append(Csv(measurement.TargetName)).Append(',');
            builder.Append(measurement.Succeeded ? "true" : "false").Append(',');
            builder.Append(Csv(measurement.LatencyMilliseconds?.ToString("0.###", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(Csv(measurement.FailureCategory)).Append(',');
            builder.Append(Csv(measurement.FailureMessage)).AppendLine();
        }

        foreach (var speed in speedTests)
        {
            builder.Append("speed,");
            builder.Append(Csv(speed.ObservedAt.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(Csv(speed.Direction)).Append(',');
            builder.Append(Csv(speed.Provider)).Append(',');
            builder.Append(speed.Succeeded ? "true" : "false").Append(',');
            builder.Append(',');
            builder.Append(Csv(speed.MegabitsPerSecond?.ToString("0.###", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(speed.BytesTransferred.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Csv(speed.FailureCategory)).Append(',');
            builder.Append(Csv(speed.FailureMessage)).AppendLine();
        }

        await File.WriteAllTextAsync(path, builder.ToString(), cancellationToken).ConfigureAwait(false);
    }

    private static string Csv(string? value)
    {
        value ??= "";
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
