# 0022 — Full pharmacy-module coverage: staff-sale tagging and a dead audit search

- **Status:** Done
- **Date:** 2026-07-27
- **PRD ref:** §5 M11, §5A-11 (staff POS variant), §5 M21 / §8 N5 (audit), spec 0016 matrix
- **MVP:** post-MVP — corrective work inside M11 Pharmacy and M21 Admin

## Problem

Asked whether *every* pharmacy feature works, the honest answer was no evidence either way:
`pharmacy-thread.py` walked the module's spine (PO → GRN → FEFO sale → refund → transfer →
audit) but **eight of the seventeen matrix rows had no end-to-end coverage at all** — company
and product registration, the reorder shortlist, supplier payments, supplier replacement,
damage write-off, outlet creation, the sales/purchase statements, and the staff-sale variant.

Walking all seventeen found the module in better shape than the coverage suggested — and two
real defects:

1. **The staff-sale tag existed only inside a discount's approval reason.** A staff member
   buying at full MRP — an ordinary transaction — ticked the box and nothing whatsoever was
   recorded. 5A-11 asks for staff-sale *tagging*; what existed was tagging-if-discounted.
2. **The audit viewer's search box had never worked.** `after` is a `jsonb` column and the
   predicate asked Postgres for `ILIKE` on jsonb, which throws `42883` — so *every* search
   term returned a 500. The audit trail is the product's accountability surface (§8 N5,
   hard rule 4); a search that always errors makes it unusable for the one job it has.

## Requirements

- [M] Every row of the spec 0016 matrix is exercised end to end by a script that can be re-run.
- [M] A staff sale is tagged as a **fact about the sale**, recorded whether or not a discount
  was applied, and attributable to the operator who made it.
- [M] The audit viewer's search works across actor, entity, action and payload, with the
  predicate evaluated in SQL (ADR-0020 — the audit table is the largest in the product).

## Acceptance criteria

1. `eng/verify/pharmacy-full.py` walks all seventeen rows and passes on a fresh and a dirty
   database; it creates everything it needs (company, product, supplier, outlet, batches).
2. A staff sale with no discount writes a `pharmacy.sale.staff` audit event naming the invoice
   and the person, at tier 2.
3. Searching `/admin/audit` for any term returns 200 and narrows the result set.
4. Existing verification stays green.

## Out of scope

- Staff **pricing** policy, still P17. This spec makes the tag durable; what a staff member
  pays remains a business decision.

## Risks / open questions

- None new. P17 unchanged.
