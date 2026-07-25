using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Core.Monitoring;

public sealed record MonitoringTargets(
    IReadOnlyList<TargetDefinition> IcmpTargets,
    IReadOnlyList<TargetDefinition> TcpTargets,
    string DnsHostname,
    Uri HttpsUri,
    Uri DownloadUri,
    Uri UploadUri)
{
    public static MonitoringTargets Defaults { get; } = new(
        IcmpTargets:
        [
            new("Cloudflare", TargetPurpose.ExternalIcmp, "1.1.1.1"),
            new("Google", TargetPurpose.ExternalIcmp, "8.8.8.8"),
            new("Quad9", TargetPurpose.ExternalIcmp, "9.9.9.9")
        ],
        TcpTargets:
        [
            new("Cloudflare HTTPS", TargetPurpose.TcpConnect, "cloudflare.com", 443),
            new("Google HTTPS", TargetPurpose.TcpConnect, "google.com", 443)
        ],
        DnsHostname: "example.com",
        HttpsUri: new Uri("https://www.example.com/"),
        DownloadUri: new Uri("https://speed.cloudflare.com/__down?bytes=500000000"),
        UploadUri: new Uri("https://speed.cloudflare.com/__up"));
}
