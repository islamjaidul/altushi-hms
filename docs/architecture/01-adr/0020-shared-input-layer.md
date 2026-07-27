# 0020 — Shared input layer: one date, search and type-ahead contract product-wide

- **Status:** Accepted
- **Date:** 2026-07-27
- **Answers:** the input-control defects in `docs/architect_review_prompt.md`; `05-ui-architecture.md` §3; PRD §7 U3/U4/U5/U9/U13
- **Spec:** `docs/specs/0014-phase2-review-and-plan/` (decision) · Wave-0 build spec (implementation)

## Context

`05-ui-architecture.md` §3 specified a kernel-level interaction grammar — "one JS module, one
Razor tag helper" per capability — and the MVP shipped without it. The measurable result, all
confirmed in code (`10-mvp-review.md` §1): four different search contracts (two of which filter
a pre-truncated page and cannot find older records), a report date range silently ignored
unless a dropdown also says "Custom", two date-entry paradigms in one product, and an orphaned
`typeahead.js` no page binds while patient pickers are `<select>` lists of the 60 most recent
patients. Fourteen more modules are about to be built; each will hand-roll its own inputs
unless the contract exists first.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Fix the four defective screens individually | Smallest diff today | Treats symptoms; the 15th module re-creates the disease; U9 stays broken by construction |
| Carry into Phase 2, build the layer later | No delay to Pharmacy | Retrofit cost across 22 modules instead of 8; every interim module adds migration surface |
| **Build the layer now, before any Phase-2 module (chosen)** | One contract enforced product-wide before the surface triples; the defects become re-implementations, not spot fixes | Delays the first module by roughly one wave |

## Decision

Build the input layer as Wave-0 work, as three kernel-level components plus one behavioural rule:

1. **`hms-date`** — a tag helper + JS module for every date field. Forgiving text entry
   (`45`, `8 months`, `12/03/1980`, `1980-03-12`, `d/m/yy` — the registration parser promoted
   to the kernel), displays and echoes `dd MMM yyyy`, Asia/Dhaka, 44 px, keyboard-first.
   Native `<input type="date">` is **banned** (a CI grep, like the colour and external-host
   gates) because it renders browser-locale `mm/dd/yyyy` and defeats U13.
2. **`hms-search`** — one server-side search contract: the predicate goes **into the SQL query**
   (`ILIKE` / trigram), then take N — never fetch-then-filter. A shared page-model helper makes
   the correct pattern the path of least resistance; the two in-memory offenders
   (`/billing/dues`, `/billing/refund`) are re-implemented on it.
3. **`hms-typeahead`** — a trigram-indexed endpoint (patients first: name/UHID/phone; the same
   contract extends to catalog, doctors, suppliers) bound by the existing `typeahead.js`.
   2–3 characters → ranked results, arrow/Enter selection, never a free-text foreign key
   (§7 U5). Replaces the recent-60 `<select>` pickers.
4. **Date-range rule:** on any screen with both a period selector and From/To inputs, touching
   the dates **implies** the custom period — explicit dates always win. A submitted date range
   that does not affect the result is a defect class, banned product-wide.

New modules must use these components; hand-rolled date/search/type-ahead inputs are a review
rejection, and the Playwright suite asserts the contract per screen family.

## Consequences

- §7 U9 ("learn one module ≈ learn all") becomes structurally true for inputs; U13 forgiving
  dates and U5 type-ahead stop being per-screen accidents.
- The dues/refund unfindable-record bug and the reports silent-range bug are fixed by
  re-implementation onto the contract, with regression tests at the contract level.
- Cost accepted: Pharmacy starts one wave later; every existing screen with a date or search
  input is touched once (mechanical, low-risk, covered by the existing 104-test suite).

## Reversal trigger

If the shared components prove too rigid for a genuinely novel input need (e.g., M9 analyzer
timestamps), the escape hatch is a new capability added to the kernel contract — never a
one-off input on a page. Two modules needing the same escape hatch = the contract grows.
