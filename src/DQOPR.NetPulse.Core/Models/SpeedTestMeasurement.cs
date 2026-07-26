namespace DQOPR.NetPulse.Core.Models;

public sealed record SpeedTestMeasurement(
    Guid SessionId,
    DateTimeOffset ObservedAt,
    string Direction,
    bool Succeeded,
    double? MegabitsPerSecond,
    long BytesTransferred,
    TimeSpan ActiveDuration,
    string Provider,
    string? Endpoint,
    string? FailureCategory,
    string? FailureMessage,
    string ResultStatus = SpeedResultStatus.Valid,
    TimeSpan? SetupDuration = null,
    TimeSpan? TransferDuration = null,
    TimeSpan? WarmupDuration = null,
    int ParallelStreamCount = 1,
    string? HttpVersion = null,
    string MethodologyVersion = MeasurementMethodology.CurrentVersion,
    string? DiagnosticJson = null);

public static class SpeedResultStatus
{
    public const string Valid = "Valid";

    public const string Degraded = "Degraded";

    public const string EndpointLimited = "Endpoint limited";

    public const string InsufficientDuration = "Insufficient duration";

    public const string UploadEndpointUnavailable = "Upload endpoint unavailable";

    public const string TestCanceled = "Test canceled";

    public const string InvalidResult = "Invalid result";

    public const string MeasurementAccountingInconsistency = "Invalid result - measurement accounting inconsistency";

    public const string LegacyEstimate = "Legacy estimate - methodology version prior to alpha.4";
}
