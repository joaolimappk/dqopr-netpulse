namespace DQOPR.NetPulse.Networking.Probes;

public sealed record NetworkProbeOptions
{
    public TimeSpan WarmupDuration { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MinimumMeasurementDuration { get; init; } = TimeSpan.FromSeconds(8);

    public TimeSpan TargetMeasurementDuration { get; init; } = TimeSpan.FromSeconds(12);

    public int ParallelStreamCount { get; init; } = 4;

    public int DownloadBufferSize { get; init; } = 128 * 1024;

    public int UploadBufferSize { get; init; } = 64 * 1024;

    public int UploadPayloadBytes { get; init; } = 4 * 1024 * 1024;

    public double MaximumCredibleMegabitsPerSecond { get; init; } = 1_000;

    public NetworkProbeOptions Validate()
    {
        if (WarmupDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(WarmupDuration), "Warmup duration cannot be negative.");
        }

        if (MinimumMeasurementDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumMeasurementDuration), "Minimum measurement duration must be positive.");
        }

        if (TargetMeasurementDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(TargetMeasurementDuration), "Target measurement duration must be positive.");
        }

        if (ParallelStreamCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ParallelStreamCount), "At least one stream is required.");
        }

        if (DownloadBufferSize <= 0 || UploadBufferSize <= 0 || UploadPayloadBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(DownloadBufferSize), "Buffer and payload sizes must be positive.");
        }

        if (MaximumCredibleMegabitsPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumCredibleMegabitsPerSecond), "Maximum credible throughput must be positive.");
        }

        return this;
    }
}
