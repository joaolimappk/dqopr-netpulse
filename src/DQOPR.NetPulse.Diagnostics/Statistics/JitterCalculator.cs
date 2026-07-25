using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Diagnostics.Statistics;

public static class JitterCalculator
{
    public const int MinimumSuccessfulSamples = 3;

    public static IReadOnlyDictionary<LatencySeriesKey, double> MeanAbsoluteDifferenceBySeries(
        IEnumerable<ProbeMeasurement> measurements)
    {
        return measurements
            .Where(measurement => measurement is { Succeeded: true, LatencyMilliseconds: not null, Method: ProbeMethod.Icmp })
            .GroupBy(LatencySeriesKey.From)
            .ToDictionary(group => group.Key, MeanAbsoluteDifference, EqualityComparer<LatencySeriesKey>.Default);
    }

    public static IcmpLatencyStatistics CalculateIcmpStatistics(IEnumerable<ProbeMeasurement> measurements, LatencySeriesKey key)
    {
        var series = measurements
            .Where(measurement => LatencySeriesKey.From(measurement) == key)
            .OrderBy(measurement => measurement.ObservedAt)
            .ToArray();
        var successful = series
            .Where(measurement => measurement is { Succeeded: true, LatencyMilliseconds: not null })
            .Select(measurement => measurement.LatencyMilliseconds!.Value)
            .ToArray();
        var failed = series.Length - successful.Length;

        if (successful.Length == 0)
        {
            return new IcmpLatencyStatistics(key, series.Length, 0, failed, null, null, null, null, null, null, true);
        }

        Array.Sort(successful);
        var jitter = successful.Length < MinimumSuccessfulSamples ? null : MeanAbsoluteSuccessiveDifference(series);
        return new IcmpLatencyStatistics(
            key,
            series.Length,
            successful.Length,
            failed,
            successful.First(),
            Percentile(successful, 50),
            successful.Average(),
            Percentile(successful, 95),
            successful.Last(),
            jitter,
            successful.Length < MinimumSuccessfulSamples);
    }

    private static double MeanAbsoluteDifference(IEnumerable<ProbeMeasurement> series)
    {
        var ordered = series.OrderBy(measurement => measurement.ObservedAt).Select(measurement => measurement.LatencyMilliseconds!.Value).ToArray();
        if (ordered.Length < MinimumSuccessfulSamples)
        {
            return double.NaN;
        }

        var totalDelta = 0.0;
        for (var i = 1; i < ordered.Length; i++)
        {
            totalDelta += Math.Abs(ordered[i] - ordered[i - 1]);
        }

        return totalDelta / (ordered.Length - 1);
    }

    private static double? MeanAbsoluteSuccessiveDifference(IEnumerable<ProbeMeasurement> series)
    {
        var ordered = series
            .Where(measurement => measurement is { Succeeded: true, LatencyMilliseconds: not null })
            .OrderBy(measurement => measurement.ObservedAt)
            .Select(measurement => measurement.LatencyMilliseconds!.Value)
            .ToArray();
        if (ordered.Length < MinimumSuccessfulSamples)
        {
            return null;
        }

        var totalDelta = 0.0;
        for (var index = 1; index < ordered.Length; index++)
        {
            totalDelta += Math.Abs(ordered[index] - ordered[index - 1]);
        }

        return totalDelta / (ordered.Length - 1);
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return double.NaN;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var position = (sortedValues.Count - 1) * percentile / 100.0;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var weight = position - lower;
        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }
}

public sealed record IcmpLatencyStatistics(
    LatencySeriesKey Key,
    int SampleCount,
    int SuccessfulSampleCount,
    int FailedSampleCount,
    double? MinimumRttMilliseconds,
    double? MedianRttMilliseconds,
    double? MeanRttMilliseconds,
    double? P95RttMilliseconds,
    double? MaximumRttMilliseconds,
    double? MeanAbsoluteSuccessiveDifferenceMilliseconds,
    bool InsufficientSamples);
