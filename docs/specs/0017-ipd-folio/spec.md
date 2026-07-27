# 0017 — M6 IPD & Patient Folio (+ R4 bill-block)

- **Status:** Done
- **Date:** 2026-07-27
- **PRD ref:** §5 M6, §5A-8, §5A-9, §5A.2 R4, §11 (Admission / Patient Folio / Bed), §12, §6.3 Journey 2
- **MVP:** post-MVP — Wave 2 of `11-build-plan-phase2.md` (the heaviest integration of the phase)

## Problem

The folio seam the MVP carried (`charge_line.folio_id`, XOR-checked, never exercised) has no
consumer: the hospital cannot admit a patient, allocate a bed, accumulate indoor charges, take
an advance, or produce a discharge bill. Every indoor taka — bed rent, ward services, indoor
medicine, indoor investigations — is invisible to the money spine. M7 (OT), M5 (nursing
charts) and M14 (canteen) are all gated on this module existing.

## Requirements

The PRD-to-screen traceability matrix is in `plan.md` (DoD rule 1: matrix first). Summary:

- [M] Admission (OPD referral / emergency / direct) with admitting consultant & provisional dx
- [M] Bed/cabin allocation, transfer with time-stamped history, reservation, cancellation;
  bed states per §11 (Free → Reserved → Occupied → Cleaning → Free · Out-of-Service)
- [M] **Bed charge auto-calculation** by day/class from the effective-dated rate plan,
  with a deterministic transfer-day rule (PM confirmation sought as **P18**)
- [M] **Patient folio** — every chargeable event posts here; folio balance =
  charges − advances − payments; folio states per §11 incl. post-lock posting⚿
- [M] Advance/deposit receipts against the folio, reconciling through counter day-close
- [M] Consultation/visit entry (doctor attribution feeds M17 accruals via `charge_line.doctor_id`)
- [M] Oxygen & unit-based service consumption entry (US6.1: nurse never touches prices)
- [M] Discharge: clinical summary, settlement assembled from the folio (US6.2), gate pass
- [M] Discharge / Death / Birth certificates with sequential numbers & reprint audit
- [M] Reports: today's admissions/discharges, occupancy by ward/class, department census
- [M] 5A-9: Admission Package & Admission Fee masters (effective-dated prices),
  **service-charge %** applied at settlement, **Medicine Indent** (issues M11 stock FEFO at
  MRP to folio — closes spec 0016 deferrals #4 and #11) and **Investigation Indent**
  (indoor test order posting to folio, riding the existing LIS flow)
- [S] 5A-8: extra bed (attendant) & visitor card fee — via catalog services posted to folio
- [S] **R4**: Blocked(due-hold)⚿ ⇄ Released⚿ on admission+folio; block list; blocked patients
  barred from further chargeable service and from discharge until released

## Acceptance criteria

1. **The seam proof first:** the module's first test posts a folio-parented charge line
   through the billing spine and shows `ck_charge_parent` holds (encounter XOR folio).
2. A bed can hold one patient: two concurrent admissions to the same bed serialize under the
   bed row lock; the loser gets a comprehensible error (ADR-0015 pattern).
3. Bed days post idempotently (one row per admission+date, DB-unique); a transfer moves
   charging to the new bed from the first unposted date; re-running catch-up never
   double-charges.
4. US6.2: the settlement invoice assembles itself from folio lines with zero re-entry;
   advances reduce the due; excess advance is returned as a negative receipt through the
   drawer; `net = gross − discount + tax + rounding_adj` still CHECK-enforced; the folio locks.
5. Post-lock posting requires a Billing Supervisor approval (§12 cross-role table); an
   unapproved attempt is server-refused.
6. §11 Admission machine complete: Reserved → Admitted → Transferred* → Blocked⚿ ⇄ Released⚿
   → Discharge Initiated → Clinically Cleared → Financially Settled → Discharged, plus Death
   (→ death certificate) and Absconded — every state reachable and leavable in the UI.
7. R4: a blocked folio refuses service postings, indents and discharge server-side; release
   is approval-gated; the block list shows blocked admissions with their balances.
8. Medicine indent issues FEFO from M11 stock (expired batches invisible), posts folio lines
   at batch MRP; a discharge-time return restocks the exact batches and posts a negative
   folio line — 0016's two M6-blocked deferrals close.
9. An investigation indent creates a test order that flows the normal LIS path (no Invoiced
   state — §11 indoor branch) with its charges on the folio.
10. Certificates issue with sequential numbers from the kernel series; reprint increments a
    counter and appends an audit row.
11. Advances taken at a counter appear in that session's tender totals and day-close
    reconciles (expected cash includes advances); the new `advances_taken` figure is on the
    day-close summary.
12. §12: Nurse posts services/requisitions but cannot settle or admit; Front Desk manages
    beds/admissions but cannot settle; Billing Operator settles; nav composes from grants.
13. Tests at three levels (service integration, Playwright, `ipd-thread.py` end-to-end) and
    the upgrade gate passes — the bill-schema migration (invoice/receipt folio parents) must
    prove itself over previous-release data.

## Out of scope (explicit deferrals — reasons in the matrix)

- **ICU/CCU/HDU/NICU acuity daily bundles** [S] — bed classes and per-class tariffs ship;
  bundle composition needs clinical protocol input we don't have. Bed-class rate covers the
  demo need.
- **Nurse/ward-boy duty assignment views** [S] — needs the M16 staff registry (Wave 5).
- **Pre-admission cost quotation** [C] — deferred; quotation is presentation over the same
  rate plan, no structural risk.
- **Interim bills during stay** — the PRD's [M] is final-bill settlement; interim billing is
  additive later (folio supports multiple invoices structurally but only settlement is built).
- **Outdoor-counter enforcement of R4 blocks beyond OPD** (diagnostics/pharmacy counters) —
  the OPD billing screen gets the guard; blanket outdoor enforcement lands with M21's
  request-center wave.

## Risks / open questions

- **Bed-day proration** — the PRD demands "correct per-day charging from transfer time" but
  defines no rule. Implemented default (deterministic, never reverses posted money): one bed
  day per calendar date from admission date through the last admitted date; a date's charge
  goes to the bed occupied when that date is posted; transfers take effect from the first
  unposted date. Raised as **P18** for PM confirmation.
- **Bill-schema migration risk** — making `invoice.encounter_id`/`receipt.invoice_id`
  nullable + XOR parents is the phase's first mutation of MVP money tables. Mitigated by the
  upgrade gate (ADR-0022) and XOR CHECKs mirroring the proven `ck_charge_parent` pattern.
- Death/Absconded paths carry dues; due follow-up stays the existing dues screen (no new
  collections flow).
