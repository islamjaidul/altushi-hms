# 0017 — BEFTN bank-payout batch export readiness (Q13, forward-looking)

- **Status:** Accepted
- **Note:** forward-looking — no MVP build; shapes the money model now
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q13 (§3.4, §5A-15)

## Context

Payout modules (M16/M17/M19 — post-MVP, §9A.3) must emit bank-uploadable BEFTN batch files per payee type; formats vary per bank, treated as pluggable. Q13's real demand on the *MVP* is that the data model not make this painful later.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Ignore until payouts are built | Zero MVP cost | Bank-account data missing from masters created during construction → painful backfill across live data (C2 failure mode in miniature) |
| Build export engine now | "Ready" | Violates scope C1; no payouts exist to export |
| **Model the data now, engine later (chosen)** | Masters captured during construction are payout-complete; engine arrives with the payout modules | Small master-data surface added to MVP screens |

## Decision

**In MVP:** a reusable `bank_account` child entity (account name/number, bank, branch, routing number, active flags, effective dates) attachable to doctors/consultants and referrers (and later suppliers/employees — same shape). Captured optionally on the MVP's doctor/referrer masters (module 7 scope §9A.2 already includes these masters); validation of routing-number format only (no bank connectivity). Every invoice line already attributes doctor and referrer (ADR-0003 seams), so future accruals compute from day-one data.

**Post-MVP (recorded intent):** a payout run produces an immutable batch (approval-gated per §12) rendered to a per-bank file format via pluggable serializers (format plugins configured per hospital's bank); acknowledgement/return handling reconciles batch line status. Formats are data-driven templates where possible so a new bank is configuration, not a release.

## Consequences

- Construction-phase data entry (the F1 lock-in window) captures bank details once, when the hospital is motivated — the payout modules land on complete masters.
- MVP cost: one entity + optional form section; no engine, no bank claims made to customers yet.

## Reversal trigger

Bangladesh Bank standardising a single BEFTN file format across banks (verify when payout modules are specced) would collapse the plugin design to one serializer — a simplification, adopted when proven.
