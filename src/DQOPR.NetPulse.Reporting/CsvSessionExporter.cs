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
        builder.AppendLine("type,observed_at_utc,observed_at_local,timezone,method_or_direction,target_or_provider,target_host,address_family,probe_stream_id,sequence,succeeded,latency_ms,mbps,bytes_transferred,result_status,active_duration_ms,setup_duration_ms,transfer_duration_ms,warmup_duration_ms,parallel_stream_count,http_version,methodology_version,endpoint,failure_category,failure_message");

        foreach (var measurement in measurements)
        {
            var local = measurement.ObservedAt.ToLocalTime();
            builder.Append("probe,");
            builder.Append(Csv(measurement.ObservedAt.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(Csv(local.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(Csv(local.Offset.ToString())).Append(',');
            builder.Append(Csv(measurement.Method.ToString())).Append(',');
            builder.Append(Csv(measurement.TargetName)).Append(',');
            builder.Append(Csv(measurement.TargetHost)).Append(',');
            builder.Append(Csv(measurement.AddressFamily)).Append(',');
            builder.Append(Csv(measurement.ProbeStreamId)).Append(',');
            builder.Append(Csv(measurement.Sequence?.ToString(CultureInfo.InvariantCulture))).Append(',');
            builder.Append(measurement.Succeeded ? "true" : "false").Append(',');
            builder.Append(Csv(measurement.LatencyMilliseconds?.ToString("0.###", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(Csv(measurement.MethodologyVersion)).Append(',');
            builder.Append(',');
            builder.Append(Csv(measurement.FailureCategory)).Append(',');
            builder.Append(Csv(measurement.FailureMessage)).AppendLine();
        }

        foreach (var speed in speedTests)
        {
            var local = speed.ObservedAt.ToLocalTime();
            builder.Append("speed,");
            builder.Append(Csv(speed.ObservedAt.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(Csv(local.ToString("O", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(Csv(local.Offset.ToString())).Append(',');
            builder.Append(Csv(speed.Direction)).Append(',');
            builder.Append(Csv(speed.Provider)).Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(',');
            builder.Append(speed.Succeeded ? "true" : "false").Append(',');
            builder.Append(',');
            builder.Append(Csv(speed.MegabitsPerSecond?.ToString("0.###", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(speed.BytesTransferred.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Csv(speed.ResultStatus)).Append(',');
            builder.Append(Csv(speed.ActiveDuration.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(Csv(speed.SetupDuration?.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(Csv(speed.TransferDuration?.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(Csv(speed.WarmupDuration?.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture))).Append(',');
            builder.Append(speed.ParallelStreamCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            builder.Append(Csv(speed.HttpVersion)).Append(',');
            builder.Append(Csv(speed.MethodologyVersion)).Append(',');
            builder.Append(Csv(speed.Endpoint)).Append(',');
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
