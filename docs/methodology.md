# Methodology

DQOPR NetPulse is designed to produce defensible evidence, not absolute proof. Reports should describe what the data supports and clearly state limitations.

## Probe Strategy

NetPulse should combine:

- Local gateway checks.
- Multiple independent public target checks.
- TCP connection checks on common service ports such as 443.
- DNS resolution timing and failure tracking.
- HTTPS request timing and status tracking.
- Route inspection during major incidents and at conservative intervals.
- Optional speed tests at bandwidth-conscious intervals.

ICMP is useful, but ICMP-only evidence is not conclusive because some networks rate-limit, deprioritize, or block ICMP.

## Default Targets

The initial defaults are:

- Cloudflare: `1.1.1.1`
- Google: `8.8.8.8`
- Quad9: `9.9.9.9`
- OpenDNS: `208.67.222.222`

Users should be able to enable, disable, or add targets.

## Default Intervals

| Probe | Default interval |
| --- | --- |
| Latency | 2 seconds |
| TCP | 10 seconds |
| DNS | 15 seconds |
| HTTPS | 30 seconds |
| Route inspection | 15 minutes and on major incident start |
| Speed test | 30 minutes |

Speed tests should never run continuously or every few seconds because they consume bandwidth and can affect the connection being measured.

## Latency And Jitter

Latency spike detection should combine absolute and baseline-relative rules. A spike may be detected when latency exceeds a configured high-latency threshold or rises substantially above a recent rolling median.

Jitter must use a documented calculation. The preferred first implementation is median absolute difference between consecutive successful latency samples within a rolling window, reported in milliseconds. Reports must disclose the selected method.

## Packet Loss

Packet loss analysis should track:

- Individual missed probes.
- Rolling packet-loss percentage.
- Consecutive losses.
- Short bursts.
- Sustained loss.
- Whether loss affects one target or multiple independent targets.

A single public target failure should be classified as isolated or inconclusive unless other targets show related failures.

## Incident Classification

Incidents should group related failures and include:

- Start and end time.
- Duration.
- Severity.
- Affected tests and targets.
- Worst latency.
- Packet-loss percentage.
- Consecutive failures.
- Gateway, external, DNS, HTTPS, and speed-test context.
- Probable fault domain.
- Confidence.
- Plain-language explanation.
- Supporting measurement references.

## Fault-Domain Reasoning

| Evidence | Recommended assessment |
| --- | --- |
| Gateway loss or severe gateway latency during public-target failures | Possible local network, Wi-Fi, cable, adapter, or router problem. |
| Stable gateway with simultaneous failures across multiple public targets | Probable modem, ISP, or upstream issue. |
| Direct-IP connectivity works while DNS fails or is unusually slow | DNS-related issue. |
| Only one public target fails | Target-specific or route-specific issue; inconclusive for general outage. |
| Latency rises during upload/download but idle latency is normal | Possible congestion or bufferbloat. |

Use confidence labels: High confidence, Moderate confidence, Low confidence, or Inconclusive.

## Contracted Speed Handling

When contracted speeds are known, calculate measured speed as a percentage of the contracted plan. When contracted speeds are unknown, report measured values without percentage-of-plan claims and state that contracted speed was not provided.

## Report Language

Reports should be ISP-friendly and cautious. Prefer "suggests a probable issue" over "proves the ISP caused this".
