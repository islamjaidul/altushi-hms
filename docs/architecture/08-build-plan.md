# 08 — Build Plan

- **Status:** Draft for PM review · **Date:** 2026-07-26 · **Spec:** `docs/specs/0003-mvp-architecture/`
- No demo date exists in the PRD, so the plan is expressed in **2-week sprints (S1–S7, ~14 weeks)** with the golden thread demonstrable early and continuously. When the PM pins a date, sprints map onto it; if the date is shorter than S7, the pre-agreed cut list (§4) applies. Team assumption for estimates: **2–3 engineers + the architect hands-on** (estimate; renegotiate the plan if staffing differs).

## 0. Standing rules

- The golden thread must pass end-to-end on the demo laptop **from S2 onward** — every sprint ends with a demo-reset + thread run (the §9A.4 timed tests join in S4).
- Money-path and state-machine tests (DoD) grow with each sprint; a red money test blocks merge.
- Every §-cited behaviour lands with its edge cases from the brief, not after them.

## S1 — Walking skeleton + the risk spikes

Compose stack (Caddy/app/db/backup) boots on VM + Mac; CI (build, tests, image, no-external-hosts check); kernel scaffolding: authN/session/lock-screen, authZ policies + nav composition, audit writer, number series (+ concurrency test), settings, entitlement file load; UI shell with tokens/templates from the design reference; **Spike A (gates ADR-0009/0014): Bangla PDF shaping proof** — printed sample or the fallback path is invoked now; **Spike B: silent thermal + label print** from browser profile on real hardware.
**Exit:** log in as two roles on the laptop, see role-filtered nav, print a Bangla-footered test PDF.

## S2 — Registration → invoice → payment (the money spine)

Patient registration (dup-warning, age⇄DOB, unknown-emergency, no-phone), patient directory/search, UHID + card print; encounter + charge lines + OPD invoice (POS template) + receipts/tenders + due; counter sessions (open/float); receipt/invoice prints (thermal + A4 + PDF).
**Exit:** golden thread first half runs; ≤ 60 s registration achieved by a non-developer on seeded data.

## S3 — Diagnostics + approval engine

Test catalog + rate versions (effective-dated, exclusion constraint) + resolution; diagnostic order invoice with TAT promise + referrer capture; **approval engine** (policies, thresholds, delegation, inbox, SSE) wired to discount/refund/edit/reset; unbilled-charge SSE seam (order appears at billing); barcode label printing on payment.
**Exit:** discount-above-threshold demo beat works end-to-end; effective-dated price test green (change price, historical invoice byte-identical).

## S4 — LIS + delivery + day-close

Sample entities (M:N, rejection/recollection children, reprint), pipeline board (scan-advance), result entry with ref ranges/flags, verify/e-sign (incl. reporting consultant), amendment versions, delivery log; report-ready notifications + simulation tray; **counter day-close** (expected/counted/variance, carry-close, reopen⚿, summary holding rows); refund-after-day-close path.
**Exit:** full golden thread incl. day-close; §9A.4 timed UI tests run in CI; concurrency harness (parallel serials/dues) green.

## S5 — MD dashboard, admin, import, hardening

MD dashboard read-models + drill-downs; masters screens; **bulk import pipeline** (catalog/prices/beds/doctors/users) with error round-trip; provisional flags + go-live checklist; audit viewer; 2FA enrolment for approver roles; backup/restore scripts + drill; disk/clock sentinels.
**Exit:** F1 construction-kit demo beat works; restore drill ≤ 5 min rehearsed.

## S6 — Demo kit + performance + polish

Seed generator (90-day history, §14 shape) + golden snapshot + `demo-reset.sh` (< 30 s) + dual instances; demo-load test (25–40 operator simulation) → **measure the memory budget table and capacity claims** (replacing estimates — DoD); micro-help pages for the 16 screens; print golden-file suite complete; power-cut recovery drill; accessibility/keyboard pass on §7 checks.
**Exit:** offline checklist passes on a cold laptop; budget table updated with measurements.

## S7 — Buffer + full dress rehearsals

Fix-list from rehearsals; two full 20-minute runbook rehearsals with a non-team keyboard driver (edge 10); documentation runbook set (06 §7); release candidate tagged.

## 2. Dependency-ordered task spine (abbreviated)

kernel(auth→audit→numbering→approvals) → registration → charges/invoice/receipts → sessions → catalog/rates → diagnostics order → SSE seam → labels → LIS chain → delivery/notify → day-close → dashboard → import → demo kit. Printing spikes run first because they gate three later stages; the approval engine precedes diagnostics because the discount beat depends on it.

## 3. Risks (top 6)

| Risk | Exposure | Mitigation |
|---|---|---|
| Bangla PDF shaping fails across candidate libs | Demo edge 9 + report fidelity | S1 spike; pooled-Chromium fallback pre-designed (ADR-0009) with budget swap identified |
| Silent printing per counter proves flaky | Every counter interaction | S1 spike on real printers; PDF preview is an acceptable demo path; print profiles documented |
| .NET RSS exceeds budget under load | The 3 GB promise | S6 measured; GC/trim levers listed in ADR-0001; reversal trigger defined |
| ≤ 60 s / ≤ 2 min operator targets missed | §9A.4 acceptance | Timed UI tests from S4; type-ahead/tab-order tuning time reserved in S6 |
| Seed realism underwhelms (empty-feeling demo) | F-fears unanswered | Seed generator is a first-class S6 deliverable with §14-shaped volumes, reviewed by PM |
| Single-dev-team schedule slip | Demo date | Cut list below, agreed **now** |

## 4. Pre-agreed cut list (PM decides, in this order — recommendation only)

1. Radiology-style extras beyond LIS-lite scope (already out, guard against creep).
2. Appointment module depth → serial issue + today's queue only (keep §9A.2 "lite" honestly lite).
3. Import: keep catalog/price import; defer beds/users importers to post-demo (manual entry passable).
4. Amendment/merge flows demoted from demo script (still built or explicitly deferred with PM sign-off).
5. **Never cut:** money integrity, approvals, day-close, offline posture, seeded history, reset. These are the product.

Anything cut is flagged in `09-questions-for-pm.md` — no unilateral scope changes (ground rule 1).
