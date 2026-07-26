# 0018 — TDS/VAT tax engine placement (Q14, forward-looking)

- **Status:** Accepted
- **Note:** forward-looking — no MVP build; shapes the money model now
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q14 (§3.4)

## Context

Post-MVP needs configurable withholding (TDS on doctor/referrer/supplier payouts) and VAT on applicable services, with treasury-deposit (TR Form 6) and VAT reports. Q14 asks *where* tax computation lives relative to billing & payouts so the MVP money design doesn't preclude it.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Tax logic inside each billing/payout module | Local convenience | Scattered rates and rounding — the same disease C7 forbids for approvals |
| External tax service | Isolation | A network hop inside invoice math on an offline-first box |
| **One shared tax kernel service, applied at document level (chosen)** | Single rate table, single rounding rule, one audit story; in-process (no RAM/hop cost) | Kernel API must be designed before first real use |

## Decision

**Placement:** a kernel-level tax service (same tier as the approval engine) that, given a document (invoice or payout statement) and effective date, returns tax lines from **effective-dated, configurable rate tables** (same versioning pattern as rate plans, C6). Billing/payout modules attach the returned lines; they never compute rates themselves.

**MVP obligations (built now, cheap):** invoice/charge lines carry a nullable `tax_amount` and `tax_code` (zero/absent today); totals math already separates gross → discount → tax → net so introducing VAT later changes data, not arithmetic; the whole-taka rounding rule (`03-data-model.md` §6) is defined to include the tax step; payout attribution data (ADR-0017) gives TDS its base amounts. Rate configuration screens, TR Form 6 and VAT reporting arrive with the Accounts module.

## Consequences

- VAT introduction becomes: populate rate tables, flip service tax codes, reports — no invoice-schema migration, historical invoices untouched (they carry their own resolved amounts, C6).
- Cost accepted: two dormant columns and a totals convention carried by MVP tests.

## Reversal trigger

NBR rule changes demanding per-line (not per-document) tax attribution or e-invoicing integration — re-open placement with the Accounts module spec; the document-level seam is the compatible starting point.
