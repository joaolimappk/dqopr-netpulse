namespace DQOPR.NetPulse.Core.Models;

[Flags]
public enum TargetPurpose
{
    None = 0,
    LocalGateway = 1,
    ExternalIcmp = 2,
    TcpConnect = 4,
    DnsResolution = 8,
    Https = 16,
    SpeedTest = 32,
    PublicIp = 64
}
