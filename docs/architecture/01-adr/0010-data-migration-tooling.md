# 0010 — Data migration & bulk import tooling (Q6)

- **Status:** Accepted
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q6 (edge cases 11–12)

## Context

Q6: every deal replaces something (competitor system or Excel); minimum imports are patient master, test catalog, opening stock, opening ledger balances. The construction-phase customer's price list arrives as a spreadsheet (edge 12) and masters must accept placeholders (edge 11). The F1 lock-in (§9A.1) *is* configuration-during-construction — import quality is a sales feature, not plumbing.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Ad-hoc SQL per implementation | Zero product cost | Violates auditability; unrepeatable; depends on vendor engineer per site (conflicts with §16.1 A4) |
| Full ETL platform | Powerful | RAM + complexity far beyond need |
| **Product-native import pipeline (chosen)** | Repeatable, validated, auditable; demo-able during sales (F1) | Build cost in MVP for catalog/price/bed imports |

## Decision

A shared **staged import pipeline** in the Admin module: upload spreadsheet (XLSX/CSV) → column mapping (saved as reusable template per source) → validation pass producing a per-row error report (downloadable, fix-and-re-upload; idempotent upsert by natural key so re-import corrects rather than duplicates — edge 12) → commit as one audited batch (who, when, source file hash) → rollback = reversal batch, honouring C5.

**MVP importers** (§9A.2 module 7): test catalog with prices & TATs, service/bed inventory, doctors/departments, users, referrers. **Post-MVP importers** (same pipeline): patient master, opening stock, opening ledger balances — the pipeline is generic; each importer is a mapping + validator set.

**Placeholders (edge 11):** masters carry a `provisional` flag (missing licence no., unconfirmed price, not-yet-physical bed). Provisional records work fully during construction; a **go-live checklist report** lists everything still provisional, and the go-live switch (config) can require sign-off before provisional records are billable. Tightening is config, not code.

## Consequences

- Implementation time (≤ 45 days/site target, §15) leans on saved mapping templates per competitor source.
- Import batches are first-class audit objects — an implementation error is diagnosable and reversible months later.

## Reversal trigger

If pilot implementations show sources too messy for template mapping (free-form Excel chaos), add a vendor-side cleansing toolkit *outside* the product rather than growing ETL complexity inside the 3 GB box.
