# 0011 — Audit-trail depth & storage (Q7)

- **Status:** Accepted
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q7 (§8 N5, N10; C5)

## Context

Every financial and clinical write must be attributable (user, time); audit is append-only; retention is indefinite (§8 N10) on a small disk (edge 32). Q7 delegates the depth-vs-storage trade.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Full row-image on every write, every table | Maximal forensics | Storage growth × §14 volumes; noise drowns signal |
| App-log-only auditing | Cheap | Logs are not transactional evidence; fails C5 attribution |
| **Tiered, in-transaction audit (chosen)** | Evidence-grade where it matters, summary elsewhere; bounded growth | Requires an explicit tier list kept current |

## Decision

- **Tier 1 — money & clinical documents** (invoice, receipt, discount/refund/approval decisions, day-close, rate changes, results/verification, user/role/permission changes): append-only `audit_event` row written **in the same DB transaction** as the change — actor, timestamp (UTC), entity ref, action, JSONB before/after of business fields, request correlation id. Enforced by the kernel's audit writer + DB triggers on the financial tables as a second net.
- **Tier 2 — masters & config:** audit event with after-image only (before recoverable from prior event).
- **Tier 3 — reads:** not audited, **except** privileged exports (patient phone lists — §8 N5's named leak vector) and permission-denied attempts, which log actor+scope.
- **Storage bounds:** audit rows are compact JSONB of business fields (not whole ORM entities); monthly partitions; partitions older than a configurable horizon compress (`pg_dump` archive to backup store) but **never delete** (N10). Estimate at §14 typical volumes: low single-digit GB/year (mark: estimate, validate in pilot) — disk sizing in `06-deployment.md`.
- **Tamper-evidence:** nightly job chains a hash over each day's audit partition and stores it with the backup manifest — cheap, detects retroactive edits.
- No `UPDATE`/`DELETE` grants on `audit_event` for the app role; corrections to audit are themselves new events.

## Consequences

- Auditors get a single queryable timeline (the design reference's admin screens surface it); support gets correlation ids.
- Cost accepted: the tier list is a living document in `03-data-model.md`; adding a module means classifying its writes.

## Reversal trigger

Pilot-measured audit growth exceeding ~10 GB/year at a typical site, or a regulator/customer demanding cryptographic (not hash-chain) evidence — revisit storage format and signing.
