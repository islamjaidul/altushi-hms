# 0006 — S2: Money spine (registration → invoice → payment)

- **Status:** Done
- **Date:** 2026-07-26
- **PRD ref:** §9A.2 modules 1 & 4, §7 (UX binding), §11 (Invoice/Counter machines), §5A
- **MVP:** in scope

## Problem

S1's walking skeleton has no product behaviour. The demo's first half — register a patient in ≤ 60 s,
bill an OPD service, take payment, leave a due — needs the registration and billing cores with the
G6 money invariants as executable tests.

## Requirements

- [M] Patient registration: UHID issue (ADR-0004), dup-warning (phone/phonetic), age⇄DOB (edge 26),
      unknown-emergency (edge 25), no-phone (edge 24); patient directory with trigram search.
- [M] Encounter + charge lines (C2 spine, 02 §2.3) + OPD invoice with frozen lines, resolved prices,
      whole-taka rounding (03 §6); receipts/tenders incl. partial payment and due; row-locked due
      collection (ADR-0015).
- [M] Counter sessions: one open per counter (constraint), open with float; receipts bind to session.
- [M] G6 invariants as tests on real Postgres: `net = gross − discount + tax + rounding_adj`;
      `Σ receipts + due = net`; no DELETE on financial rows; audit event in-transaction.
- [M] Concurrency: parallel due collection on one invoice never over-collects (G7).
- [S] Registration + OPD billing screens on templates 4/2; UHID card PDF.

## Acceptance criteria

1. Money-invariant + concurrency tests green against Testcontainers Postgres.
2. Golden-thread first half runs: register → encounter → invoice → pay → due visible.
3. Invoice numbers gap-free under parallel billing (reuses S1 harness pattern).

## Out of scope

Rate versions/discount approvals (S3); day-close computation (S4); dup-merge UI (S4+).

## Risks / open questions

None new — patterns fixed by ADR-0015 and 03 §4/§6.
