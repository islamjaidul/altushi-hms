# 0027 — Payroll policy is effective-dated configuration, never code

- **Status:** Accepted
- **Date:** 2026-07-31
- **Relates to:** ADR-0018 (TDS/VAT engine placement), ADR-0017 (BEFTN readiness), hard rules 3 and 5
- **Spec:** `docs/specs/0034-hrm-product-line/`

## Context

Payroll is arithmetic over rules that are simultaneously **jurisdictional**, **contractual** and
**mutable**:

- *Jurisdictional* — NBR salary tax slabs, Labour Act 2006 leave entitlements, gratuity, provident
  fund rules. **None of these appear anywhere in the PRD or the architecture docs.** A grep across the
  whole documentation tree for gratuity, festival bonus, EPF, labour law, tax slab, maternity, earned
  leave, casual leave, service book and final settlement returns nothing. The only statutory content
  recorded is TDS/TR Form 6, PF, Welfare Fund, VAT and BEFTN in §3.4 — named, not specified.
- *Contractual* — overtime multipliers, grace minutes, weekly-off patterns (Friday only, or Friday and
  Saturday), holiday-work pay, festival bonus months. These vary per employer and are negotiated, not
  legislated.
- *Mutable* — all of the above change, and a salary sheet from eighteen months ago must still
  reproduce the figures it was approved with.

CLAUDE.md rule 3 forbids asserting a regulation that is not verified. `11-build-plan-phase2.md` says
outright that payroll math is jurisdiction-specific and *"anything unverifiable about BD statutory
rules goes to the PM, not invented."* Precedent already exists: **P14** ruled that BEFTN file formats
and tax rates would not be fabricated, and that the accrual ledger builds regardless.

The temptation is a `BdTaxRules.cs` with slab constants. That file would be a liability the day it is
written: unverifiable, silently stale, wrong for the employer who negotiated something different, and
incapable of reproducing history after the first amendment.

## Decision

**Every rate, threshold, slab, multiplier, entitlement and formula selector is a row in an
effective-dated table, entered by the customer. No statutory or contractual constant appears in C#.**

Policy tables in the `hr` schema — `tax_slab`, `pf_policy`, `gratuity_rule`, `overtime_rule`,
`grace_time_rule`, `holiday_pay_policy`, `deduction_rule`, `leave_policy`, `weekly_off_pattern`,
`holiday_calendar` — each carrying `effective_from` and an optional `effective_to`.

Rules that follow from this:

1. **Resolution is by date, always.** A payroll run resolves every policy as of the period it is
   computing, exactly as `RateResolver` resolves a price as of a business day. Re-running a past
   month therefore reproduces it. This is hard rule 5 — *a historical invoice must always reproduce
   its historical price* — applied to salary.
2. **Overlap is impossible, not merely validated.** Effective ranges use Postgres exclusion
   constraints (`btree_gist` is already installed by `deploy/db-init/01-roles.sh`). Two overlapping
   tax-slab sets or pay structures cannot exist to be resolved ambiguously.
3. **The engine is formula-shaped, not rule-shaped.** C# knows how to apply *a* progressive slab set,
   *a* percentage-of-basic contribution, *a* multiplier on overtime hours. It does not know what any
   BD rate is. Adding a new employer's scheme is data entry; adding a genuinely new *kind* of
   computation is code, and that is the correct boundary.
4. **The product ships empty, not fabricated.** No seeded slabs, no assumed 8.33% festival bonus, no
   presumed gratuity multiplier. First-run configuration is a documented onboarding step. A seeded
   BD default pack is a separate spec, gated on verified sources — raised as **P26**.
5. **Day-count convention is itself policy.** Whether a mid-month joiner is prorated over calendar
   days or a fixed thirty is an employer decision, not a constant. Both are selectable rows.
6. **A locked run pins what it used.** Each payroll line records the policy version and pay-structure
   version it resolved. Reproduction does not depend on the resolution logic being unchanged — the
   figures are attributable to identified rows, per hard rule 4's attribution requirement.

## Consequences

- The product is legally safe to sell to any Bangladeshi employer, and portable to any other
  jurisdiction without a code change — which is what makes a general-business HRM SKU viable at all.
- Onboarding is heavier: a customer cannot run payroll until policy is configured. Mitigated by an
  explicit setup checklist and by refusing a payroll run with a named missing policy rather than
  silently computing zero.
- Support conversations become "which policy row was effective in March", which is answerable from
  the database, instead of "which version of the software did you have".
- We carry no risk of shipping a stale statutory constant after an NBR amendment, and no risk of
  asserting a regulation we never verified.

## Reversal trigger

If verified, dated, authoritative BD statutory sources are supplied and the PM accepts ownership of
keeping them current, seed them as a **default policy pack** — still rows, still effective-dated,
still overridable per employer. That is a data change under this ADR, not a reversal of it. A genuine
reversal would be hardcoding rates in C#, and nothing observed so far justifies it.
</content>
