using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Core.Monitoring;
using DQOPR.NetPulse.Diagnostics.Statistics;

namespace DQOPR.NetPulse.Networking.Probes;

public sealed class NetworkProbeService(HttpClient? httpClient = null) : INetworkProbeService, IDisposable
{
    private const string ProviderName = "NetPulse built-in estimate";
    private static readonly TimeSpan WarmupDuration = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinimumMeasurementDuration = TimeSpan.FromSeconds(8);
    private const int ParallelStreamCount = 4;
    private const int DownloadBufferSize = 128 * 1024;
    private const int UploadPayloadBytes = 4 * 1024 * 1024;
    private readonly HttpClient httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    private readonly bool ownsHttpClient = httpClient is null;

    public async Task<ProbeMeasurement> ProbeIcmpAsync(Guid sessionId, TargetDefinition target, int sequence, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target.Host, (int)timeout.TotalMilliseconds).WaitAsync(cancellationToken).ConfigureAwait(false);
            var addressFamily = ResolveAddressFamily(target.Host);
            var streamId = ProbeStreamId(sessionId, target, addressFamily);
            return reply.Status == IPStatus.Success
                ? new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Icmp, target.Name, true, reply.RoundtripTime, TargetHost: target.Host, AddressFamily: addressFamily, ProbeStreamId: streamId, Sequence: sequence)
                : new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Icmp, target.Name, false, null, reply.Status.ToString(), $"ICMP sequence {sequence} failed with {reply.Status}.", target.Host, addressFamily, streamId, sequence);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is PingException or SocketException or InvalidOperationException)
        {
            var addressFamily = ResolveAddressFamily(target.Host);
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Icmp, target.Name, false, null, ex.GetType().Name, $"ICMP sequence {sequence} failed: {ex.Message}", target.Host, addressFamily, ProbeStreamId(sessionId, target, addressFamily), sequence);
        }
    }

    public async Task<ProbeMeasurement> ProbeTcpAsync(Guid sessionId, TargetDefinition target, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var port = target.Port ?? 443;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            using var client = new TcpClient();
            await client.ConnectAsync(target.Host, port, timeoutSource.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.TcpConnect, target.Name, true, stopwatch.Elapsed.TotalMilliseconds, TargetHost: target.Host, AddressFamily: ResolveAddressFamily(target.Host), ProbeStreamId: $"{sessionId}:tcp:{target.Name}:{target.Host}:{port}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.TcpConnect, target.Name, false, null, "Timeout", $"TCP connection to {target.Host}:{port} timed out.", target.Host, ResolveAddressFamily(target.Host), $"{sessionId}:tcp:{target.Name}:{target.Host}:{port}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SocketException ex)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.TcpConnect, target.Name, false, null, ex.SocketErrorCode.ToString(), ex.Message, target.Host, ResolveAddressFamily(target.Host), $"{sessionId}:tcp:{target.Name}:{target.Host}:{port}");
        }
    }

    public async Task<ProbeMeasurement> ProbeDnsAsync(Guid sessionId, string hostname, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            var addresses = await Dns.GetHostAddressesAsync(hostname, timeoutSource.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Dns, hostname, addresses.Length > 0, stopwatch.Elapsed.TotalMilliseconds, addresses.Length == 0 ? "NoAddresses" : null, addresses.Length == 0 ? "DNS returned no addresses." : $"Resolved {addresses.Length} address(es).", hostname, "resolver", $"{sessionId}:dns:{hostname}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Dns, hostname, false, null, "Timeout", "DNS resolution timed out.", hostname, "resolver", $"{sessionId}:dns:{hostname}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SocketException ex)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Dns, hostname, false, null, ex.SocketErrorCode.ToString(), ex.Message, hostname, "resolver", $"{sessionId}:dns:{hostname}");
        }
    }

    public async Task<ProbeMeasurement> ProbeHttpsAsync(Guid sessionId, Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
            stopwatch.Stop();
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Https, uri.Host, response.IsSuccessStatusCode, stopwatch.Elapsed.TotalMilliseconds, response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}", response.ReasonPhrase, uri.Host, ResolveAddressFamily(uri.Host), $"{sessionId}:https:{uri.Host}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Https, uri.Host, false, null, "Timeout", "HTTPS request timed out.", uri.Host, ResolveAddressFamily(uri.Host), $"{sessionId}:https:{uri.Host}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Https, uri.Host, false, null, ex.HttpRequestError.ToString(), ex.Message, uri.Host, ResolveAddressFamily(uri.Host), $"{sessionId}:https:{uri.Host}");
        }
    }

    public async Task<IReadOnlyList<SpeedTestMeasurement>> RunSpeedTestAsync(Guid sessionId, Uri downloadUri, Uri uploadUri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var results = new List<SpeedTestMeasurement>
        {
            await RunDownloadAsync(sessionId, downloadUri, timeout, cancellationToken).ConfigureAwait(false),
            await RunUploadAsync(sessionId, uploadUri, timeout, cancellationToken).ConfigureAwait(false)
        };
        return results;
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    private async Task<SpeedTestMeasurement> RunDownloadAsync(Guid sessionId, Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var budget = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(45) : timeout;
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(budget);
            var warmup = await DownloadStreamAsync(uri, WarmupDuration, 0, timeoutSource.Token).ConfigureAwait(false);
            var measurementDuration = MeasurementDuration(budget, warmup.Elapsed);
            var streams = await Task.WhenAll(Enumerable.Range(0, ParallelStreamCount)
                .Select(index => DownloadStreamAsync(uri, measurementDuration, index + 1, timeoutSource.Token))).ConfigureAwait(false);

            var failed = streams.Where(stream => !stream.Succeeded).ToArray();
            if (failed.Length == streams.Length)
            {
                return FailedSpeed(sessionId, observedAt, "download", budget, uri, SpeedResultStatus.InvalidResult, failed[0].FailureCategory ?? "AllStreamsFailed", failed[0].FailureMessage ?? "Every download stream failed.", warmup, streams);
            }

            var bytes = streams.Where(stream => stream.Succeeded).Sum(stream => stream.BytesTransferred);
            var transferDuration = streams.Where(stream => stream.Succeeded).Max(stream => stream.TransferDuration);
            var setupDuration = TimeSpan.FromMilliseconds(streams.Where(stream => stream.Succeeded).Average(stream => stream.SetupDuration.TotalMilliseconds));
            var status = ThroughputCalculator.Classify(anySucceeded: true, anyFailed: failed.Length > 0, transferDuration, MinimumMeasurementDuration);
            return SpeedResult(sessionId, observedAt, "download", status == SpeedResultStatus.Valid || status == SpeedResultStatus.Degraded, bytes, setupDuration, transferDuration, WarmupDuration, uri, status, null, null, streams);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailedSpeed(sessionId, observedAt, "download", budget, uri, SpeedResultStatus.TestCanceled, "Timeout", "Download throughput estimate timed out.", null, []);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return FailedSpeed(sessionId, observedAt, "download", budget, uri, SpeedResultStatus.InvalidResult, ex.GetType().Name, ex.Message, null, []);
        }
    }

    private async Task<SpeedTestMeasurement> RunUploadAsync(Guid sessionId, Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var budget = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(45) : timeout;
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(budget);
            var payload = RandomPayload(UploadPayloadBytes);
            var warmup = await UploadStreamAsync(uri, payload, WarmupDuration, 0, timeoutSource.Token).ConfigureAwait(false);
            var measurementDuration = MeasurementDuration(budget, warmup.Elapsed);
            var streams = await Task.WhenAll(Enumerable.Range(0, ParallelStreamCount)
                .Select(index => UploadStreamAsync(uri, payload, measurementDuration, index + 1, timeoutSource.Token))).ConfigureAwait(false);

            var failed = streams.Where(stream => !stream.Succeeded).ToArray();
            if (failed.Length == streams.Length)
            {
                return FailedSpeed(sessionId, observedAt, "upload", budget, uri, SpeedResultStatus.UploadEndpointUnavailable, failed[0].FailureCategory ?? "AllStreamsFailed", failed[0].FailureMessage ?? "Every upload stream failed.", warmup, streams);
            }

            var bytes = streams.Where(stream => stream.Succeeded).Sum(stream => stream.BytesTransferred);
            var transferDuration = streams.Where(stream => stream.Succeeded).Max(stream => stream.TransferDuration);
            var setupDuration = TimeSpan.FromMilliseconds(streams.Where(stream => stream.Succeeded).Average(stream => stream.SetupDuration.TotalMilliseconds));
            var status = ThroughputCalculator.Classify(anySucceeded: true, anyFailed: failed.Length > 0, transferDuration, MinimumMeasurementDuration, uploadEndpointUnavailable: true);
            return SpeedResult(sessionId, observedAt, "upload", status == SpeedResultStatus.Valid || status == SpeedResultStatus.Degraded, bytes, setupDuration, transferDuration, WarmupDuration, uri, status, null, null, streams);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailedSpeed(sessionId, observedAt, "upload", budget, uri, SpeedResultStatus.TestCanceled, "Timeout", "Upload throughput estimate timed out.", null, []);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return FailedSpeed(sessionId, observedAt, "upload", budget, uri, SpeedResultStatus.UploadEndpointUnavailable, ex.GetType().Name, ex.Message, null, []);
        }
    }

    private async Task<ThroughputStreamResult> DownloadStreamAsync(Uri uri, TimeSpan duration, int streamIndex, CancellationToken cancellationToken)
    {
        var endpoint = CacheBusted(uri, streamIndex);
        var startedAt = DateTimeOffset.UtcNow;
        var setup = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint) { VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            request.Headers.AcceptEncoding.Clear();
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            setup.Stop();
            if (!response.IsSuccessStatusCode)
            {
                return ThroughputStreamResult.Failed(streamIndex, startedAt, DateTimeOffset.UtcNow, setup.Elapsed, $"HTTP {(int)response.StatusCode}", response.ReasonPhrase ?? "Download endpoint returned an invalid status.", response.Version.ToString());
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[DownloadBufferSize];
            long bytes = 0;
            var transfer = Stopwatch.StartNew();
            while (transfer.Elapsed < duration)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                bytes += read;
            }

            transfer.Stop();
            return ThroughputStreamResult.Success(streamIndex, startedAt, DateTimeOffset.UtcNow, bytes, setup.Elapsed, transfer.Elapsed, response.Version.ToString());
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            setup.Stop();
            return ThroughputStreamResult.Failed(streamIndex, startedAt, DateTimeOffset.UtcNow, setup.Elapsed, ex.GetType().Name, ex.Message, null);
        }
    }

    private async Task<ThroughputStreamResult> UploadStreamAsync(Uri uri, byte[] payload, TimeSpan duration, int streamIndex, CancellationToken cancellationToken)
    {
        long bytes = 0;
        var setup = TimeSpan.Zero;
        var startedAt = DateTimeOffset.UtcNow;
        var transfer = Stopwatch.StartNew();
        try
        {
            while (transfer.Elapsed < duration)
            {
                var endpoint = CacheBusted(uri, streamIndex);
                var setupWatch = Stopwatch.StartNew();
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                request.Content = new ByteArrayContent(payload);
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                setupWatch.Stop();
                setup += setupWatch.Elapsed;
                if (!response.IsSuccessStatusCode)
                {
                    return ThroughputStreamResult.Failed(streamIndex, startedAt, DateTimeOffset.UtcNow, setup, $"HTTP {(int)response.StatusCode}", response.ReasonPhrase ?? "Upload endpoint rejected the payload.", response.Version.ToString());
                }

                bytes += payload.LongLength;
            }

            transfer.Stop();
            return ThroughputStreamResult.Success(streamIndex, startedAt, DateTimeOffset.UtcNow, bytes, setup, transfer.Elapsed, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            transfer.Stop();
            return ThroughputStreamResult.Failed(streamIndex, startedAt, DateTimeOffset.UtcNow, setup, ex.GetType().Name, ex.Message, null);
        }
    }

    private static SpeedTestMeasurement SpeedResult(Guid sessionId, DateTimeOffset observedAt, string direction, bool succeeded, long bytes, TimeSpan setupDuration, TimeSpan transferDuration, TimeSpan warmupDuration, Uri uri, string status, string? category, string? message, IReadOnlyList<ThroughputStreamResult> streams)
    {
        var mbps = succeeded ? ThroughputCalculator.MegabitsPerSecond(bytes, transferDuration) : null;
        var diagnostic = JsonSerializer.Serialize(new
        {
            methodology = MeasurementMethodology.CurrentVersion,
            provider = ProviderName,
            direction,
            endpoint = uri.Host,
            streamCount = ParallelStreamCount,
            bytesTransferred = bytes,
            setupDurationMs = setupDuration.TotalMilliseconds,
            transferDurationMs = transferDuration.TotalMilliseconds,
            warmupDurationMs = warmupDuration.TotalMilliseconds,
            resultStatus = status,
            streams = streams.Select(stream => new
            {
                stream.StreamIndex,
                stream.StartedAt,
                stream.EndedAt,
                stream.Succeeded,
                stream.BytesTransferred,
                setupDurationMs = stream.SetupDuration.TotalMilliseconds,
                transferDurationMs = stream.TransferDuration.TotalMilliseconds,
                stream.HttpVersion,
                stream.FailureCategory,
                stream.FailureMessage
            })
        });
        return new SpeedTestMeasurement(sessionId, observedAt, direction, succeeded, mbps, bytes, transferDuration, ProviderName, uri.ToString(), category, message, status, setupDuration, transferDuration, warmupDuration, ParallelStreamCount, streams.FirstOrDefault(stream => stream.HttpVersion is not null)?.HttpVersion, MeasurementMethodology.CurrentVersion, diagnostic);
    }

    private static SpeedTestMeasurement FailedSpeed(Guid sessionId, DateTimeOffset observedAt, string direction, TimeSpan duration, Uri uri, string status, string category, string message, ThroughputStreamResult? warmup, IReadOnlyList<ThroughputStreamResult> streams)
        => SpeedResult(sessionId, observedAt, direction, false, streams.Where(stream => stream.Succeeded).Sum(stream => stream.BytesTransferred), TimeSpan.Zero, duration, warmup is null ? TimeSpan.Zero : WarmupDuration, uri, status, category, message, streams);

    private static TimeSpan MeasurementDuration(TimeSpan budget, TimeSpan elapsedWarmup)
    {
        var remaining = budget - elapsedWarmup - TimeSpan.FromSeconds(2);
        if (remaining < MinimumMeasurementDuration)
        {
            return remaining > TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
        }

        return remaining > TimeSpan.FromSeconds(12) ? TimeSpan.FromSeconds(12) : remaining;
    }

    private static Uri CacheBusted(Uri uri, int streamIndex)
    {
        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return new Uri($"{uri}{separator}np_cache_bust={Guid.NewGuid():N}&np_stream={streamIndex}");
    }

    private static byte[] RandomPayload(int size)
    {
        var payload = new byte[size];
        Random.Shared.NextBytes(payload);
        return payload;
    }

    private static string ResolveAddressFamily(string host)
    {
        if (!IPAddress.TryParse(host, out var address))
        {
            return "hostname";
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
    }

    private static string ProbeStreamId(Guid sessionId, TargetDefinition target, string addressFamily)
        => $"{sessionId}:icmp:{target.Name}:{target.Host}:{addressFamily}";

    private sealed record ThroughputStreamResult(
        int StreamIndex,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        bool Succeeded,
        long BytesTransferred,
        TimeSpan SetupDuration,
        TimeSpan TransferDuration,
        string? HttpVersion,
        string? FailureCategory,
        string? FailureMessage)
    {
        public TimeSpan Elapsed => SetupDuration + TransferDuration;

        public static ThroughputStreamResult Success(int streamIndex, DateTimeOffset startedAt, DateTimeOffset endedAt, long bytesTransferred, TimeSpan setupDuration, TimeSpan transferDuration, string? httpVersion)
            => new(streamIndex, startedAt, endedAt, true, bytesTransferred, setupDuration, transferDuration, httpVersion, null, null);

        public static ThroughputStreamResult Failed(int streamIndex, DateTimeOffset startedAt, DateTimeOffset endedAt, TimeSpan setupDuration, string failureCategory, string failureMessage, string? httpVersion)
            => new(streamIndex, startedAt, endedAt, false, 0, setupDuration, TimeSpan.Zero, httpVersion, failureCategory, failureMessage);
    }
}
