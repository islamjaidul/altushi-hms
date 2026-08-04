# 0048 — An IPD bill counter that handles only IPD

- **Status:** Done
- **Date:** 2026-08-04
- **PRD ref:** §5 M4 (counters/sessions), §5 M6 (IPD billing), §12 (cash controls)
- **MVP:** in scope — post-freeze Phase 2; separates money streams in shipped modules
- **Requested by:** the owner, ahead of tonight's presentation

## Problem

IPD money rides whatever counter the operator happens to have open. Settlement at discharge
accepts any open session — a front-desk OPD drawer absorbs inpatient settlements, so the IPD
cash stream has no drawer, no float, no variance and no day-close of its own. There is no
screen where an IPD cashier works: advances live on the folio page, final bills on the
discharge page, and the dues queue mixes OPD and IPD invoices. The hospital cannot staff a
dedicated indoor billing desk, which is how a 50–300-bed hospital actually runs.

## Requirements

- [M] A dedicated **IPD Billing Counter** (its own sessions, float, variance, day-close —
  the per-session machinery already provides this once the counter exists).
- [M] Discharge settlement requires an open **IPD** counter session; the prompt names the
  action ("Open the IPD billing counter") instead of failing obscurely.
- [M] An operator whose only open session is the IPD counter **cannot create OPD invoices** —
  the IPD counter handles only IPD.
- [M] An **IPD billing workspace** (`/billing/ipd`): session status, the discharge pipeline
  with folio totals / advances held / balance, and links into folio and settlement.
- [S] The same operator may hold an OPD and an IPD session simultaneously (sessions are
  per-counter), so a small hospital's single cashier still works both desks.

## Acceptance criteria

1. Confirming a settlement without an open IPD session is refused with the "Open the IPD
   billing counter" message; with an IPD session open it succeeds and the invoice's session
   is the IPD counter's.
2. With only an IPD session open, `/billing/opd` refuses invoice creation with "This counter
   handles IPD only"; opening a front-desk session restores OPD billing.
3. `/billing/ipd` lists admissions in the discharge pipeline with correct folio gross,
   advances and balance, linking to the folio and discharge screens.
4. The IPD counter day-closes independently, showing only its own receipts/advances.
5. Full t1 verify tier green after the harness gains the ensure-IPD-session step.

## Out of scope

- Splitting the dues/refund queues by OPD/IPD (the IPD counter may collect IPD dues; queue
  filtering is a follow-up).
- OPD-vs-IPD split in the reports screen beyond the existing per-counter grouping.
- A dedicated IPD-cashier role/permission — `ipd.settle` already expresses the right; role
  design is a PM conversation.

## Risks / open questions

- Six verify-script call sites drive settlement through arbitrary counters; they are updated
  in the same change via one shared harness helper (the widest blast radius of the day).
