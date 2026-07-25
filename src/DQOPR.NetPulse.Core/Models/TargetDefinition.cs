namespace DQOPR.NetPulse.Core.Models;

public sealed record TargetDefinition(
    string Name,
    TargetPurpose Purpose,
    string Host,
    int? Port = null,
    Uri? Uri = null)
{
    public bool Supports(TargetPurpose purpose) => Purpose.HasFlag(purpose);
}
