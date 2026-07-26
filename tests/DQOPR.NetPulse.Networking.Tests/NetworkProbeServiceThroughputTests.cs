using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DQOPR.NetPulse.Core.Models;
using DQOPR.NetPulse.Networking.Probes;

namespace DQOPR.NetPulse.Networking.Tests;

public sealed class NetworkProbeServiceThroughputTests
{
    [Fact]
    public async Task UsesGlobalWallClockWindowForDownloadAndUpload()
    {
        await using var server = await ControlledThroughputServer.StartAsync(
            downloadBytesPerSecond: 20_000_000 / 8,
            uploadBytesPerSecond: 10_000_000 / 8);
        using var probes = new NetworkProbeService(
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            TestOptions(streams: 1));

        var results = await probes.RunSpeedTestAsync(Guid.NewGuid(), server.DownloadUri, server.UploadUri, TimeSpan.FromSeconds(8), CancellationToken.None);

        var download = Assert.Single(results, result => result.Direction == "download");
        var upload = Assert.Single(results, result => result.Direction == "upload");
        Assert.Equal(SpeedResultStatus.Valid, download.ResultStatus);
        Assert.Equal(SpeedResultStatus.Valid, upload.ResultStatus);
        Assert.InRange(download.MegabitsPerSecond!.Value, 14, 28);
        Assert.True(double.IsFinite(upload.MegabitsPerSecond!.Value));
        Assert.True(upload.MegabitsPerSecond.Value > 0);
        AssertEvidenceMatchesResult(download);
        AssertEvidenceMatchesResult(upload);
    }

    [Fact]
    public async Task AggregatesParallelStreamsAgainstOneGlobalElapsedDuration()
    {
        await using var server = await ControlledThroughputServer.StartAsync(
            downloadBytesPerSecond: 5_000_000 / 8,
            uploadBytesPerSecond: 5_000_000 / 8);
        using var probes = new NetworkProbeService(
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            TestOptions(streams: 2));

        var results = await probes.RunSpeedTestAsync(Guid.NewGuid(), server.DownloadUri, server.UploadUri, TimeSpan.FromSeconds(8), CancellationToken.None);

        var download = Assert.Single(results, result => result.Direction == "download");
        Assert.Equal(SpeedResultStatus.Valid, download.ResultStatus);
        Assert.InRange(download.MegabitsPerSecond!.Value, 7, 14);
        AssertEvidenceMatchesResult(download);
    }

    [Fact]
    public async Task MarksSuspiciousThroughputAsAccountingInconsistencyInsteadOfClamping()
    {
        await using var server = await ControlledThroughputServer.StartAsync(
            downloadBytesPerSecond: 20_000_000 / 8,
            uploadBytesPerSecond: 10_000_000 / 8);
        using var probes = new NetworkProbeService(
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            TestOptions(streams: 1) with { MaximumCredibleMegabitsPerSecond = 1 });

        var results = await probes.RunSpeedTestAsync(Guid.NewGuid(), server.DownloadUri, server.UploadUri, TimeSpan.FromSeconds(8), CancellationToken.None);

        var download = Assert.Single(results, result => result.Direction == "download");
        Assert.Equal(SpeedResultStatus.MeasurementAccountingInconsistency, download.ResultStatus);
        Assert.False(download.Succeeded);
        Assert.Null(download.MegabitsPerSecond);
        Assert.Equal("SuspiciousThroughputCeiling", download.FailureCategory);

        var upload = Assert.Single(results, result => result.Direction == "upload");
        Assert.True(upload.MegabitsPerSecond is null or > 0);
        AssertEvidenceMatchesResult(download);
        AssertEvidenceMatchesResult(upload);
    }

    private static NetworkProbeOptions TestOptions(int streams)
        => new()
        {
            WarmupDuration = TimeSpan.Zero,
            MinimumMeasurementDuration = TimeSpan.FromSeconds(1),
            TargetMeasurementDuration = TimeSpan.FromSeconds(2),
            ParallelStreamCount = streams,
            DownloadBufferSize = 16 * 1024,
            UploadBufferSize = 16 * 1024,
            UploadPayloadBytes = 64 * 1024,
            MaximumCredibleMegabitsPerSecond = 100
        };

