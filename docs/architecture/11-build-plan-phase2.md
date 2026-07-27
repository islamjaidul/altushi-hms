# 11 — Phase-2 Build Plan: the fourteen released modules

- **Status:** Approved (architect); sequencing per PRD §9 / §9A.3 with deviations argued inline
- **Date:** 2026-07-27
- **Spec:** `docs/specs/0014-phase2-review-and-plan/`
- **Precondition:** `10-mvp-review.md` — the review whose rulings this plan cashes in

Scope: the fourteen modules PRD §9A.3 deferred and the PM has now released — M2, M5, M6, M7,
M10, M11, M12, M13, M14, M15, M16, M17, M18, M19 — plus the §5A.2 v1.0 gaps R2 (health/discount
cards), R3 (public queue display + self-service report status), R4 (bill-block / due-control
hold). One spec per module, each with a PRD-to-screen traceability matrix **before** code, per
the Definition of Done in `docs/architect_review_prompt.md`. All effort figures below are
**estimates**, marked as such.

---

## 1. Sequencing summary

| Wave | Contents | Why this order |
|---|---|---|
| **0** | Shared input layer · upgrade-path test · semantic cross-context guard · security-stamp revalidation · go-live runbook section · seed-history generator + memory measurement | Review items 1, 2, 4, 5, 7, 9. Everything here multiplies across the fourteen modules; nothing here blocks on a module. |
| **1** | **M11 Pharmacy** (+ stock-ledger kernel) | §9A.3's own call: first module after MVP, cash engine. The stock kernel it forces is also M12/M13/M14's foundation. |
| **2** | **M6 IPD & folio** · **M2 Front Desk** · **R4 bill-block** · **R3 queue display** | M6 is the heaviest integration and the gate for M7/M5-nursing/M14-folio-billing; start it the moment the spine is free. M2/R3 are thin and ride along. R4 adds the `Blocked` state to admission+folio (§11) — cheapest while M6's state machines are wet. |
| **3** | **M5 Prescription & EMR** · **M7 OT** · **M10 Radiology** | All three consume M6 (nursing charts, folio posting, admitted-patient worklists). M10 reuses the existing report-template engine (5A-10 `Must` — engine shipped in MVP admin). |
| **4** | **M12 Inventory (3 stores)** · **M15 Accounts & Finance** | M12 generalises the Wave-1 stock kernel (assets, raw-material conversion, reagent-machine). M15 consumes the day-close holding structure the MVP has been posting into — verify that seam **first task** in its spec. |
| **5** | **M16 HR & Payroll** · **M17 Consultant Payments** · **M19 Marketing & Referral** · **M18 Corporate/Panel** · **R2 cards** | The payout cluster. M17/M19 read `charge_line.doctor_id/referrer_id` accruals (already on every line — review §5); M17 needs M15's ledgers and M16's payee records. M18 revisits the payment-in-full lab-release trigger (review §4.2). R2 is rate-plan work adjacent to M18's panel rates. |
| **6** | **M13 Blood Bank** · **M14 Canteen** | Phase-3 items in PRD §9; pulled in last. M13's spine is its §11 blood-unit/request state machines; M14 posts to folio (M6) and cash (M4) — both exist by then. |

Deviation from PRD §9's phase grouping: **M12 and M15 move ahead of M16–M19** (PRD lists all in
Phase 2 without internal order). Reason: M17 cannot pay a consultant without M15's ledgers to
post into, and M12 is a generalisation of the stock kernel while it is still fresh. This is
sequencing within the released scope, not a scope change — no PM decision needed.

## 2. Wave 0 — the shared input layer and the safety rails

**Input layer** (review §1 ruling; contract per `05-ui-architecture.md` §3; ADR-0020):

1. `hms-date` tag helper + JS module: forgiving text entry (`45`, `8 months`, `12/03/1980`,
   `1980-03-12`, `d/m/yy`), renders/echoes `dd MMM yyyy`, 44px, keyboard-first. Replaces every
   native `<input type="date">` (`/admin/masters`, `/billing/reports`).
2. `hms-search` server contract: one page-model pattern — predicate **in SQL**, then take N.
   Re-implement `/billing/dues` and `/billing/refund` on it (the two in-memory offenders);
   `/registration` and `/admin/audit` already comply and adopt the helper unchanged.
