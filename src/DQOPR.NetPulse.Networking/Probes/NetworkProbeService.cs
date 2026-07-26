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

public sealed class NetworkProbeService : INetworkProbeService, IDisposable
{
    private const string ProviderName = "NetPulse built-in estimate";
    private const string UserAgent = "DQOPR-NetPulse/0.3 throughput-estimate";
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly NetworkProbeOptions options;

    public NetworkProbeService(HttpClient? httpClient = null, NetworkProbeOptions? options = null)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        ownsHttpClient = httpClient is null;
        this.options = (options ?? new NetworkProbeOptions()).Validate();
    }

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
            var warmup = options.WarmupDuration == TimeSpan.Zero
                ? ThroughputStreamResult.Success(0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, TimeSpan.Zero, TimeSpan.Zero, null, TimeSpan.Zero, TimeSpan.Zero, 0, [])
                : await DownloadWarmupAsync(uri, timeoutSource.Token).ConfigureAwait(false);
            var measurementDuration = MeasurementDuration(budget, warmup.Elapsed);
            var result = await RunGlobalWindowAsync(
                "download",
                uri,
                measurementDuration,
                index => DownloadWorkerAsync(uri, index, timeoutSource.Token),
                timeoutSource.Token).ConfigureAwait(false);

            return SpeedResult(sessionId, observedAt, "download", uri, result, warmup);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailedSpeed(sessionId, observedAt, "download", budget, uri, SpeedResultStatus.TestCanceled, "Timeout", "Download throughput estimate timed out.", null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return FailedSpeed(sessionId, observedAt, "download", budget, uri, SpeedResultStatus.InvalidResult, ex.GetType().Name, ex.Message, null, null);
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
            var payload = RandomPayload(options.UploadPayloadBytes);
            var warmup = options.WarmupDuration == TimeSpan.Zero
                ? ThroughputStreamResult.Success(0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, TimeSpan.Zero, TimeSpan.Zero, null, TimeSpan.Zero, TimeSpan.Zero, 0, [])
                : await UploadWarmupAsync(uri, payload, timeoutSource.Token).ConfigureAwait(false);
            var measurementDuration = MeasurementDuration(budget, warmup.Elapsed);
            var result = await RunGlobalWindowAsync(
                "upload",
                uri,
                measurementDuration,
                index => UploadWorkerAsync(uri, payload, index, timeoutSource.Token),
                timeoutSource.Token).ConfigureAwait(false);

            return SpeedResult(sessionId, observedAt, "upload", uri, result, warmup);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailedSpeed(sessionId, observedAt, "upload", budget, uri, SpeedResultStatus.TestCanceled, "Timeout", "Upload throughput estimate timed out.", null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return FailedSpeed(sessionId, observedAt, "upload", budget, uri, SpeedResultStatus.UploadEndpointUnavailable, ex.GetType().Name, ex.Message, null, null);
        }
    }

    private async Task<ThroughputWindowResult> RunGlobalWindowAsync(
        string direction,
        Uri uri,
        TimeSpan duration,
        Func<int, Task<ThroughputStreamResult>> workerFactory,
        CancellationToken cancellationToken)
    {
        var window = new ThroughputWindow(duration);
        using var windowScope = window.Enter();
        var ready = 0;
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(1, options.ParallelStreamCount)
            .Select(index => WorkerAfterGateAsync(index))
            .ToArray();

        while (Volatile.Read(ref ready) < options.ParallelStreamCount)
        {
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }

        window.Start();
        startGate.SetResult();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        window.Stop();

        var streams = tasks.Select(task => task.Result).ToArray();
        var bytes = streams.Sum(stream => stream.BytesTransferred);
        var setupDuration = streams.Length == 0
            ? TimeSpan.Zero
            : TimeSpan.FromMilliseconds(streams.Average(stream => stream.SetupDuration.TotalMilliseconds));
        var failure = ValidateThroughput(direction, bytes, window.Elapsed, streams);
        var confidence = EvaluateConfidence(direction, bytes, window.Elapsed, streams);
        var failed = streams.Where(stream => !stream.Succeeded).ToArray();
        var status = failure is not null
            ? SpeedResultStatus.MeasurementAccountingInconsistency
            : confidence.SuspectedEndpointLimitation
                ? SpeedResultStatus.DegradedUploadEndpointMayBeLimiting
            : ThroughputCalculator.Classify(anySucceeded: bytes > 0, anyFailed: failed.Length > 0, window.Elapsed, options.MinimumMeasurementDuration, uploadEndpointUnavailable: direction == "upload" && bytes == 0);

        return new ThroughputWindowResult(
            window.StartedAtUtc,
            window.EndedAtUtc,
            window.Elapsed,
            bytes,
            setupDuration,
            status,
            failure?.Category ?? (confidence.SuspectedEndpointLimitation ? "UploadEndpointMayBeLimitingThroughput" : failed.FirstOrDefault()?.FailureCategory),
            failure?.Message ?? (confidence.SuspectedEndpointLimitation ? "Degraded - upload endpoint may be limiting throughput." : failed.FirstOrDefault()?.FailureMessage),
            confidence,
            streams);

        async Task<ThroughputStreamResult> WorkerAfterGateAsync(int index)
        {
            Interlocked.Increment(ref ready);
            await startGate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return await workerFactory(index).ConfigureAwait(false);
        }
    }

    private async Task<ThroughputStreamResult> DownloadWarmupAsync(Uri uri, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = BuildDownloadRequest(uri, 0, 0);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[options.DownloadBufferSize];
            while (stopwatch.Elapsed < options.WarmupDuration)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
            }

            stopwatch.Stop();
            var headers = ResponseEvidence.From(response, stopwatch.Elapsed);
            return response.IsSuccessStatusCode
                ? ThroughputStreamResult.Success(0, startedAt, DateTimeOffset.UtcNow, 0, stopwatch.Elapsed, TimeSpan.Zero, response.Version.ToString(), TimeSpan.Zero, stopwatch.Elapsed, 1, [headers])
                : ThroughputStreamResult.Failed(0, startedAt, DateTimeOffset.UtcNow, 0, stopwatch.Elapsed, TimeSpan.Zero, $"HTTP {(int)response.StatusCode}", response.ReasonPhrase ?? "Download warmup failed.", response.Version.ToString(), TimeSpan.Zero, stopwatch.Elapsed, 1, [headers]);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            stopwatch.Stop();
            return ThroughputStreamResult.Failed(0, startedAt, DateTimeOffset.UtcNow, 0, stopwatch.Elapsed, TimeSpan.Zero, ex.GetType().Name, ex.Message, null, TimeSpan.Zero, stopwatch.Elapsed, 1, []);
        }
    }

    private async Task<ThroughputStreamResult> DownloadWorkerAsync(Uri uri, int streamIndex, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var setupDuration = TimeSpan.Zero;
        var startedOffset = ThroughputWindow.Current!.Elapsed;
        var stoppedOffset = startedOffset;
        long bytes = 0;
        var requestIndex = 0;
        string? httpVersion = null;
        var responses = new List<ResponseEvidence>();
        try
        {
            while (!ThroughputWindow.Current.IsExpired && !cancellationToken.IsCancellationRequested)
            {
                requestIndex++;
                var setup = Stopwatch.StartNew();
                using var request = BuildDownloadRequest(uri, streamIndex, requestIndex);
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ThroughputWindow.Current.CancellationToken).ConfigureAwait(false);
                setup.Stop();
                setupDuration += setup.Elapsed;
                httpVersion = response.Version.ToString();
                responses.Add(ResponseEvidence.From(response, setup.Elapsed));
                if (!response.IsSuccessStatusCode)
                {
                    stoppedOffset = ThroughputWindow.Current.Elapsed;
                    return ThroughputStreamResult.Failed(streamIndex, startedAt, DateTimeOffset.UtcNow, bytes, setupDuration, stoppedOffset - startedOffset, $"HTTP {(int)response.StatusCode}", response.ReasonPhrase ?? "Download endpoint returned an invalid status.", httpVersion, startedOffset, stoppedOffset, requestIndex, responses);
                }

                await using var stream = await response.Content.ReadAsStreamAsync(ThroughputWindow.Current.CancellationToken).ConfigureAwait(false);
                var buffer = new byte[options.DownloadBufferSize];
                while (!ThroughputWindow.Current.IsExpired)
                {
                    var read = await stream.ReadAsync(buffer, ThroughputWindow.Current.CancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    if (!ThroughputWindow.Current.IsExpired)
                    {
                        bytes += read;
                    }
                }
            }

            stoppedOffset = ThroughputWindow.Current.Elapsed;
            return ThroughputStreamResult.Success(streamIndex, startedAt, DateTimeOffset.UtcNow, bytes, setupDuration, stoppedOffset - startedOffset, httpVersion, startedOffset, stoppedOffset, requestIndex, responses);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            stoppedOffset = ThroughputWindow.Current?.Elapsed ?? stoppedOffset;
            return bytes > 0 && ThroughputWindow.Current?.IsExpired == true
                ? ThroughputStreamResult.Success(streamIndex, startedAt, DateTimeOffset.UtcNow, bytes, setupDuration, stoppedOffset - startedOffset, httpVersion, startedOffset, stoppedOffset, requestIndex, responses)
                : ThroughputStreamResult.Failed(streamIndex, startedAt, DateTimeOffset.UtcNow, bytes, setupDuration, stoppedOffset - startedOffset, ex.GetType().Name, ex.Message, httpVersion, startedOffset, stoppedOffset, requestIndex, responses);
        }
    }

    private async Task<ThroughputStreamResult> UploadWarmupAsync(Uri uri, byte[] payload, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var setup = Stopwatch.StartNew();
        try
        {
            using var request = BuildUploadRequest(uri, payload, 0, 0, null, null);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            setup.Stop();
            var headers = ResponseEvidence.From(response, setup.Elapsed);
            return response.IsSuccessStatusCode
                ? ThroughputStreamResult.Success(0, startedAt, DateTimeOffset.UtcNow, 0, setup.Elapsed, TimeSpan.Zero, response.Version.ToString(), TimeSpan.Zero, setup.Elapsed, 1, [headers])
                : ThroughputStreamResult.Failed(0, startedAt, DateTimeOffset.UtcNow, 0, setup.Elapsed, TimeSpan.Zero, $"HTTP {(int)response.StatusCode}", response.ReasonPhrase ?? "Upload warmup failed.", response.Version.ToString(), TimeSpan.Zero, setup.Elapsed, 1, [headers]);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            setup.Stop();
            return ThroughputStreamResult.Failed(0, startedAt, DateTimeOffset.UtcNow, 0, setup.Elapsed, TimeSpan.Zero, ex.GetType().Name, ex.Message, null, TimeSpan.Zero, setup.Elapsed, 1, []);
        }
    }

    private async Task<ThroughputStreamResult> UploadWorkerAsync(Uri uri, byte[] payload, int streamIndex, CancellationToken cancellationToken)
    {
        long bytes = 0;
        var setup = TimeSpan.Zero;
        var startedAt = DateTimeOffset.UtcNow;
        var startedOffset = ThroughputWindow.Current!.Elapsed;
        var stoppedOffset = startedOffset;
        var requestIndex = 0;
        var responses = new List<ResponseEvidence>();
        long bytesExcludedAfterDeadline = 0;
        try
        {
            while (!ThroughputWindow.Current.IsExpired && !cancellationToken.IsCancellationRequested)
            {
                requestIndex++;
                var setupWatch = Stopwatch.StartNew();
                long requestBytes = 0;
                using var request = BuildUploadRequest(uri, payload, streamIndex, requestIndex, ThroughputWindow.Current, (written, afterDeadline) =>
                {
                    requestBytes += written;
                    if (afterDeadline)
                    {
                        bytesExcludedAfterDeadline += written;
                    }
                    else
                    {
                        bytes += written;
                    }
                });
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ThroughputWindow.Current.CancellationToken).ConfigureAwait(false);
                setupWatch.Stop();
                setup += setupWatch.Elapsed;
                responses.Add(ResponseEvidence.From(response, setupWatch.Elapsed, requestBytes));
                if (!response.IsSuccessStatusCode)
                {
                    stoppedOffset = ThroughputWindow.Current.Elapsed;
                    return ThroughputStreamResult.Failed(streamIndex, startedAt, DateTimeOffset.UtcNow, bytes, setup, stoppedOffset - startedOffset, $"HTTP {(int)response.StatusCode}", response.ReasonPhrase ?? "Upload endpoint rejected the payload.", response.Version.ToString(), startedOffset, stoppedOffset, requestIndex, responses, bytesExcludedAfterDeadline, "endpoint rejected request");
                }
            }

            stoppedOffset = ThroughputWindow.Current.Elapsed;
            return ThroughputStreamResult.Success(streamIndex, startedAt, DateTimeOffset.UtcNow, bytes, setup, stoppedOffset - startedOffset, responses.LastOrDefault()?.HttpVersion, startedOffset, stoppedOffset, requestIndex, responses, bytesExcludedAfterDeadline, ThroughputWindow.Current.IsExpired ? "global deadline reached" : "worker completed before deadline");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            stoppedOffset = ThroughputWindow.Current?.Elapsed ?? stoppedOffset;
            return bytes > 0 && ThroughputWindow.Current?.IsExpired == true
                ? ThroughputStreamResult.Success(streamIndex, startedAt, DateTimeOffset.UtcNow, bytes, setup, stoppedOffset - startedOffset, responses.LastOrDefault()?.HttpVersion, startedOffset, stoppedOffset, requestIndex, responses, bytesExcludedAfterDeadline, "global deadline canceled in-flight request")
                : ThroughputStreamResult.Failed(streamIndex, startedAt, DateTimeOffset.UtcNow, bytes, setup, stoppedOffset - startedOffset, ex.GetType().Name, ex.Message, responses.LastOrDefault()?.HttpVersion, startedOffset, stoppedOffset, requestIndex, responses, bytesExcludedAfterDeadline, ex.GetType().Name);
        }
    }

    private SpeedTestMeasurement SpeedResult(Guid sessionId, DateTimeOffset observedAt, string direction, Uri uri, ThroughputWindowResult result, ThroughputStreamResult warmup)
    {
        var succeeded = result.ResultStatus is SpeedResultStatus.Valid or SpeedResultStatus.Degraded or SpeedResultStatus.DegradedUploadEndpointMayBeLimiting;
        var mbps = succeeded ? ThroughputCalculator.MegabitsPerSecond(result.BytesTransferred, result.Elapsed) : null;
        var diagnostic = JsonSerializer.Serialize(new
        {
            methodology = MeasurementMethodology.CurrentVersion,
            provider = ProviderName,
            direction,
            endpoint = uri.Host,
            timingModel = "global-wall-clock-window",
            streamCount = options.ParallelStreamCount,
            maximumCredibleMegabitsPerSecond = options.MaximumCredibleMegabitsPerSecond,
            endpointStrategy = direction == "upload" ? "single configured upload endpoint; no silent fallback selected" : "single configured download endpoint",
            globalStartUtc = result.StartedAtUtc,
            globalEndUtc = result.EndedAtUtc,
            globalElapsedMs = result.Elapsed.TotalMilliseconds,
            bytesTransferred = result.BytesTransferred,
            setupDurationMs = result.SetupDuration.TotalMilliseconds,
            transferDurationMs = result.Elapsed.TotalMilliseconds,
            warmupDurationMs = warmup.Elapsed.TotalMilliseconds,
            resultStatus = result.ResultStatus,
            failureCategory = result.FailureCategory,
            failureMessage = result.FailureMessage,
            confidence = result.Confidence,
            warmup = new
            {
                warmup.Succeeded,
                warmup.SetupDuration,
                warmup.TransferDuration,
                warmup.FailureCategory,
                warmup.FailureMessage,
                warmup.Responses
            },
            streams = result.Streams.Select(stream => new
            {
                stream.StreamIndex,
                stream.StartedAt,
                stream.EndedAt,
                stream.Succeeded,
                stream.BytesTransferred,
                workerStartOffsetMs = stream.WorkerStartOffset.TotalMilliseconds,
                workerStopOffsetMs = stream.WorkerStopOffset.TotalMilliseconds,
                stream.RequestCount,
                stream.BytesExcludedAfterDeadline,
                stream.CancellationReason,
                setupDurationMs = stream.SetupDuration.TotalMilliseconds,
                transferDurationMs = stream.TransferDuration.TotalMilliseconds,
                stream.HttpVersion,
                stream.FailureCategory,
                stream.FailureMessage,
                stream.Responses
            })
        });
        return new SpeedTestMeasurement(sessionId, observedAt, direction, succeeded, mbps, result.BytesTransferred, result.Elapsed, ProviderName, uri.ToString(), result.FailureCategory, result.FailureMessage, result.ResultStatus, result.SetupDuration, result.Elapsed, warmup.Elapsed, options.ParallelStreamCount, result.Streams.FirstOrDefault(stream => stream.HttpVersion is not null)?.HttpVersion, MeasurementMethodology.CurrentVersion, diagnostic);
    }

    private SpeedTestMeasurement FailedSpeed(Guid sessionId, DateTimeOffset observedAt, string direction, TimeSpan duration, Uri uri, string status, string category, string message, ThroughputStreamResult? warmup, IReadOnlyList<ThroughputStreamResult>? streams)
    {
        var result = new ThroughputWindowResult(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, duration, streams?.Sum(stream => stream.BytesTransferred) ?? 0, TimeSpan.Zero, status, category, message, ThroughputConfidence.NotEvaluated, streams ?? []);
        var warmupResult = warmup ?? ThroughputStreamResult.Failed(0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, TimeSpan.Zero, TimeSpan.Zero, "NotRun", "Warmup did not run.", null, TimeSpan.Zero, TimeSpan.Zero, 0, []);
        return SpeedResult(sessionId, observedAt, direction, uri, result, warmupResult);
    }

    private TimeSpan MeasurementDuration(TimeSpan budget, TimeSpan elapsedWarmup)
    {
        var remaining = budget - elapsedWarmup - TimeSpan.FromSeconds(2);
        if (remaining < options.MinimumMeasurementDuration)
        {
            return remaining > TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1);
        }

        return remaining > options.TargetMeasurementDuration ? options.TargetMeasurementDuration : remaining;
    }

    private ThroughputValidationFailure? ValidateThroughput(string direction, long bytes, TimeSpan elapsed, IReadOnlyList<ThroughputStreamResult> streams)
    {
        if (elapsed <= TimeSpan.Zero || double.IsNaN(elapsed.TotalSeconds))
        {
            return new("ZeroOrInvalidDuration", "Global measured duration was zero or invalid.");
        }

        var mbps = ThroughputCalculator.MegabitsPerSecond(bytes, elapsed);
        if (mbps is not null && mbps > options.MaximumCredibleMegabitsPerSecond)
        {
            return new("SuspiciousThroughputCeiling", $"{direction} estimate {mbps:0.000} Mbps exceeds configured ceiling {options.MaximumCredibleMegabitsPerSecond:0.000} Mbps.");
        }

        if (streams.Any(stream => stream.WorkerStartOffset < TimeSpan.Zero || stream.WorkerStopOffset - stream.WorkerStartOffset > elapsed + TimeSpan.FromMilliseconds(50)))
        {
            return new("WorkerDurationOutsideGlobalWindow", "At least one worker recorded a duration outside the global measurement window.");
        }

        if (streams.Any(stream => stream.BytesTransferred > bytes))
        {
            return new("StreamBytesExceedTotal", "A stream byte count exceeds the total byte count.");
        }

        if (bytes != streams.Sum(stream => stream.BytesTransferred))
        {
            return new("ByteSumMismatch", "Total bytes do not match the sum of stream bytes.");
        }

        return null;
    }

    private ThroughputConfidence EvaluateConfidence(string direction, long bytes, TimeSpan elapsed, IReadOnlyList<ThroughputStreamResult> streams)
    {
        if (streams.Count == 0 || elapsed <= TimeSpan.Zero)
        {
            return ThroughputConfidence.NotEvaluated with { Reasons = ["No measured streams were available."] };
        }

        var elapsedMs = Math.Max(1, elapsed.TotalMilliseconds);
        var participationRatios = streams
            .Select(stream => Math.Clamp((stream.WorkerStopOffset - stream.WorkerStartOffset).TotalMilliseconds / elapsedMs, 0, 1))
            .ToArray();
        var minimumParticipationRatio = participationRatios.Min();
        var allStreamsActive = minimumParticipationRatio >= options.MinimumStreamParticipationRatio;
        var maxBytes = streams.Max(stream => stream.BytesTransferred);
        var minBytes = streams.Min(stream => stream.BytesTransferred);
        var streamBalanceRatio = maxBytes <= 0 ? 0 : (double)minBytes / maxBytes;
        var minimumTransferredDataMet = direction != "upload"
            || bytes >= (long)options.MinimumUploadBytesPerStream * streams.Count;
        var allResponsesSuccessful = streams.All(stream =>
            stream.Responses.Count > 0
            && stream.Responses.All(response => response.StatusCode is >= 200 and <= 299));
        var earlyCompletion = streams.Any(stream => stream.WorkerStopOffset < elapsed - TimeSpan.FromMilliseconds(250));
        var excludedBytes = streams.Sum(stream => stream.BytesExcludedAfterDeadline);
        var likelyFlowControlOrCompletionStall = direction == "upload"
            && streams.Any(stream =>
                stream.RequestCount <= 1
                && stream.SetupDuration.TotalMilliseconds / elapsedMs > 0.80
                && stream.BytesTransferred > 0);

        var reasons = new List<string>();
        if (!allStreamsActive)
        {
            reasons.Add($"One or more streams participated in less than {options.MinimumStreamParticipationRatio:P0} of the global window.");
        }

        if (streamBalanceRatio < options.MinimumStreamBalanceRatio)
        {
            reasons.Add($"Per-stream byte balance ratio {streamBalanceRatio:0.00} is below {options.MinimumStreamBalanceRatio:0.00}.");
        }

        if (!minimumTransferredDataMet)
        {
            reasons.Add("Transferred upload data did not meet the minimum diagnostic threshold.");
        }

        if (!allResponsesSuccessful)
        {
            reasons.Add("One or more endpoint responses were missing or unsuccessful.");
        }

        if (earlyCompletion)
        {
            reasons.Add("One or more workers stopped before the global deadline.");
        }

        if (likelyFlowControlOrCompletionStall)
        {
            reasons.Add("At least one upload stream spent most of the window in one request, which may indicate flow-control, endpoint, or response-completion delay.");
        }

        if (excludedBytes > 0)
        {
            reasons.Add("Some write buffers completed after the global deadline and were excluded from the Mbps calculation.");
        }

        var suspectedEndpointLimitation = direction == "upload"
            && streams.Count > 1
            && bytes > 0
            && allResponsesSuccessful
            && (earlyCompletion
                || !allStreamsActive
                || streamBalanceRatio < options.MinimumStreamBalanceRatio
                || !minimumTransferredDataMet
                || likelyFlowControlOrCompletionStall);

        return new ThroughputConfidence(
            allStreamsActive,
            minimumParticipationRatio,
            streamBalanceRatio,
            allResponsesSuccessful,
            minimumTransferredDataMet,
            earlyCompletion,
            likelyFlowControlOrCompletionStall,
            excludedBytes,
            suspectedEndpointLimitation,
            reasons);
    }

    private static HttpRequestMessage BuildDownloadRequest(Uri uri, int streamIndex, long requestIndex)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, CacheBusted(uri, streamIndex, requestIndex)) { VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.AcceptEncoding.Clear();
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        return request;
    }

    private HttpRequestMessage BuildUploadRequest(Uri uri, byte[] payload, int streamIndex, long requestIndex, ThroughputWindow? window, Action<long, bool>? countBytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, CacheBusted(uri, streamIndex, requestIndex)) { VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher };
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        request.Headers.ExpectContinue = false;
        request.Content = countBytes is null
            ? new ByteArrayContent(payload)
            : new CountingUploadContent(payload, options.UploadBufferSize, window ?? throw new InvalidOperationException("A throughput window is required for measured uploads."), countBytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return request;
    }

    private static Uri CacheBusted(Uri uri, int streamIndex, long requestIndex)
    {
        var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
        return new Uri($"{uri}{separator}np_cache_bust={Guid.NewGuid():N}&np_stream={streamIndex}&np_request={requestIndex}");
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

    private sealed record ThroughputWindowResult(
        DateTimeOffset StartedAtUtc,
        DateTimeOffset EndedAtUtc,
        TimeSpan Elapsed,
        long BytesTransferred,
        TimeSpan SetupDuration,
        string ResultStatus,
        string? FailureCategory,
        string? FailureMessage,
        ThroughputConfidence Confidence,
        IReadOnlyList<ThroughputStreamResult> Streams);

    private sealed record ThroughputValidationFailure(string Category, string Message);

    private sealed record ThroughputConfidence(
        bool AllStreamsActive,
        double MinimumParticipationRatio,
        double StreamBalanceRatio,
        bool AllResponsesSuccessful,
        bool MinimumTransferredDataMet,
        bool EarlyCompletion,
        bool LikelyFlowControlOrCompletionStall,
        long BytesExcludedAfterDeadline,
        bool SuspectedEndpointLimitation,
        IReadOnlyList<string> Reasons)
    {
        public static ThroughputConfidence NotEvaluated { get; } = new(false, 0, 0, false, false, false, false, 0, false, []);
    }

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
        string? FailureMessage,
        TimeSpan WorkerStartOffset,
        TimeSpan WorkerStopOffset,
        int RequestCount,
        IReadOnlyList<ResponseEvidence> Responses,
        long BytesExcludedAfterDeadline,
        string? CancellationReason)
    {
        public TimeSpan Elapsed => SetupDuration + TransferDuration;

        public static ThroughputStreamResult Success(int streamIndex, DateTimeOffset startedAt, DateTimeOffset endedAt, long bytesTransferred, TimeSpan setupDuration, TimeSpan transferDuration, string? httpVersion, TimeSpan workerStartOffset, TimeSpan workerStopOffset, int requestCount, IReadOnlyList<ResponseEvidence> responses, long bytesExcludedAfterDeadline = 0, string? cancellationReason = null)
            => new(streamIndex, startedAt, endedAt, true, bytesTransferred, setupDuration, transferDuration, httpVersion, null, null, workerStartOffset, workerStopOffset, requestCount, responses, bytesExcludedAfterDeadline, cancellationReason);

        public static ThroughputStreamResult Failed(int streamIndex, DateTimeOffset startedAt, DateTimeOffset endedAt, long bytesTransferred, TimeSpan setupDuration, TimeSpan transferDuration, string failureCategory, string failureMessage, string? httpVersion, TimeSpan workerStartOffset, TimeSpan workerStopOffset, int requestCount, IReadOnlyList<ResponseEvidence> responses, long bytesExcludedAfterDeadline = 0, string? cancellationReason = null)
            => new(streamIndex, startedAt, endedAt, false, bytesTransferred, setupDuration, transferDuration, httpVersion, failureCategory, failureMessage, workerStartOffset, workerStopOffset, requestCount, responses, bytesExcludedAfterDeadline, cancellationReason);
    }

    private sealed record ResponseEvidence(
        int StatusCode,
        string? ReasonPhrase,
        string HttpVersion,
        long? ContentLength,
        string? ContentEncoding,
        string? Age,
        string? Via,
        string? XCache,
        double? RequestDurationMilliseconds,
        long? RequestBytesWritten)
    {
        public static ResponseEvidence From(HttpResponseMessage response, TimeSpan? requestDuration = null, long? requestBytesWritten = null)
            => new(
                (int)response.StatusCode,
                response.ReasonPhrase,
                response.Version.ToString(),
                response.Content.Headers.ContentLength,
                string.Join(",", response.Content.Headers.ContentEncoding),
                response.Headers.TryGetValues("Age", out var age) ? string.Join(",", age) : null,
                response.Headers.TryGetValues("Via", out var via) ? string.Join(",", via) : null,
                response.Headers.TryGetValues("X-Cache", out var cache) ? string.Join(",", cache) : null,
                requestDuration?.TotalMilliseconds,
                requestBytesWritten);
    }

    private sealed class ThroughputWindow : IDisposable
    {
        private static readonly AsyncLocal<ThroughputWindow?> CurrentWindow = new();
        private readonly CancellationTokenSource deadlineSource = new();
        private readonly TimeSpan duration;
        private readonly Stopwatch stopwatch = new();

        public ThroughputWindow(TimeSpan duration)
        {
            this.duration = duration;
        }

        public static ThroughputWindow? Current => CurrentWindow.Value;

        public DateTimeOffset StartedAtUtc { get; private set; }

        public DateTimeOffset EndedAtUtc { get; private set; }

        public TimeSpan Elapsed => stopwatch.Elapsed;

        public CancellationToken CancellationToken => deadlineSource.Token;

        public bool IsExpired => deadlineSource.IsCancellationRequested || stopwatch.Elapsed >= duration;

        public IDisposable Enter()
        {
            CurrentWindow.Value = this;
            return this;
        }

        public void Start()
        {
            StartedAtUtc = DateTimeOffset.UtcNow;
            stopwatch.Start();
            deadlineSource.CancelAfter(duration);
        }

        public void Stop()
        {
            if (stopwatch.IsRunning)
            {
                stopwatch.Stop();
            }

            EndedAtUtc = StartedAtUtc + stopwatch.Elapsed;
        }

        public void Dispose()
        {
            CurrentWindow.Value = null;
            deadlineSource.Dispose();
        }
    }

    private sealed class CountingUploadContent(byte[] payload, int bufferSize, ThroughputWindow window, Action<long, bool> countBytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            var offset = 0;
            while (offset < payload.Length && !window.IsExpired && !cancellationToken.IsCancellationRequested)
            {
                var count = Math.Min(bufferSize, payload.Length - offset);
                await stream.WriteAsync(payload.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
                if (window.IsExpired)
                {
                    countBytes(count, true);
                    break;
                }

                countBytes(count, false);
                offset += count;
            }
        }

        protected override bool TryComputeLength(out long length)
        {
            length = payload.LongLength;
            return true;
        }
    }
}
