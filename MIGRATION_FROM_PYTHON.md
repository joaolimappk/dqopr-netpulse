# Migration From Python

The Python implementation remains in `src/dqopr_netpulse` during the C# rewrite.

Status: **Legacy prototype — not recommended for production evidence collection**.

## What To Preserve

- Product concept and user workflow.
- Conservative diagnostic wording.
- Local-first privacy posture.
- SQLite durability goals.
- Report and export concepts.
- Existing Python tests as behavioral examples where statistically correct.

## What Not To Preserve As Truth

- Mixed-method packet-loss summaries.
- Jitter calculations that compare incompatible targets or methods.
- Probe scheduling that effectively runs unrelated probes on the latency interval.
- Speed testing that can block ordinary monitoring or imply ISP-grade measurement from a small fallback transfer.

## Database Compatibility Plan

The C# schema starts separately at schema version 1. Before C# replaces the Python alpha, the project must implement one of:

- A read-only compatibility importer for Python SQLite sessions.
- A backup-and-migrate command that copies Python databases before conversion.
- A documented decision to keep Python and C# session stores separate for prereleases.

No migration code may modify an existing Python database in place without first creating a backup.
