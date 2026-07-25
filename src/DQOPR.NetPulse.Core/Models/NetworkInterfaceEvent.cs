namespace DQOPR.NetPulse.Core.Models;

public sealed record NetworkInterfaceEvent(
    Guid SessionId,
    DateTimeOffset ObservedAt,
    string EventType,
    string? InterfaceName,
    string? Gateway,
    string? Details);
