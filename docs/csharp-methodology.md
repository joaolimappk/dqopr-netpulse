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

Jitter is calculated per target and probe method using mean absolute difference between consecutive successful latency samples ordered by timestamp.

Gateway ICMP, external ICMP, TCP connect duration, DNS duration, and HTTPS duration are separate series. A one-sample series has no jitter value and should display as unavailable or waiting for samples.

## Quick Test

Quick Test is a snapshot and may miss intermittent problems.

It must use a configurable burst of 10 to 20 probes per selected latency target, with a reasonable delay between probes. The current default is 12 probes spaced 250 milliseconds apart.

Quick Test should include latency, packet loss, jitter, DNS, HTTPS, download estimate, upload estimate, analysis, and persistence. Speed testing must be clearly labeled as full speed test, built-in throughput estimate, or unavailable.

## Fault-Domain Language

Diagnosis must remain conservative:

- Gateway instability with external failures suggests local network instability.
- Stable gateway with simultaneous failures across multiple independent external targets suggests possible modem, ISP, or upstream routing trouble.
- DNS failure with direct connectivity suggests DNS trouble.
- One target failing while others work suggests target-specific or route-specific trouble.
- Anything weaker should be inconclusive.

Never claim certainty.
