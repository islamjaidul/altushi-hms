# 0013 — Backup, restore & disaster recovery (Q9)

- **Status:** Accepted
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q9 (§8 N3; edge cases 8, 32)

## Context

§8 N3: daily backup minimum, ambition of a few-minutes recovery point, and hospitals can always export their own data. "Show me a backup restore" may be asked live in the demo (edge 8). Disk is small (edge 32). One stateful service (ADR-0002) keeps this tractable.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Nightly dump only | Simple | Up to 24 h data loss — fails the few-minutes ambition |
| Streaming replica to second host | Near-zero RPO | Second machine most customers won't buy at MVP stage |
| **WAL archiving + nightly base backup (chosen)** | Minutes-level RPO on one box's disk + off-site copy; standard Postgres tooling | Restore is a procedure, not a button — mitigated by scripting + drills |

## Decision

- **Continuous:** WAL archiving (`archive_command`) to a separate disk path/volume, target RPO ≈ ≤ 5 minutes (checkpoint/archive tuning; estimate until measured).
- **Nightly:** scripted base backup (`pg_dump` custom-format for portability **and** a physical base backup for PITR), plus config/uploaded-file volumes, bundled with a manifest (versions, hashes, audit hash-chain heads — ADR-0011).
- **Off-site:** encrypted push of nightly bundle + WAL segments to vendor cloud storage or customer NAS **when internet exists**; queued while offline (ADR-0006).
- **Retention on-box:** 7 nightly + WAL window; older ages off-site. Disk watermarks alert at 70/85% and pause PDF-archive growth before the DB is threatened (edge 32).
- **Restore:** one script, two modes — full PITR to a timestamp, and fresh-machine restore. **Drilled**: the restore script runs against a scratch container in CI weekly and is a rehearsed ≤ 5-minute demo item (edge 8). A restore that hasn't been drilled is treated as no backup.
- **Customer export** (N3): admin-triggered full export (SQL dump + CSVs of business tables + PDFs) — their data, no lock-in, matching §2.3 positioning.

## Consequences

- RPO minutes / RTO ≤ ~30 min on replacement hardware (estimate; drill-timed). Demo restore is scripted and safe (targets a scratch instance, never the live demo DB mid-presentation).
- Cost accepted: WAL archiving disk overhead and the discipline of watching the drill job.

## Reversal trigger

Customers buying second on-site hardware (or the hosted variant dominating) → move to streaming replication with automatic failover and retire PITR-as-primary.
