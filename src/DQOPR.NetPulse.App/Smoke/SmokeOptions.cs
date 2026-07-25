using System.IO;

namespace DQOPR.NetPulse.App.Smoke;

public sealed record SmokeOptions(
    bool Enabled,
    string OutputDirectory,
    string? DatabasePath,
    int DurationSeconds)
{
    public static SmokeOptions Parse(IReadOnlyList<string> args)
    {
        var enabled = args.Contains("--ci-smoke", StringComparer.OrdinalIgnoreCase);
        var output = GetValue(args, "--output") ?? Path.Combine(Environment.CurrentDirectory, "smoke-evidence");
        var database = GetValue(args, "--db");
        var duration = int.TryParse(GetValue(args, "--duration-seconds"), out var parsed) ? parsed : 12;

        if (database is null && enabled)
        {
            database = Path.Combine(output, "netpulse-smoke.sqlite3");
        }

        return new SmokeOptions(enabled, output, database, Math.Max(6, duration));
    }

    private static string? GetValue(IReadOnlyList<string> args, string name)
    {
        for (var index = 0; index < args.Count - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
