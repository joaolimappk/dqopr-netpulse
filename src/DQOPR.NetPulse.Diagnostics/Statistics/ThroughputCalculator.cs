using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Diagnostics.Statistics;

public static class ThroughputCalculator
{
    public static double? MegabitsPerSecond(long bytesTransferred, TimeSpan transferDuration)
        => bytesTransferred > 0 && transferDuration > TimeSpan.Zero
            ? bytesTransferred * 8.0 / transferDuration.TotalSeconds / 1_000_000.0
            : null;

    public static string Classify(bool anySucceeded, bool anyFailed, TimeSpan transferDuration, TimeSpan minimumDuration, bool uploadEndpointUnavailable = false, bool canceled = false)
    {
        if (canceled)
        {
            return SpeedResultStatus.TestCanceled;
        }

        if (!anySucceeded)
        {
            return uploadEndpointUnavailable ? SpeedResultStatus.UploadEndpointUnavailable : SpeedResultStatus.InvalidResult;
        }

        if (transferDuration < minimumDuration)
        {
            return SpeedResultStatus.InsufficientDuration;
        }

        return anyFailed ? SpeedResultStatus.Degraded : SpeedResultStatus.Valid;
    }
}
