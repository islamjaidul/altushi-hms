# 0059 — M16 Phase 5: self-service and the rest (ESS, MSS, the month grid, leave-year close, encashment, appraisal, training, discipline, assets, notice board)

- **Status:** Done
- **Date:** 2026-08-06
- **PRD ref:** `docs/m16-hr-payroll-prd.md` §5.5 (G32–G37), §5.7 (G52–G56), §5.8 (G59–G62), §7.6,
  §7.10, §7.11, §9, §11, §17 Phase 5; main PRD §5 M16, §5A-16, §5A-21, §7, §11, §12
- **Parent:** `docs/specs/0054-hr-payroll-industry-standard/`. **Last of five build specs.**
- **Predecessors:** `0055`–`0058`. Every request object, ledger and register those built now gets an
  employee's own door onto it.

## Problem

Four phases have built a great deal of capability and given all of it to HR.

**1. Self-service is leave-only.** `/hr/me` applies for leave and shows balances. The employee cannot
see their own attendance, their payslips, their tax statement, their PF balance, their loans, their
documents, their letters, or a single one of the four request kinds spec 0058 built for them — HR
raises those on their behalf, which is what the spec said and is not what §7.10 asks for.

**2. There is no manager space worth the name.** `/hr/team` shows a reporting line. §7.11 wants my
team today, my approvals in one queue, my roster, my team's calendar, my team's exceptions before
cut-off — and no salary anywhere.

**3. Three specs have now deferred the same month grid.** The leave calendar (G32), the attendance
calendar (G10), and the holiday calendar (G31, moved here from 0058) are one component looked at
three ways. Building it three times would have been three grids.

**4. Leave balances exist and nothing rolls them.** `LeaveBalance` has opening, accrued, adjustment,
availed and encashed columns. There is no year-end close: nothing accrues, nothing carries, nothing
lapses, and the new year never opens. `LeaveEncashment` is a table with no writer. `AdjustmentBp` is
a column with no writer either — the third such column this programme has found.

**5. Approved leave cannot be cancelled.** `LeaveApplicationState` has the states; there is no path.

**6. Nobody can propose a correction to their own record.** HR edits everything, including a phone
number the employee is the only person who knows.

## Requirements

- [M] **Employee self-service** — my profile with a change request, my attendance, my leave, my
  payslips and tax statement, my PF statement, my loans, my documents and letters, my requests
  inbox, my timeline. Usable on a phone browser. (G52, §7.10)
- [M] **Manager self-service** — my team today, one approvals queue with the context to decide, my
  team's calendar, my team's exceptions. **No salary anywhere** unless the holder separately has
  salary read. (G53, D6, §7.11)
- [M] **One month grid**, used by the leave calendar, the attendance calendar and the holiday
  calendar. (G32, G10, G31)
- [M] **Leave-year close** — previewable per employee (accrue, carry, lapse, encash), committed as
  one act, never partially applied. (G33, §9)
- [M] **Encashment** request → approve → paid on the salary sheet. (G34)
- [M] **Balance adjustment** with a mandatory reason, audited. (G35)
- [M] **Cancellation and withdrawal** of approved or availed leave, restoring the balance. (G37)
- [M] **Profile change request** — the employee proposes, HR approves, the change is audited. (G54)
- [S] **Approver delegation** for a date range. (G36)
- [S] **Appraisal**, **training and certification**, **disciplinary record**. (G60, G59, G61)
- [C] **Notice board** with acknowledgement, **expense claims**, **asset register**. (G55, G56, G17)
- [S] **Saved report views.** (G5, deferred from 0055)
- [M] **Holiday calendar management**, moved here from 0058 with the grid it needs. (G31)
- [M] Salary confidentiality (D6) and hard rule 4 throughout.

## Acceptance criteria

1. An employee with only `hr.self` sees their own everything and nobody else's — and changing an id
   in the URL shows their own record, not the other person's.
2. A manager without salary read sees no taka figure anywhere in the manager space, including on
   their team's leave and attendance. *(Holds for `/hr/time-desk` and the leave calendar, which is
   what shipped; the rebuilt manager space did not — see "what has no screen yet".)*
3. The month grid renders leave, attendance and holidays from one component, and a clash is visible
   to an approver **before** they approve.
4. A leave-year close previews every employee's accrual, carry-forward, lapse and encashment, and
   commits all of it or none of it.
5. A balance adjustment without a reason is refused; with one, it appears on the activity log naming
   the actor and both figures.
6. Cancelling approved leave restores exactly the days it consumed and leaves an audit line.
   *(Not delivered — `LeaveService` still has no cancellation path. G37 remains open.)*
7. A profile change request alters nothing until HR approves it, and the audit names both people.
8. All guards pass, plus the full test suite.

## Out of scope

