# 0002 — PostgreSQL as the only stateful service

- **Status:** Accepted
- **Date:** 2026-07-26

## Context

§8 N4 (concurrency correctness), N3 (recover to last few minutes), C5/C6 (append-only audit, effective-dated prices) demand a transactional store with real constraints. The 3 GB budget punishes every additional stateful component. Type-ahead search (§7 U5), a job queue (SMS, background posting) and audit storage are also needed — the cheap temptation is Redis + a search engine + a broker, three more resident processes.

## Options considered

| Option | Pros | Cons | RAM cost (estimate) |
|---|---|---|---|
| **PostgreSQL for everything (chosen)** | One process to budget, back up, and crash-recover; SKIP LOCKED queues, trigram/prefix indexes for type-ahead, JSONB for audit before/after images; strong constraints for money rules | Queue/search are "good enough", not best-of-breed; discipline needed on index bloat | ~400–550 MB tuned (shared_buffers 256 MB) |
| MySQL/MariaDB | Familiar in market | Weaker transactional DDL, no SKIP LOCKED parity at the time of comparison in our usage shape, weaker JSON/trigram story | similar |
| + Redis (cache/queue) | Faster queue, sessions | +50–150 MB resident, second failure domain, second backup story — nothing in §14 volumes (≤ 1,200 invoices/day) needs it | +≥100 MB |
| + Elasticsearch (search) | Fancy search | ~1 GB JVM minimum — instantly blows the budget; patient search is prefix/phonetic over ≤ 1 M rows, well inside trigram index territory | +≥1 GB |
| SQLite | Tiny | Multi-writer counter concurrency (§8 N4) is exactly its weak spot | ~0 |

## Decision

**A single PostgreSQL instance** (current stable major at build start) carries: relational data, the job queue (`jobs` table drained with `FOR UPDATE SKIP LOCKED`), audit events (append-only table, JSONB diffs), and type-ahead search (`pg_trgm` + prefix indexes on patients/catalog). Financial invariants live in the schema: CHECK constraints, partial unique indexes, no-DELETE grants on financial tables, triggers for audit on the money paths. App connects through a small fixed pool (PgBouncer not needed at MVP connection counts — revisit at scale-up).

## Consequences

- One backup/restore story (ADR-0013), one crash-recovery story (WAL) — this is what makes the power-cut edge case (7) tractable.
- Queue latency is polling-based (~1 s worst case) — fine against §8 N7 (~1 min SMS).
- At §14 design ceiling (150 operators) Postgres wants its own host — the compose topology already isolates it, so separation is an operation, not a migration.

## Reversal trigger

Measured p95 > 1 s on billing search/save at 25 operators after index/tuning passes (move DB to dedicated hardware first); or SMS/job volume growing past ~10 k jobs/day sustained (introduce a real broker then, on bigger hardware).
