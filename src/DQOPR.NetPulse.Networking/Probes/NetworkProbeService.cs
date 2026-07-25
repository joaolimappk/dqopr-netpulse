using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Core.Monitoring;

namespace DQOPR.NetPulse.Networking.Probes;

public sealed class NetworkProbeService(HttpClient? httpClient = null) : INetworkProbeService, IDisposable
{
    private readonly HttpClient httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    private readonly bool ownsHttpClient = httpClient is null;

    public async Task<ProbeMeasurement> ProbeIcmpAsync(Guid sessionId, TargetDefinition target, int sequence, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target.Host, (int)timeout.TotalMilliseconds).WaitAsync(cancellationToken).ConfigureAwait(false);
            return reply.Status == IPStatus.Success
                ? new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Icmp, target.Name, true, reply.RoundtripTime)
                : new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Icmp, target.Name, false, null, reply.Status.ToString(), $"ICMP sequence {sequence} failed with {reply.Status}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is PingException or SocketException or InvalidOperationException)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Icmp, target.Name, false, null, ex.GetType().Name, $"ICMP sequence {sequence} failed: {ex.Message}");
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
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.TcpConnect, target.Name, true, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.TcpConnect, target.Name, false, null, "Timeout", $"TCP connection to {target.Host}:{port} timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SocketException ex)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.TcpConnect, target.Name, false, null, ex.SocketErrorCode.ToString(), ex.Message);
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
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Dns, hostname, addresses.Length > 0, stopwatch.Elapsed.TotalMilliseconds, addresses.Length == 0 ? "NoAddresses" : null, addresses.Length == 0 ? "DNS returned no addresses." : $"Resolved {addresses.Length} address(es).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Dns, hostname, false, null, "Timeout", "DNS resolution timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (SocketException ex)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Dns, hostname, false, null, ex.SocketErrorCode.ToString(), ex.Message);
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
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Https, uri.Host, response.IsSuccessStatusCode, stopwatch.Elapsed.TotalMilliseconds, response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}", response.ReasonPhrase);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Https, uri.Host, false, null, "Timeout", "HTTPS request timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            return new ProbeMeasurement(sessionId, observedAt, ProbeMethod.Https, uri.Host, false, null, ex.HttpRequestError.ToString(), ex.Message);
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
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeoutSource.Token).ConfigureAwait(false);
            var buffer = new byte[64 * 1024];
            long bytes = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, timeoutSource.Token).ConfigureAwait(false)) > 0)
            {
                bytes += read;
                if (stopwatch.Elapsed >= TimeSpan.FromSeconds(10))
                {
                    break;
                }
            }

            stopwatch.Stop();
            return SuccessfulSpeed(sessionId, observedAt, "download", bytes, stopwatch.Elapsed, "Built-in throughput estimate", uri);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailedSpeed(sessionId, observedAt, "download", timeout, "Built-in throughput estimate", uri, "Timeout", "Download throughput estimate timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            stopwatch.Stop();
            return FailedSpeed(sessionId, observedAt, "download", stopwatch.Elapsed, "Built-in throughput estimate", uri, ex.GetType().Name, ex.Message);
        }
    }

    private async Task<SpeedTestMeasurement> RunUploadAsync(Guid sessionId, Uri uri, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var observedAt = DateTimeOffset.UtcNow;
        var payload = new byte[256 * 1024];
        Random.Shared.NextBytes(payload);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            using var content = new ByteArrayContent(payload);
            using var response = await httpClient.PostAsync(uri, content, timeoutSource.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            stopwatch.Stop();
            return SuccessfulSpeed(sessionId, observedAt, "upload", payload.Length, stopwatch.Elapsed, "Built-in throughput estimate", uri);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FailedSpeed(sessionId, observedAt, "upload", timeout, "Built-in throughput estimate", uri, "Timeout", "Upload throughput estimate timed out.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            stopwatch.Stop();
            return FailedSpeed(sessionId, observedAt, "upload", stopwatch.Elapsed, "Built-in throughput estimate", uri, ex.GetType().Name, ex.Message);
        }
    }

    private static SpeedTestMeasurement SuccessfulSpeed(Guid sessionId, DateTimeOffset observedAt, string direction, long bytes, TimeSpan duration, string provider, Uri uri)
    {
        var mbps = duration.TotalSeconds <= 0 ? 0 : bytes * 8.0 / duration.TotalSeconds / 1_000_000.0;
        return new SpeedTestMeasurement(sessionId, observedAt, direction, true, mbps, bytes, duration, provider, uri.ToString(), null, null);
    }

    private static SpeedTestMeasurement FailedSpeed(Guid sessionId, DateTimeOffset observedAt, string direction, TimeSpan duration, string provider, Uri uri, string category, string message)
        => new(sessionId, observedAt, direction, false, null, 0, duration, provider, uri.ToString(), category, message);
}
