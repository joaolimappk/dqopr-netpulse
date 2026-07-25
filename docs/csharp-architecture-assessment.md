# C# Architecture Assessment

The Python alpha has useful product shape: domain models, configuration validation, an async monitoring engine, SQLite storage, conservative incident language, Quick Test orchestration, CSV/ZIP export, graph/report generation, and Windows packaging scaffolding.

The rewrite should preserve those product ideas but correct the parts that weaken evidence quality:

- Live probes must not all run on the latency interval.
- A slow HTTPS or speed test must not block latency probes.
- Packet loss must mean packet-oriented ICMP loss, not mixed DNS/TCP/HTTPS failures.
- DNS, TCP, HTTPS, and speed-test failures must be reported as separate rates.
- Jitter must be computed only within the same target and probe method.
- Quick Test must use a burst of samples, not a one-probe snapshot.
- Incidents must be stateful events with lifecycle, recovery, merging, and supporting context.
- Sleep/resume and interface changes must not be mislabeled as ISP outages.
- Reports must disclose methodology and uncertainty.

## Migration Risks

- Existing Python SQLite databases may need a compatibility reader or safe backup-and-migrate path.
- Existing report text may describe intended methodology more strongly than the alpha implementation actually does.
- DNS target metadata needs clearer separation between resolver path, hostname resolved, and public target labels.
- Speed-test provider behavior needs legal and technical review before bundling or invoking third-party CLIs.
- WPF and installer behavior require Windows validation; Linux builds cannot prove launch/install correctness.

## Dependency Position

Dependencies should remain conservative. The first C# milestone uses only xUnit test packages and SQLite packages beyond the .NET SDK. `SQLitePCLRaw.lib.e_sqlite3` is pinned to `2.1.12` to avoid the vulnerable transitive version restored by the initial SQLite package graph.
