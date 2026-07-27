# 0025 — M7 Operation Theatre (Wave 3)

- **Status:** Done
- **Date:** 2026-07-28
- **PRD ref:** §5 M7, §11 (OT Case), §12, §10
- **MVP:** in scope — Wave 3 of `11-build-plan-phase2.md`

## Problem

Surgery is the largest single line on most indoor bills and the product cannot record one. There
is no theatre, no schedule, no surgical team, and no way for an operation to reach the folio —
so a hospital running two theatres bills surgery from a paper register, by hand, after the fact.
That is precisely where revenue leaks (US7.2: "surgery revenue is never under-billed"), and it is
also where the consultant-payout data comes from (US7.3), which M17 will need and cannot invent.

M6's folio exists now, so the charges have somewhere to land.

## Requirements

- [M] **Theatre master** and an **operation catalogue** with base charges (effective-dated rates,
  hard rule 5 — a historical case must reproduce its historical price).
- [M] **Schedule an operation** for an admitted patient (or an OPD day case) against a theatre and
  a time window, with the **surgical team**: surgeon, assistants, anaesthetist.
- [M] **Double-booking is impossible** (US7.1 AC): a theatre or a surgeon already committed for an
  overlapping window is refused, with a comprehensible message naming the clash.
- [M] The §11 **OT case state machine**: Scheduled → Patient Ready → In-Theatre → Completed;
  Cancelled(reason) and Postponed as terminal/return branches.
- [M] **Operative record** captured at completion: findings, procedure performed, anaesthesia
  type, times.
- [M] **Charge posting on completion** (US7.2 AC): the operation's own charge plus per-role team
  charges post to the patient's folio in one transaction, derived from the catalogue and the
  team setup — never re-typed.
- [M] **Consumables** issued to a case deduct pharmacy stock and post to the folio at the batch's
  price, through the existing FEFO kernel (ADR-0021).
- [M] **Operation register**: the chronological statutory-style log.
- [S] **OT schedule board** — today's theatres and what is in them.
- [M] Team splits recorded per case so M17 can compute payouts without a spreadsheet (US7.3).

## Acceptance criteria

1. Scheduling an operation into a theatre that is already busy for that window is refused, and
   the message names the clashing case; the same for a surgeon already operating.
2. A case walks Scheduled → Patient Ready → In-Theatre → Completed, and each transition is
   attributable; an out-of-order transition is refused, not silently applied.
3. Completing a case posts the operation charge and one charge per team member to the folio, in
   one transaction, at catalogue prices; the folio total rises by exactly that sum.
4. Issuing a consumable to a case reduces stock (FEFO) and adds a folio charge at the batch price.
5. A cancelled case posts nothing and records why; a postponed case returns to Scheduled.
6. The operation register lists completed cases chronologically with surgeon, theatre and times.
7. Completing a case twice is refused (the second attempt changes no rows and says so).

## Out of scope

- **[C] Pre-op checklist capture** — explicitly cut, marked in the matrix.
- Anaesthesia charting and recovery-room observation: not in §5 M7, not invented here.
- Theatre utilisation analytics beyond the register and the day board.
- OT for outpatients without an encounter: a day case still needs a visit at the counter, exactly
  as a prescription does (spec 0024's decision, applied consistently).

## Risks / open questions

- **Team charge rates are per role, not per person.** The catalogue prices "surgeon fee" and
  "anaesthetist fee"; who fills the role is recorded on the case for M17. If a hospital pays
  different surgeons differently for the same operation, that is a rate-plan scope question for
  M17, not a change here. Flagged to the PM as **P23**.
- **Validatable today?** Build-only, honestly: no theatre is running at an under-construction
  customer, so first live use waits. The demo seed ships two theatres and a small operation
  catalogue so the workflow is demonstrable rather than hypothetical.
