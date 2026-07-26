# 0008 — S4: LIS + delivery + day-close

- **Status:** Done
- **Date:** 2026-07-26
- **PRD ref:** §9A.2 modules 6 & 4 (day-close), §11 (Sample/Result/Counter machines), §6.6
- **MVP:** in scope

## Problem
The golden thread's second half: sample chain (collect → receive → result → verify → deliver)
and the only door to accounting — counter day-close with variance, carry-close and the immutable
summary holding rows the future Accounts module consumes.

## Requirements
- [M] Sample M:N model (edge 33): one barcode per sample, samples↔tests join; rejection spawns a
      child sample (chain preserved); reprint reuses the barcode with audit (edge 27).
- [M] Result versions: entered → verified (e-sign identity incl. reporting consultant, edge 34);
      amendment = v2+, v1 immutable (edge 22).
- [M] Day-close: expected-from-receipts vs counted, variance recorded not blocked (edge 18);
      session locks its receipts (trigger from S2); immutable DayCloseSummary versioned rows.
- [M] Carry-close (edge 17): stale session closes against its own business day ⚿ before a new
      session opens; two days never merge.
- [M] Refund after day-close (edge 20): negative receipt in the CURRENT session linked to the
      original; closed sessions stay closed. G7 test: day-close vs late receipt race.
- [S] Pipeline board + result entry screens; report-ready notification event.

## Acceptance criteria
1. State machines table-driven-tested incl. illegal moves (G9).
2. Day-close arithmetic: Σ tender totals = Σ session receipts (G6) proven on real Postgres.
3. Concurrency: closing a session while a receipt lands in it → exactly one consistent outcome.
4. Refund-after-close leaves original session/receipts untouched.

## Out of scope
Notifications dispatch (S5 wiring); dashboards (S5).

## Risks / open questions
P5 refundable-subset policy applies as data default.
