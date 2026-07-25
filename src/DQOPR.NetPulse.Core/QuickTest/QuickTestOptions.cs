namespace DQOPR.NetPulse.Core.QuickTest;

public sealed record QuickTestOptions
{
    public int ProbeBurstCount { get; init; } = 20;

    public TimeSpan ProbeSpacing { get; init; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan ProbeTimeout { get; init; } = TimeSpan.FromSeconds(1);

    public bool IncludeDownloadEstimate { get; init; } = true;

    public bool IncludeUploadEstimate { get; init; } = true;

    public void Validate()
    {
        if (ProbeBurstCount is < 20 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(ProbeBurstCount), "Quick Test must use at least 20 probes.");
        }

        if (ProbeSpacing < TimeSpan.FromMilliseconds(100))
        {
            throw new ArgumentOutOfRangeException(nameof(ProbeSpacing), "Probe spacing must leave room for network recovery.");
        }

        if (ProbeTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ProbeTimeout), "Probe timeout must be positive.");
        }
    }
}
