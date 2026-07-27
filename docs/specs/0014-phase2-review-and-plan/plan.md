# 0014 — Plan

## Approved: 2026-07-27

Executed per `docs/architect_review_prompt.md` deliverables A and B.

1. **Verify before judging.** Build the solution; run all .NET tests; reset to a fresh
   database (`hms_verify`, created alongside the existing dev DB rather than dropping it);
   run `golden-thread.py`, `discount-and-dues.py`, then the full Playwright suite.
2. **Verify the handoff's claims in source**, not by trust: folio XOR check + line
   attribution in the Billing migrations/`BillDbContext`; `FOR UPDATE` paths in
   `BillingService`; the four search contracts (`Dues`, `Refund`, `Registration/Index`,
   `Admin/Audit`); the `Reports` custom-range branch; orphaned `typeahead.js`;
   `PermissionClaimsFactory` cookie stamping; `const BranchId = 1`; the regex
   `CrossContextQueryTests`; QuestPDF's consumer set.
3. **Write `docs/architecture/10-mvp-review.md`** — rule on the input layer first; ADR
   compliance table with drift named; money-spine concurrency judgement; folio-seam
   proof; rulings on the four deliberate decisions; debt ranked with dispositions.
4. **Write `docs/architecture/11-build-plan-phase2.md`** — Wave 0 (structural) then six
   waves covering all fourteen modules + R2/R3/R4; per-module structural work, migration
   risk, validatable-vs-buildable honesty; deviations from PRD §9 argued inline.
5. **Record decisions:** ADR-0020 (input layer), ADR-0022 (upgrade-path testing),
   ADR-0019 amendment (security-stamp revalidation), ADR-0021 reserved for the stock
   kernel; append P13–P16 to `09-questions-for-pm.md`.
