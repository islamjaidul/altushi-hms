# 0015 — Shared input layer + Wave-0 safety rails

- **Status:** Done
- **Date:** 2026-07-27
- **PRD ref:** §7 U3/U4/U5/U9/U13; §14 volumes; §12 (revocation)
- **MVP:** post-MVP — Wave 0 of `11-build-plan-phase2.md`, per ADR-0020/ADR-0022 and the
  ADR-0019 amendment

## Problem

Four input-control defects confirmed in code (`10-mvp-review.md` §1) share one cause: the
interaction grammar of `05-ui-architecture.md` §3 was never built, so screens hand-rolled
inputs. Operators cannot find older unpaid invoices (dues/refund filter a pre-truncated
page), a money report silently ignores its date range unless a dropdown also says Custom,
date entry works differently on registration than everywhere else, and patient pickers are
60-item `<select>` lists that break at §14 volumes. Separately, revoked permissions live
until re-login, the cross-context query guard misses method-chain joins, and no test boots
the current build against a previous release's data — the failure class that already
reached production once.

## Requirements

- [M] One forgiving date contract product-wide: free-text entry accepting the registration
  formats everywhere; native browser date inputs banned by CI.
- [M] One search contract: predicate in the SQL query, never fetch-then-filter; dues and
  refund find any matching record regardless of age.
- [M] Patient type-ahead (2+ chars, name/UHID/phone, ranked, keyboard-selectable) replacing
  the recent-60 `<select>` pickers on OPD billing, diagnostics order, and appointments.
- [M] On any screen with a period selector and date inputs, explicit dates always win —
  a submitted date range that does not affect the result is a defect.
- [M] Permission revocation takes effect within 5 minutes without re-login.
- [M] Upgrade-path gate: current build boots against the previous release's database and
  the golden thread passes (ADR-0022).
- [S] Cross-context guard catches method-chain joins, not only query-syntax joins.
- [S] Go-live switch procedure recorded in `deploy/RUNBOOK.md`.

## Acceptance criteria

1. Searching `/billing/dues` and `/billing/refund` for a record older than the previous
   fetch window (300/200) returns it.
2. `/billing/reports` with From/To set and the period dropdown untouched returns the chosen
   range, with the range echoed in the headings.
3. Typing 2–3 characters of a patient name/UHID/phone on OPD, diagnostics order, or
   appointments offers ranked matches; arrow/Enter selects; the chosen patient drives the
   form exactly as the old `<select>` did (verification scripts unchanged and green).
4. `45`, `8 months`, `12/03/1980`, `1980-03-12` are accepted anywhere a date is asked for;
   no `<input type="date">` remains in `src/` (CI-enforced).
5. Revoking a permission is enforced on the user's next request after ≤5 minutes.
6. `eng/verify/upgrade/` restores the previous-release dump, boots the current build over
   it, and the golden thread passes — locally and as a CI job.
7. Full harness green: .NET tests, golden-thread, discount-and-dues, Playwright suite
   (updated where picker markup changed).

## Out of scope

Barcode-wedge and micro-help capabilities of 05 §3 (separate debt items); the 90-day seed
history and memory measurement (owned by spec 0010); catalog/doctor/supplier type-ahead
sources (the endpoint contract extends; patients ship first).

## Risks / open questions

- Replacing pickers touches the golden-thread scripts' pages; mitigated by keeping the
  `PatientId` form field name and GET binding unchanged.
- The upgrade fixture is a demo-data dump refreshed at each release cut — never production
  data (ADR-0022).
