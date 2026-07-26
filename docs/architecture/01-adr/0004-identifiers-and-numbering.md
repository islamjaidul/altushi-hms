# 0004 — Identifiers & numbering: UHID, invoice/receipt sequences, fiscal year

- **Status:** Accepted
- **Date:** 2026-07-26

## Context

Edge cases 15–16 of the brief: numbering must be **gap-free, collision-free under concurrency, and reset at fiscal-year boundaries** with the fiscal convention configurable; day-close and "today" need an explicit business-day boundary in a 24/7 hospital (Asia/Dhaka, no DST). C6 makes numbers financial evidence. Postgres `SEQUENCE`s are collision-free but **not** gap-free (rollback burns numbers) — a real constraint, not a style choice.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| DB sequences | Fast, no locks | Gaps on rollback — fails the gap-free requirement for money documents |
| UUIDs everywhere | Trivially concurrent | Human-hostile at counters (§7); auditors and printed documents need short serial numbers |
| **Counter rows under row lock (chosen, money documents)** | Gap-free & collision-free: number issued inside the committing transaction while holding the counter row lock | Serialises number issuance per scope — acceptable: lock is held milliseconds, scope is per counter/type/fiscal-year |

## Decision

- **Surrogate keys:** all tables use internal `bigint identity` PKs (never shown to users). Business numbers are separate columns with unique constraints.
- **Money-document numbers** (invoice, money receipt, day-close): issued from a `number_series` table keyed `(doc_type, branch_id, fiscal_year)`; the issuing transaction does `SELECT … FOR UPDATE`, increments, and uses the number — a rolled-back transaction rolls the counter back too, so no gaps. Format example `INV-2026-27-000123` (display format configurable per hospital).
- **UHID:** permanent, never fiscal-reset, format configurable (default `<prefix>-YY-serial`, matching the market pattern seen in the design reference, e.g. `ALT-26-00412`); generated the same way, scoped `(branch or facility, year)` per hospital config.
- **Fiscal year** is configuration (`fiscal_year_start`, default July 1 per Bangladesh convention — **confirm as PM/customer setting, not hardcode**; listed in `09-questions-for-pm.md`).
- **Business day** for day-close and "today" dashboards: configurable day boundary (default 00:00 Asia/Dhaka) with counter sessions attributed to the day they were **opened**; night shifts crossing midnight stay in one session, so nothing double-counts (edge 16/17). All timestamps stored UTC, displayed Asia/Dhaka.
- **Barcodes** carry the business number (sample no., UHID) in Code 128; reprint reuses the same number (edge 27) — a reprint is an audit event, never a new identity.

## Consequences

- Gap-free numbering serialises on the counter row: throughput per scope is bounded by lock hold time (estimate: trivially sufficient at §14 peaks of ~600 invoices/day; measure in the money-path tests).
- Fiscal-year rollover is data (`number_series` rows for the new year), not a deploy.

## Reversal trigger

Measured lock contention on `number_series` above ~50 ms p95 at demo-load testing → shard the series per counter (still gap-free within counter) and record the numbering convention change with the PM.