3. `hms-typeahead`: trigram-indexed endpoint (patient by name/UHID/phone; extensible to
   catalog/doctor/supplier) + binding for the existing orphaned `typeahead.js`. Replaces the
   60-most-recent `<select>` patient pickers. This is §7 U5 finally honoured.
4. Report period fix: choosing From/To **implies** Custom (dates win; the dropdown follows).
   A date range silently ignored on a money report is a correctness bug, not polish.

**Safety rails:**

5. Upgrade-path test (ADR-0022): CI job boots the previous release's schema + seed from a
   dumped snapshot, runs current migrations, then the golden thread. Fails on any migration
   that only works on a clean database.
6. Replace the regex cross-context guard with a semantic check (Roslyn) that resolves the
   context types used inside a single LINQ execution, catching method-chain joins.
7. Security-stamp revalidation ≤5 min (ADR-0019 amendment) — revocation takes effect
   mid-session.
8. `deploy/RUNBOOK.md` go-live section: disable seed, rotate demo credentials, verify.
9. Seed-history generator (spec 0010's unfinished job) + measured RSS under the Playwright
   suite → replaces the estimated memory table in `06-deployment.md` §2. Every later wave
   records the same measurement at deploy; **abort criterion: sustained RSS > 2.2 GB on the
   VM profile forces a consolidation stop before the next module.**

Estimate: 1 wave-sized unit of work (comparable to spec 0013). Everything after rides on it.

## 3. Per-module notes: structural work, migration risk, validatable vs buildable

"Validate" below means: a real operator can exercise the real workflow (even seeded) and the
result is meaningful. "Build-only" means the real-world precondition (§9A.3's honest reasons)
does not exist at an under-construction customer — the module ships demo-seeded, and first
live use is deferred until the precondition exists. **Demo risk goes to the PM, not hidden.**

| Module | Structural work forced on the spine | Migration risk | Validatable today? |
|---|---|---|---|
| **M11 Pharmacy** | New **stock kernel** (ADR-0021): batch/expiry, GRN, FEFO issue, transfer, damage/adjustment, supplier returns — designed as shared infrastructure for M12/M13/M14, owned here. OTC sale needs a charge-line parent: **counter-sale encounter** (additive; the `ck_charge_parent` XOR is untouched). Multi-outlet + staff-pharmacy price variant via existing effective-dated rate plans. | Low — all additive; POS reuses `BillingService` verbatim. Risk is conceptual: get FEFO + batch valuation right in the kernel, once. | **Yes** (seeded stock). Real stock doesn't exist yet — first live use needs an opening-stock load, which is itself an F1 lock-in feature. |
| **M6 IPD & folio** | First consumer of `charge_line.folio_id`. New `ipd` schema: admission, bed/cabin (states §11), folio, advances, admission package, service-charge %, indents (medicine indents consume M11 stock). Settlement invoicing over folio lines; R4 `Blocked` state. | **Medium — highest of the phase.** The seam is proven additive (review §5) but settlement (package + % + advances + interim bills) is the hardest money math in the product. Spec's first test: post a folio line through `PostChargeAsync` before any screen. | **Build-only** — no beds occupied. Demo-seeded ward board. |
| **M2 Front Desk** | Thin: enquiry desk, visitor/attendant cards (5A-8 ties to M6 beds), directory over existing patient index. | Low. | **Yes.** |
| **M5 EMR** | Prescription entry (doctor-facing — first non-operator persona), Rx print, patient receive note; 5A-7 nursing charts (MAR, diabetic chart) hang off M6 admissions. | Low-medium; new `emr` schema, additive. | Prescription: **yes**. Nursing charts: build-only (need occupied beds). |
| **M7 OT** | Theatre schedule, OT notes, surgeon/anaesthetist teams; charges post to folio. | Low once M6 exists; wrong before it. | **Build-only.** |
| **M10 Radiology** | Modality worklist (manual — no machines), per-modality report templates (5A-10 `Must`) **reusing the MVP template engine**; delivery via existing report-delivery flow. | Low. Analyzer/DICOM stays out (§9A.3; devices not bought). | Workflow: yes seeded; modality integration: **blocked on hardware**. |
| **M12 Inventory** | Generalise stock kernel to 3 stores; fixed assets register, raw-material conversion, approval-authority limits (5A-12), reagent-machine linkage. | Low-medium; kernel already exists from Wave 1. | **Build-only** (no stores running). |
| **M15 Accounts** | Hierarchical chart of heads, the seven ledgers, vouchers, bank module + central cash collection (5A-13 `Must`), top sheet/budget/IOU (5A-14). **First task: verify the MVP day-close holding structure actually feeds the ledger shape** — if not, an additive bridge, never a rewrite of day-close. | Medium — money aggregation correctness; everything additive. | **Yes** (day-close data already flows). |
| **M16 HR & Payroll** | `hr` schema: employees, attendance, comp-off/OT ledger, 3-tier leave approval (reuses MVP approval engine), payroll runs, bonus/increment, HR documents (5A-16/17). | Low-medium; additive. Payroll math is jurisdiction-specific — anything unverifiable about BD statutory rules goes to the PM, not invented. | **Build-only** (no staff on payroll). |
| **M17 Consultant Payments** | Accrual read-model over `charge_line.doctor_id` (already populated); doctor-payment sub-system (5A-18 `Must`); BEFTN file export (ADR-0017) + TDS (ADR-0018, 5A-15 `Must`). **BEFTN file format and TDS rates must be verified against real bank/NBR documents — no fabrication; flagged as an external dependency.** | Medium — money out the door. Payout runs are new; source data is not. | **Build-only** (no accrual history, no bank onboarding). |
| **M18 Corporate/Panel** | Credit invoices, panel rate plans (effective-dated engine reused), corporate statements, collections. **Must revisit the payment-in-full lab-release trigger** (review §4.2) — a credit-approved order must release. Server-rendered statements wire through the QuestPDF path (review debt 6). | Medium — touches the release trigger, a clinical-flow guard. | **Yes** (a panel agreement can be real before opening). |
| **M19 Marketing & Referral** | Referrer accrual over `charge_line.referrer_id`; four-way commission split + RCDD (5A-19 `Must`); MPO setup and payout runs (shares payout rails with M17). | Medium — same class as M17. | Partially — referrers exist pre-opening; payouts build-only. |
| **M13 Blood Bank** | `blood` schema around the §11 blood-unit + request state machines; donor registry, crossmatch, issue. | Low-medium; self-contained. | **Blocked on transfusion licence** — PM question. |
| **M14 Canteen** | Menu/kitchen POS; charges to cash (M4 path) or folio (M6 path). | Low. | **Build-only** (no kitchen). |
| **R2 cards** | Card master + auto-applying discount rate at billing — an effective-dated rate-plan application, not a new engine. | Low. | **Yes.** |
| **R3 queue display** | Read-only public screen over M3 queue + report-status self-lookup. No auth; separate anonymous surface, no PHI beyond serial/name policy (PM confirms display content). | Low. | **Yes.** |

## 4. Decisions and ADRs this plan surfaces

| Decision | Record |
|---|---|
| Shared input layer: build now, contract + components | **ADR-0020** |
| Stock-ledger kernel shared by M11/M12/M13/M14; FEFO issue; batch valuation | **ADR-0021** (written with the M11 spec) |
| Upgrade-path testing: boot-previous-schema in CI | **ADR-0022** |
| Security-stamp revalidation interval | **ADR-0019 amendment** |
| OTC pharmacy sales use counter-sale encounters (XOR untouched) | recorded in ADR-0021 |
| Lab-release trigger revisited under corporate credit | decided in the M18 spec; flagged now |

## 5. Questions for the PM

Appended to `09-questions-for-pm.md`:

1. **Transfusion licence timeline** — M13 is sequenced last; if a licence application is in
   flight, say so and it moves up.
2. **BEFTN + TDS source documents** — M17 needs the partner bank's BEFTN file spec and current
   NBR TDS rates from a verifiable source; we will not fabricate either.
3. **R3 public display content** — what may appear on a public screen (full name vs masked)?
4. **Go-live owner** — who executes the seed-off/credential-rotation runbook, and is there a
   target date that should pull Wave-0 item 8 earlier?

## 6. Cadence and verification per module (restating the bar)

Per module: spec with traceability matrix → build → 3-level tests (service integration,
Playwright, end-to-end thread script) + upgrade-path run → deploy to the VM per
`deploy/RUNBOOK.md` §4 with a DB snapshot first → record measured RSS in the spec's notes →
close the spec. No module is "done" below the Definition of Done in
`docs/architect_review_prompt.md`; anything cut is cut **explicitly** in the spec's matrix.
