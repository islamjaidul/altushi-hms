# 0015 — Concurrency control for money, serials, beds and stock (Q11)

- **Status:** Accepted
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q11 (§8 N4; C9; edge case 28)

## Context

N4 is binding: no double-booked serial/bed, no double-collected due, no lost charge, under 30–100 simultaneous operators — and the outcome of a collision must be **user-comprehensible, never a silent overwrite** (edge 28). §11 defines the state machines whose transitions must be race-safe.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| App-level locks (in-memory) | Easy | Break on multi-worker scale-up (ADR-0003's growth path) and on crash |
| Serializable isolation everywhere | Strong | Retry storms at counters; unnecessary — conflicts are localised to known hot rows |
| **DB constraints + targeted row locks + optimistic versions (chosen)** | Correctness enforced where it's cheapest and survives scale-out | Requires per-workflow analysis (done below) |

## Decision

Three mechanisms, each applied where it fits:

1. **Uniqueness by constraint (make the race impossible):** one serial per `(doctor, date, serial_no)`; one open counter session per `(counter)` (partial unique on open state); one number per `(series, value)` (ADR-0004); one active rate version per `(item, timepoint)` (exclusion constraint on the effective range). A losing transaction gets a constraint violation, surfaced as "Serial 14 was just taken — next free is 15" (the app retries the *allocation*, not the user).
2. **Row locks on balances and allocators (`SELECT … FOR UPDATE`):** due collection locks the invoice's due row (two collectors can't both take the last ৳500 — one succeeds, the other sees the refreshed balance); number series rows (ADR-0004); future: bed assignment and stock batches use the same pattern (seam noted for M6/M11).
3. **Optimistic concurrency on human-edited documents** (draft invoices, results before verify, masters): a version column checked on save; on conflict the operator gets a plain-English "Reloaded — someone else saved changes" flow that preserves their entries where mergeable (§7 U8's consequence-preview tone). Never last-write-wins.

**State-machine transitions** (§11) are guarded `UPDATE … WHERE state = 'expected'` — an affected-rows-0 result is a comprehensible "already moved on" message (e.g., sample already received). All money mutations are single transactions (invoice + lines + number + audit), so "lost charge" cannot occur between screens (§7 U2's "system carries the data").

## Consequences

- Correctness survives the scale-up path (multi-worker, separated DB) because it lives in the database.
- Cost accepted: each new workflow must name its hot rows and mechanism — a required section in future module specs.
- Money-path tests (DoD) include concurrency harness cases: parallel serial issuance, parallel due collection, parallel day-close vs. late receipt.

## Reversal trigger

Measured lock contention (p95 lock wait > 100 ms on due/number rows under demo-scale load tests) → narrow the transactions further or introduce per-counter sharding of allocators; a move to serializable isolation would need evidence targeted locks are missing real anomalies.