    private static void AssertEvidenceMatchesResult(SpeedTestMeasurement result)
    {
        using var document = JsonDocument.Parse(result.DiagnosticJson!);
        var root = document.RootElement;
        Assert.Equal("global-wall-clock-window", root.GetProperty("timingModel").GetString());

        var elapsedMs = root.GetProperty("globalElapsedMs").GetDouble();
        Assert.True(root.TryGetProperty("confidence", out var confidence));
        Assert.True(confidence.TryGetProperty("AllStreamsActive", out _));
        Assert.True(confidence.TryGetProperty("StreamBalanceRatio", out _));
        Assert.True(confidence.TryGetProperty("BytesExcludedAfterDeadline", out _));
        Assert.True(confidence.TryGetProperty("SuspectedEndpointLimitation", out _));
        Assert.InRange(elapsedMs, result.ActiveDuration.TotalMilliseconds - 25, result.ActiveDuration.TotalMilliseconds + 25);
        Assert.InRange(
            DateTimeOffset.Parse(root.GetProperty("globalEndUtc").GetString()!, CultureInfo.InvariantCulture)
                - DateTimeOffset.Parse(root.GetProperty("globalStartUtc").GetString()!, CultureInfo.InvariantCulture),
            result.ActiveDuration - TimeSpan.FromMilliseconds(50),
            result.ActiveDuration + TimeSpan.FromMilliseconds(50));

        var streamBytes = root.GetProperty("streams")
            .EnumerateArray()
            .Sum(stream => stream.GetProperty("BytesTransferred").GetInt64());
        Assert.Equal(result.BytesTransferred, streamBytes);

        foreach (var stream in root.GetProperty("streams").EnumerateArray())
        {
            Assert.InRange(stream.GetProperty("workerStartOffsetMs").GetDouble(), -1, elapsedMs + 25);
            Assert.InRange(stream.GetProperty("workerStopOffsetMs").GetDouble(), -1, elapsedMs + 50);
            Assert.True(stream.GetProperty("RequestCount").GetInt32() >= 1);
            Assert.True(stream.TryGetProperty("BytesExcludedAfterDeadline", out _));
            Assert.True(stream.TryGetProperty("CancellationReason", out _));
            foreach (var response in stream.GetProperty("Responses").EnumerateArray())
            {
                Assert.True(response.TryGetProperty("RequestDurationMilliseconds", out _));
                Assert.True(response.TryGetProperty("RequestBytesWritten", out _));
            }
        }
    }

    private sealed class ControlledThroughputServer : IAsyncDisposable
    {
        private readonly TcpListener listener;
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task acceptLoop;
        private readonly int downloadBytesPerSecond;
        private readonly int uploadBytesPerSecond;

        private ControlledThroughputServer(TcpListener listener, int downloadBytesPerSecond, int uploadBytesPerSecond)
        {
            this.listener = listener;
            this.downloadBytesPerSecond = downloadBytesPerSecond;
            this.uploadBytesPerSecond = uploadBytesPerSecond;
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            DownloadUri = new Uri($"http://127.0.0.1:{port}/download");
            UploadUri = new Uri($"http://127.0.0.1:{port}/upload");
            acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public Uri DownloadUri { get; }

        public Uri UploadUri { get; }

        public static Task<ControlledThroughputServer> StartAsync(int downloadBytesPerSecond, int uploadBytesPerSecond)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return Task.FromResult(new ControlledThroughputServer(listener, downloadBytesPerSecond, uploadBytesPerSecond));
        }

