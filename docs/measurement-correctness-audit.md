# Measurement Correctness Audit

Version audited: `0.3.0-alpha.4`

Branch: `csharp-rewrite`

## Summary

The real Windows test exposed three correctness defects in the alpha.3 C# rewrite:

- Generic dashboard latency mixed ICMP RTT with DNS, TCP connect, and HTTPS request durations.
- Dashboard jitter was computed from a broad historical bucket rather than the same current ICMP probe stream shown as latency.
- Built-in throughput used short, single-stream/general-purpose HTTP transfers that could not reliably saturate a 200/20 Mbps connection.

Alpha.4 changes the measurement model so every displayed value has a scope, target, sample count, methodology version, and validity status.

## Displayed Metrics

| Metric | Source rows | Protocol | Target | Sample count | Formula | Failure handling | Aggregation window | Mixing allowed |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Router latency | `measurements` where `method = Icmp` and `target_name = Local Gateway` | ICMP | Detected default gateway | Current stream, successful samples | Median RTT in ms | Failed probes excluded from RTT, counted as loss | Same session/target/host/address-family/stream | No |
| Internet latency | `measurements` where `method = Icmp` and target is not gateway | ICMP | Displayed target name and host | Current stream, successful samples | Median RTT in ms | Failed probes excluded from RTT, counted as loss | Same session/target/host/address-family/stream | No |
| Internet jitter | Same rows as displayed internet latency | ICMP | Same displayed internet target | At least 3 successful samples | Mean absolute successive RTT difference in ms | Failed probes excluded from arithmetic | Same session/target/host/address-family/stream | No |
| ICMP packet loss | `measurements` where `method = Icmp` for displayed target | ICMP | Same displayed internet target | Sent/received/lost counts | `lost / sent * 100` | Failed ICMP rows count as lost | Same target summary | No DNS/TCP/HTTPS |
| DNS response | `measurements` where `method = Dns` | DNS resolver lookup | Configured hostname | Latest row or detail rows | Elapsed DNS lookup duration in ms | Failure category/message stored | Separate connectivity category | Not latency/jitter |
| TCP response | `measurements` where `method = TcpConnect` | TCP connect | Configured host:port | Latest row or detail rows | TCP connect elapsed duration in ms | Failure category/message stored | Separate connectivity category | Not latency/jitter |
| HTTPS response | `measurements` where `method = Https` | HTTPS GET | Configured URI host | Latest row or detail rows | HTTP response-header elapsed duration in ms | HTTP status/exception stored | Separate connectivity category | Not latency/jitter |
| Download estimate | `speed_tests` where `direction = download` | HTTPS streamed GET | Configured throughput endpoint | 4 streams plus warmup | `actual_bytes_read * 8 / transfer_seconds / 1_000_000` | Invalid/short/canceled rows are not displayed as valid speeds | One speed-test operation | Not averaged with legacy/failed rows |
| Upload estimate | `speed_tests` where `direction = upload` | HTTPS POST | Configured throughput endpoint | 4 streams plus warmup | `accepted_payload_bytes * 8 / transfer_seconds / 1_000_000` | Upload endpoint failures show unavailable/partial | One speed-test operation | Not averaged with legacy/failed rows |

## Root Cause: 14 ms Latency Beside 128.6 ms Jitter

Alpha.3 updated the dashboard `Latency` card from the latest successful measurement with any `LatencyMilliseconds` value. ICMP, DNS, TCP connect, and HTTPS rows all used that same field, so the card could show a recent low ICMP RTT while another protocol or target had been measured nearby.

Alpha.3 jitter was calculated from the ViewModel's whole in-memory measurement list, grouped only by target name and method. It did not include the session id, target host, address family, or probe stream, and the quick-test path did not clear prior in-memory samples before starting. That allowed jitter to describe a different historical target stream than the currently displayed latency. This is why a plausible 14 ms latency could be shown beside an implausible 128.6 ms jitter.

Alpha.4 calculates displayed internet latency and jitter from the same ICMP series key:

`session_id + method + target_name + target_host + address_family + probe_stream_id`

DNS, TCP, HTTPS, gateway ICMP, other internet targets, other address families, and previous sessions cannot contribute to that displayed jitter value.

## Latency and Jitter Formulas

Raw ICMP rows are preserved. Failed probes remain rows with `succeeded = false`, no RTT, and a safe failure category/message.

For a selected ICMP series:

