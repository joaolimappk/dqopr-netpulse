namespace DQOPR.NetPulse.Core.Models;

public enum ProbeMethod
{
    Icmp,
    TcpConnect,
    Dns,
    Https,
    RouteSnapshot,
    InterfaceSnapshot,
    PublicIp,
    SpeedTestDownload,
    SpeedTestUpload
}
