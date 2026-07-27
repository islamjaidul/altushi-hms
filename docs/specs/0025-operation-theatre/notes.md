# 0025 — Notes

## What the double-booking guarantee actually rests on

US7.1 says double-booking must be *impossible*, not merely warned about. Three things make that
true, and it is worth being precise about which does the work:

1. The theatre row is locked `FOR UPDATE` before the overlap query runs, so two schedulers racing
   for the same slot serialise rather than both reading "free" (ADR-0015 #1).
2. The overlap predicate is the standard half-open interval test (`existing.from < new.to AND
   new.from < existing.to`), which is right at the boundaries — a case ending at 10:30 does not
   clash with one starting at 10:30.
3. Only *holding* states occupy a theatre. A cancelled or postponed case releases its slot, which
   is tested explicitly, because the opposite bug (a cancelled case blocking its theatre forever)
   is the kind that surfaces three weeks after go-live.

The surgeon check rides the same lock. A surgeon in two theatres at once is the same defect as a
theatre with two operations; treating them differently would be arbitrary.

## Completion is the money moment

`OtService.CompleteAsync` moves the state; `OtBilling.PostCompletionChargesAsync` posts the
charges; both run in one transaction from the screen. That ordering is deliberate: the state
guard runs first, so a second completion changes no rows and *throws before anything bills*. The
thread asserts the folio total is unchanged after the refused second attempt, which is the real
question — "was it refused?" matters less than "did it bill twice?".

Team fees are per role, not per person (P23 to the PM). What each named person earned is written
onto `ot.case_team.amount_posted` at completion, so M17 reads payouts from the record.

## Deliberate limits

- **Build-only.** No theatre is running at the customer, so first live use waits. The seed ships
  two theatres and a priced catalogue so the workflow is demonstrable rather than hypothetical —
  that is the honest position `11-build-plan-phase2.md` asked for, not a claim of validation.
- [C] pre-op checklist is cut, marked in the matrix.
- Rescheduling is a *new case*, not an edit of the window. Editing a booked window would need the
  same clash checks plus a reason trail; a new case with the old one postponed says the same
  thing and leaves the register honest.

## Follow-ups

- The consumable picker lists the first 200 products. Same limit, same eventual fix as the
  consultation screen's test list: a type-ahead when a customer's real catalogue arrives.
- Consumables are issued from the **main** outlet, not a theatre sub-store. M12 introduces proper
  multi-store issue; until then the main outlet is where OT stock lives, and that is stated
  rather than configurable.
- The thread had to give each run its own theatres *and* its own date. Theatres alone were not
  enough, because surgeons are shared across runs — a clash assertion is only meaningful when the
  clash is one this run created. Same class of lesson as spec 0022's near-expiry assertions.
