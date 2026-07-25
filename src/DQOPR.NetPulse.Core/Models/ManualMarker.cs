namespace DQOPR.NetPulse.Core.Models;

public sealed record ManualMarker(
    Guid Id,
    Guid SessionId,
    DateTimeOffset ObservedAt,
    string Note);
