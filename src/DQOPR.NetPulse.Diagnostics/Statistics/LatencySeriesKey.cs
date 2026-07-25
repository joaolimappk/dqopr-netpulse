using DQOPR.NetPulse.Core.Models;

namespace DQOPR.NetPulse.Diagnostics.Statistics;

public sealed record LatencySeriesKey(string TargetName, ProbeMethod Method);
