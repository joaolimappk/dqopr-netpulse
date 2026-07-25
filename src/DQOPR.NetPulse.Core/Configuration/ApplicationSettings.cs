using System.Text.Json;

namespace DQOPR.NetPulse.Core.Configuration;

public sealed record ApplicationSettings
{
    public TimeSpan MonitoringDuration { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan IcmpInterval { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan TcpInterval { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan DnsInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan HttpsInterval { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan SpeedTestInterval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(2);

    public string IcmpTargets { get; set; } = "Cloudflare=1.1.1.1;Google=8.8.8.8;Quad9=9.9.9.9";

    public string DnsHostname { get; set; } = "example.com";

    public string TcpEndpoints { get; set; } = "Cloudflare HTTPS=cloudflare.com:443;Google HTTPS=google.com:443";

    public string HttpsEndpoint { get; set; } = "https://www.example.com/";

    public string DownloadEndpoint { get; set; } = "https://cachefly.cachefly.net/100mb.test";

    public string UploadEndpoint { get; set; } = "https://speed.cloudflare.com/__up";

    public string DatabasePath { get; set; } = "";

    public string ExportDirectory { get; set; } = "";

    public bool StartMinimized { get; set; }

    public bool MinimizeToTray { get; set; }

    public bool ConfirmBeforeStopping { get; set; } = true;

    public string Theme { get; set; } = "System";

    public static ApplicationSettings Defaults(string databasePath, string exportDirectory) => new()
    {
        DatabasePath = databasePath,
        ExportDirectory = exportDirectory
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        CheckPositive(MonitoringDuration, "Monitoring duration", errors);
        CheckPositive(IcmpInterval, "ICMP interval", errors);
        CheckPositive(TcpInterval, "TCP interval", errors);
        CheckPositive(DnsInterval, "DNS interval", errors);
        CheckPositive(HttpsInterval, "HTTPS interval", errors);
        CheckPositive(SpeedTestInterval, "Speed-test interval", errors);
        CheckPositive(ProbeTimeout, "Probe timeout", errors);

        if (string.IsNullOrWhiteSpace(IcmpTargets))
        {
            errors.Add("At least one ICMP target is required.");
        }

        if (string.IsNullOrWhiteSpace(DnsHostname))
        {
            errors.Add("DNS hostname is required.");
        }

        if (!Uri.TryCreate(HttpsEndpoint, UriKind.Absolute, out var httpsUri) || httpsUri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("HTTPS endpoint must be a valid https:// URL.");
        }

        if (!Uri.TryCreate(DownloadEndpoint, UriKind.Absolute, out _))
        {
            errors.Add("Download endpoint must be a valid URL.");
        }

        if (!Uri.TryCreate(UploadEndpoint, UriKind.Absolute, out _))
        {
            errors.Add("Upload endpoint must be a valid URL.");
        }

        if (string.IsNullOrWhiteSpace(DatabasePath))
        {
            errors.Add("Database path is required.");
        }

        if (string.IsNullOrWhiteSpace(ExportDirectory))
        {
            errors.Add("Export directory is required.");
        }

        return errors;
    }

    public MonitoringIntervals ToIntervals() => new(
        Icmp: IcmpInterval,
        TcpConnect: TcpInterval,
        Dns: DnsInterval,
        Https: HttpsInterval,
        InterfaceSnapshot: TimeSpan.FromSeconds(30),
        RouteSnapshot: TimeSpan.FromMinutes(15),
        PublicIp: TimeSpan.FromMinutes(5),
        SpeedTest: SpeedTestInterval);

    private static void CheckPositive(TimeSpan value, string label, ICollection<string> errors)
    {
        if (value <= TimeSpan.Zero)
        {
            errors.Add($"{label} must be greater than zero.");
        }
    }
}

public sealed class ApplicationSettingsStore(string path, ApplicationSettings defaults)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Path { get; } = path;

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(Path))
        {
            return defaults;
        }

        await using var stream = File.OpenRead(Path);
        return await JsonSerializer.DeserializeAsync<ApplicationSettings>(stream, JsonOptions, cancellationToken).ConfigureAwait(false) ?? defaults;
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        var errors = settings.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)) ?? ".");
        await using var stream = File.Create(Path);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken).ConfigureAwait(false);
    }
}
