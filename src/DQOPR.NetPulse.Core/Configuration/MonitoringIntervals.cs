namespace DQOPR.NetPulse.Core.Configuration;

public sealed record MonitoringIntervals(
    TimeSpan Icmp,
    TimeSpan TcpConnect,
    TimeSpan Dns,
    TimeSpan Https,
    TimeSpan InterfaceSnapshot,
    TimeSpan RouteSnapshot,
    TimeSpan PublicIp,
    TimeSpan SpeedTest)
{
    public static MonitoringIntervals EvidenceDefaults { get; } = new(
        Icmp: TimeSpan.FromSeconds(2),
        TcpConnect: TimeSpan.FromSeconds(10),
        Dns: TimeSpan.FromSeconds(15),
        Https: TimeSpan.FromSeconds(30),
        InterfaceSnapshot: TimeSpan.FromSeconds(30),
        RouteSnapshot: TimeSpan.FromMinutes(15),
        PublicIp: TimeSpan.FromMinutes(5),
        SpeedTest: TimeSpan.FromMinutes(30));
}