        public async ValueTask DisposeAsync()
        {
            cancellation.Cancel();
            listener.Stop();
            try
            {
                await acceptLoop.ConfigureAwait(false);
            }
            catch (SocketException)
            {
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            cancellation.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(cancellation.Token).ConfigureAwait(false);
                    _ = Task.Run(() => HandleClientAsync(client));
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
        }

        private async Task HandleClientAsync(TcpClient client)
        {
            using (client)
            {
                client.ReceiveBufferSize = 4 * 1024;
                client.SendBufferSize = 4 * 1024;
                await using var stream = client.GetStream();
                var headers = await ReadHeadersAsync(stream, cancellation.Token).ConfigureAwait(false);
                if (headers.Length == 0)
                {
                    return;
                }

                var text = Encoding.ASCII.GetString(headers);
                if (text.StartsWith("GET ", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteDownloadAsync(stream, cancellation.Token).ConfigureAwait(false);
                    return;
                }

                if (text.StartsWith("POST ", StringComparison.OrdinalIgnoreCase))
                {
                    await ReadUploadAsync(stream, text, cancellation.Token).ConfigureAwait(false);
                    await WriteAsciiAsync(stream, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK", cancellation.Token).ConfigureAwait(false);
                }
            }
        }

        private async Task WriteDownloadAsync(Stream stream, CancellationToken token)
        {
            await WriteAsciiAsync(
                stream,
                "HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: 104857600\r\nCache-Control: no-store\r\nX-Cache: TEST-MISS\r\nConnection: close\r\n\r\n",
                token).ConfigureAwait(false);
            var chunk = new byte[Math.Max(1024, downloadBytesPerSecond / 20)];
            var stopwatch = Stopwatch.StartNew();
            long sent = 0;
            while (!token.IsCancellationRequested)
            {
                await stream.WriteAsync(chunk, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                sent += chunk.Length;
                var expected = TimeSpan.FromSeconds((double)sent / downloadBytesPerSecond);
                var delay = expected - stopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
            }
        }

        private async Task ReadUploadAsync(Stream stream, string headers, CancellationToken token)
        {
            if (headers.Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase))
            {
                await ReadChunkedBodyAsync(stream, token).ConfigureAwait(false);
                return;
            }

            var length = ContentLength(headers);
            if (length > 0)
            {
                await ReadFixedBodyAsync(stream, length, token).ConfigureAwait(false);
            }
        }

        private async Task ReadChunkedBodyAsync(Stream stream, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var line = await ReadLineAsync(stream, token).ConfigureAwait(false);
                var semicolon = line.IndexOf(';', StringComparison.Ordinal);
                var sizeText = semicolon >= 0 ? line[..semicolon] : line;
                var size = int.Parse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (size == 0)
                {
                    await ReadLineAsync(stream, token).ConfigureAwait(false);
                    return;
                }

                await ReadFixedBodyAsync(stream, size, token).ConfigureAwait(false);
                await ReadLineAsync(stream, token).ConfigureAwait(false);
            }
        }

        private async Task ReadFixedBodyAsync(Stream stream, long length, CancellationToken token)
        {
            var buffer = new byte[4096];
            var stopwatch = Stopwatch.StartNew();
            long readTotal = 0;
            while (readTotal < length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, length - readTotal)), token).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                readTotal += read;
                var expected = TimeSpan.FromSeconds((double)readTotal / uploadBytesPerSecond);
                var delay = expected - stopwatch.Elapsed;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, token).ConfigureAwait(false);
                }
            }
        }

        private static async Task<byte[]> ReadHeadersAsync(Stream stream, CancellationToken token)
        {
            var bytes = new List<byte>();
            while (bytes.Count < 64 * 1024)
            {
                var value = stream.ReadByte();
                if (value < 0)
                {
                    break;
                }

                bytes.Add((byte)value);
                if (bytes.Count >= 4
                    && bytes[^4] == '\r'
                    && bytes[^3] == '\n'
                    && bytes[^2] == '\r'
                    && bytes[^1] == '\n')
                {
                    break;
                }

                await Task.Yield();
                token.ThrowIfCancellationRequested();
            }

            return bytes.ToArray();
        }

        private static async Task<string> ReadLineAsync(Stream stream, CancellationToken token)
        {
            var bytes = new List<byte>();
            while (true)
            {
                var value = stream.ReadByte();
                if (value < 0)
                {
                    return Encoding.ASCII.GetString(bytes.ToArray());
                }

                bytes.Add((byte)value);
                if (bytes.Count >= 2 && bytes[^2] == '\r' && bytes[^1] == '\n')
                {
                    return Encoding.ASCII.GetString(bytes.Take(bytes.Count - 2).ToArray());
                }

                await Task.Yield();
                token.ThrowIfCancellationRequested();
            }
        }

        private static long ContentLength(string headers)
        {
            foreach (var line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                    && long.TryParse(line["Content-Length:".Length..].Trim(), CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }

            return 0;
        }

        private static Task WriteAsciiAsync(Stream stream, string value, CancellationToken token)
            => stream.WriteAsync(Encoding.ASCII.GetBytes(value), token).AsTask();
    }
}