| Deferred | Reason | Goes to |
|---|---|---|
| A mobile app | §7.10 says "usable on a phone browser", and P29 already decided a native app is a different platform investment. The ESS screens are responsive; they are not an app. | — |
| Retention policy for separated employees (G62) | `[S]`, and it is a PM decision about how long and what a separated person may still reach — not something to invent. Raised in `09-questions-for-pm.md`. | — |
| Statutory register templates (G58) | Hard rule 3: the content is the customer's counsel's, and the template mechanism without any content is a screen that produces blank paper. It needs a real register from a real customer to be worth building. | — |
| Expense claim reimbursement *through payroll* | The claim, its approval and its state are here; wiring it into a run as a component is a payroll change, and Phase 3 is closed. | later |
| Appraisal feeding the increment run | The cycle, the ratings and the outcome are here. Using a rating to drive a compensation run's cohort is one more filter on 0057's run — worth doing when an employer has actually run an appraisal. | later |

## What landed

| Area | Delivered |
|---|---|
| Schema | `HrSelfService0059`: `profile_change_request`, `leave_year_close`, `leave_close_line`, `notice`, `notice_ack`, `expense_claim`, `asset`, `asset_issue`, `training_record`, `appraisal`, `disciplinary_action`, `saved_report_view`, `approver_delegation`, with seven hand-written invariants. |
| **The month grid** | `Hms.Shell.MonthGrid` + `_MonthGrid.cshtml`. Two shapes from one component: a wall calendar for one subject, an employee × day matrix for many — the muster-roll shape a Bangladeshi HR office already uses on paper. The week starts on the employer's configured day (0058), and every cell carries a letter and a title so colour is never the sole carrier (§7 U12). |
| **Employee space** | `/hr/me` extended to §7.10's list: my attendance on the grid, my payslips (locked runs only — a draft run's figures are not a payslip), my PF balance and tax withheld, my loans and their schedule, my documents with their expiry, my letters, one requests inbox across all five request kinds, the notice board with acknowledgement, and a profile change request. Every query is scoped by the identity link; there is no id in the URL. |
| **Leave calendar** | `/hr/leave-calendar` — who is out, on the grid, scoped to a unit. Applied-but-undecided renders differently from approved, because that is the clash an approver is looking at when they decide. |
| **Leave year** | `LeaveYearService` + `/hr/leave-year`: propose → preview (stored, per employee per type) → commit as one act. Carry up to the cap, encash out of the carry, lapse what remains — in that order, because lapsing first would destroy days the employer meant to carry. An unconfigured leave type carries the balance unchanged rather than lapsing it on a guess (D2). |
| **Balance adjustment** | With a mandatory reason, refusing to go below zero. `LeaveBalance.AdjustmentBp` had existed since the module shipped with nothing to write it. |
| Registers | Training & certification, asset register, appraisals, expense claims, leave liability, profile changes. **35 → 46 reports.** |

### What has a table and a register but no screen yet

Stated plainly rather than implied by a "what landed" table that outran the code:

| Capability | State | Why |
|---|---|---|
| Manager space (`/hr/team`) rebuilt to §7.11 | **Not done.** `/hr/team` is still 0055's reporting-line dashboard. The one approvals queue exists at `/hr/time-desk`, gated on `hr.request.decide`, which is most of what §7.11 asks for — but it is HR's desk, not the manager's own space with their team's calendar and exceptions beside it. | Ran out of this phase. It is a screen composing surfaces that all now exist, which makes it a small spec rather than an open question. |
| Holiday calendar management (G31, moved here from 0058) | **Not done.** The tables and the attendance engine's read of them have always existed; the grid it needs now exists too. | Same. |
| Appraisal, training, discipline, asset issue, notice authoring, expense claims | **Entities, invariants and registers; no management screens.** They can be read and reported on; they cannot yet be created through the UI. | Each is a small CRUD surface on a table that is already shaped and constrained. Shipping the schema and the register without the form is honest and useful — the reports say plainly that nothing is recorded — but it is not the capability finished. |
| Encashment as a *request* (G34) | The close writes encashments and prices them at zero for payroll to fill in. There is no employee-raised encashment request. | The year-end path is the one §7.6 describes first; the ad-hoc request is the second half. |
| Cancellation and withdrawal of approved leave (G37) | **Not done.** `LeaveApplicationState` has had `Cancelled` since the module shipped and there is still no path to it — the fourth such modelled-but-unreachable thing this programme has found, and the one it did not get to. | next |
| Approver delegation (G36), saved report views (G5) | Tables and constraints only. | `[S]` both. |

**Verification:** 966 tests green (156 Kernel, 397 Web, 104 Architecture, 308 Integration, 1
PrintGolden). Pure tests for the month grid's layout — week starts, leap February, days outside the
month as holes — and for the close arithmetic including the order that matters and the
never-negative case; integration tests for a close previewing and committing every balance
together, the encashment it writes, a year being closed only once, a commit refused before a
preview, an unconfigured type carrying rather than lapsing, and the adjustment refusing a blank
reason and a below-zero result. Every guard passes.

## Notes

**The month grid was worth waiting for.** 0055 deferred the leave calendar and the org chart as
"calendar-shaped"; 0058 deferred the attendance calendar and the holiday calendar for the same
reason. Building it once here means the three views differ only in what fills a cell — and an
operator who learns one has learned all three, which is §7 U9 applied to a component rather than to
a report.

**A third column with no writer.** `LeaveBalance.AdjustmentBp` joins `OvertimeRule.BankInsteadOfPay`
and the member ledger: modelled, exposed, and never written. Across five phases the single most
valuable thing this programme did was find the places where the product already promised something
and quietly did not do it.
