using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Diagnostics.Statistics;

public static class JitterCalculator
{
    public static IReadOnlyDictionary<LatencySeriesKey, double> MeanAbsoluteDifferenceBySeries(
        IEnumerable<ProbeMeasurement> measurements)
    {
        return measurements
            .Where(measurement => measurement is { Succeeded: true, LatencyMilliseconds: not null })
            .GroupBy(measurement => new LatencySeriesKey(measurement.TargetName, measurement.Method))
            .ToDictionary(group => group.Key, MeanAbsoluteDifference, EqualityComparer<LatencySeriesKey>.Default);
    }

    private static double MeanAbsoluteDifference(IEnumerable<ProbeMeasurement> series)
    {
        var ordered = series.OrderBy(measurement => measurement.ObservedAt).Select(measurement => measurement.LatencyMilliseconds!.Value).ToArray();
        if (ordered.Length < 2)
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
}