- `sample_count`: every ICMP row in the series.
- `successful_sample_count`: rows with `succeeded = true` and RTT.
- `failed_sample_count`: `sample_count - successful_sample_count`.
- `min`: minimum successful RTT.
- `median`: 50th percentile of successful RTTs.
- `mean`: arithmetic mean of successful RTTs.
- `p95`: interpolated 95th percentile of successful RTTs.
- `max`: maximum successful RTT.
- `jitter`: mean absolute difference between consecutive successful RTTs in observed order.

At least 3 successful ICMP samples are required before jitter is reported. Outliers are not discarded.

## Throughput Audit

Alpha.3 download used a tiny single-stream Cloudflare download URL and included setup effects in a short transfer. A 1 MB object is too small to saturate a 200+ Mbps connection, so TCP slow start, TLS/setup, buffering, scheduling, and endpoint behavior dominated the result.

Alpha.3 upload used a single 256 KiB POST to a general-purpose `httpbin.org` endpoint. That endpoint is not a throughput service and may reject, rate-limit, process slowly, or fail requests. Counting a small payload over a request/response round trip under-reported upload and produced `HttpRequestException` failures.

Alpha.4 built-in throughput behavior:

- Provider label: `NetPulse built-in estimate`.
- Default download endpoint: `https://speed.cloudflare.com/__down?bytes=25000000`, repeated with cache-busting inside each stream until the timed transfer window ends.
- Default upload endpoint: `https://speed.cloudflare.com/__up`.
- HTTPS only by default.
- Cache-busting query parameters per stream.
- `Cache-Control: no-cache, no-store`.
- `Accept-Encoding: identity`.
- 1 second warmup per direction, excluded from Mbps.
- 4 parallel measurement streams.
- 8 second minimum transfer duration for valid/degraded status.
- Monotonic stopwatch timing.
- Actual bytes read or accepted payload bytes counted.
- Setup duration, transfer duration, warmup duration, stream count, HTTP version, status, endpoint, and safe failure fields persisted.

Rows can have these statuses: `Valid`, `Degraded`, `Endpoint limited`, `Insufficient duration`, `Upload endpoint unavailable`, `Test canceled`, `Invalid result`, or `Legacy estimate - methodology version prior to alpha.4`.

Only `Valid` and `Degraded` alpha.4 speed rows are shown as numeric dashboard/history speeds. Legacy, invalid, insufficient-duration, canceled, endpoint-limited, and unavailable rows remain visible in details/exports but are not aggregated as valid speeds.

## Optional Reference Engine

An optional recognized speed-test engine remains a future integration. It must not be bundled, downloaded, or invoked silently. Before integration, the project must review the engine's license, redistribution terms, automated-use terms, attribution requirements, and consent flow.

Alpha.4 adds manual reference-result storage instead. Testers can enter provider, download, upload, latency, timestamp, and notes. NetPulse calculates comparison deltas against the selected session but does not adjust its measurements to match the reference.

## Persistence

Schema version `2` adds:

- `sessions.methodology_version`
- `measurements.target_host`
- `measurements.address_family`
- `measurements.probe_stream_id`
- `measurements.sequence`
- `measurements.methodology_version`
- `speed_tests.result_status`
- `speed_tests.setup_duration_ms`
- `speed_tests.transfer_duration_ms`
- `speed_tests.warmup_duration_ms`
- `speed_tests.parallel_stream_count`
- `speed_tests.http_version`
- `speed_tests.methodology_version`
- `speed_tests.diagnostic_json`
- `reference_speed_results`

Existing rows upgraded from earlier schema versions are marked `pre-alpha.4`; existing speed rows are labeled `Legacy estimate - methodology version prior to alpha.4`.

## Time Display

SQLite stores timestamps as UTC ISO-8601 strings. The WPF UI displays local time for history/detail grids. CSV, JSON, HTML, and diagnostic exports include local time and timezone offset while retaining UTC.

## Diagnostics

The diagnostic bundle includes raw ICMP samples, target metadata, timestamps, calculated ICMP statistics, throughput stream-level bytes/durations/start/end timestamps, endpoint/provider/status, and safe exception category/message. It does not include request headers, response headers, credentials, or payload bytes.

## Remaining Limitations

- The built-in throughput estimate is not ISP-certified and should be compared against independent reference tests.
- Upload measurement still depends on endpoint behavior and HTTP request completion semantics; endpoint unavailability is reported rather than converted into a numeric speed.
- GitHub-hosted smoke tests validate function and evidence capture, not the user's real ISP path.
- Real attended Windows comparison must still be repeated across at least five idle cycles before claiming accuracy against the acceptance targets.
