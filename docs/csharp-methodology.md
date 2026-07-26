# C# Monitoring Methodology

This document defines the methodology the C# implementation must follow. Code and tests should be changed when they conflict with this document, not the other way around, unless the methodology is intentionally revised.

## Independent Scheduling

Each probe type has its own schedule, timeout, cancellation path, status, and missed-run handling.

Default evidence intervals:

- ICMP: 2 seconds
- TCP connect: 10 seconds
- DNS: 15 seconds
- HTTPS: 30 seconds
- Interface snapshot: 30 seconds
- Route snapshot: 15 minutes
- Public-IP check: 5 minutes
- Speed test: 30 minutes

A speed test every 5 minutes in a 10-minute active session should run at active-time offsets `00:00` and `05:00`. It should not depend on the latency-probe loop.

## Packet Loss

Packet loss is calculated from ICMP measurements only.

Separate metrics are reported for:

- ICMP packet loss
- TCP connection-failure rate
- DNS failure rate
- HTTPS failure rate
- Speed-test failure rate

Reports must never label DNS, TLS, HTTP, or speed-test failures as packet loss.

## Jitter

Jitter is calculated only from successful ICMP RTT samples using mean absolute difference between consecutive samples ordered by timestamp.

The series key is:

- session
- protocol
- target name
- target host
- address family
- probe stream

Gateway ICMP, external ICMP targets, address families, TCP connect duration, DNS duration, and HTTPS duration are separate series. DNS, TCP, and HTTPS are never jitter inputs. A series with fewer than 3 successful ICMP samples has no jitter value and should display as insufficient samples.

## Quick Test

Quick Test is a snapshot and may miss intermittent problems.

It must use at least 20 probes per selected latency target, with a reasonable delay between probes. The current default is 20 probes spaced 250 milliseconds apart.

Quick Test should include latency, packet loss, jitter, DNS, HTTPS, download estimate, upload estimate, analysis, and persistence. Speed testing must be clearly labeled as full speed test, built-in throughput estimate, or unavailable.

The expected Quick Test stage order is: detecting network, testing router, testing internet latency, testing TCP, testing DNS, testing HTTPS, warming up download, measuring download, warming up upload, measuring upload, calculating statistics, and saving results. A complete built-in throughput Quick Test may take roughly 25 to 40 seconds.

## Throughput Validity

Built-in speed rows must include provider, endpoint, actual bytes, global active/setup/transfer/warmup durations, stream count, HTTP version, status, methodology version, and safe failure details.

Download and upload Mbps must be calculated from one synchronized global measurement window:

`sum(bytes transferred by all workers during the global window) * 8 / global wall-clock seconds / 1,000,000`

Per-stream active read/write durations are diagnostic evidence only and must not replace the global denominator. GitHub-hosted runners validate execution and evidence capture only; they are not reference networks for accuracy claims.

Only `Valid` and `Degraded` rows from the current methodology may be displayed as numeric dashboard/history speeds. Invalid, insufficient-duration, canceled, endpoint-limited, unavailable, and legacy rows remain visible as evidence but are not aggregated into valid speed summaries.

## Fault-Domain Language

Diagnosis must remain conservative:

- Gateway instability with external failures suggests local network instability.
- Stable gateway with simultaneous failures across multiple independent external targets suggests possible modem, ISP, or upstream routing trouble.
- DNS failure with direct connectivity suggests DNS trouble.
- One target failing while others work suggests target-specific or route-specific trouble.
- Anything weaker should be inconclusive.

Never claim certainty.
