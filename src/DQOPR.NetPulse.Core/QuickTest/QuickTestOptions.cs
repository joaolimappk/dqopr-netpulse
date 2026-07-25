namespace DQOPR.NetPulse.Core.QuickTest;

public sealed record QuickTestOptions
{
    public int ProbeBurstCount { get; init; } = 12;

    public TimeSpan ProbeSpacing { get; init; } = TimeSpan.FromMilliseconds(250);

    public bool IncludeDownloadEstimate { get; init; } = true;

    public bool IncludeUploadEstimate { get; init; } = true;

    public void Validate()
    {
        if (ProbeBurstCount is < 10 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(ProbeBurstCount), "Quick Test must use 10 to 20 probes.");
        }

        if (ProbeSpacing < TimeSpan.FromMilliseconds(100))
        {
            throw new ArgumentOutOfRangeException(nameof(ProbeSpacing), "Probe spacing must leave room for network recovery.");
        }
    }
}
