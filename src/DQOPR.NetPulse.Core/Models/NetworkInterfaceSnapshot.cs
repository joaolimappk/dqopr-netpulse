namespace DQOPR.NetPulse.Core.Models;

public sealed record NetworkInterfaceSnapshot(
    DateTimeOffset ObservedAt,
    string? InterfaceName,
    string? Description,
    string? Gateway,
    string? LocalAddress,
    bool IsUp,
    string? NetworkType);
