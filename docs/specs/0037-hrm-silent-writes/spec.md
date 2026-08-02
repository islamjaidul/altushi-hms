# 0037 — Six HR screens report success and save nothing

- **Status:** Draft
- **Date:** 2026-08-02
- **PRD ref:** §5 M16, §5 M21, §7 (UX principles), §11 (payroll state chain), §12
- **ADR ref:** ADR-0025 (two-host product line), G19 (one business action, one transaction)
- **Parent:** `docs/specs/0036-hrm-operable/` — QA of what 0036 shipped
- **MVP:** in scope (defect repair, no new scope)

## Problem

Spec 0036 made the HRM operable and its notes named the blind spot that had let four defects
through: **every one returned HTTP 200 with correct markup.** A QA sweep of the shipped product,
driving every screen, every button and every search input and then reading the database back,
found the same blind spot had hidden something larger.

### The transaction commits without saving

`HrTx.RunAsync` opens a connection, begins a transaction, hands three `DbContext`s to the caller
and calls `CommitAsync`. It never calls `SaveChangesAsync`. A commit flushes nothing — EF stages
changes in the change tracker and writes them only when told. `EmployeeService` even carries the
comment *"stages rows; the caller's `IHrTx` commit makes them durable — the house convention"*.
That sentence is false, and it is the mental model the whole module was written against.

Any write that mutates a tracked entity and does not itself call `SaveChangesAsync` therefore
returns 302, shows its success toast, and changes nothing. Six are confirmed by reading the rows
back after driving the screen:

| Screen | Control | Toast shown | Database after |
|---|---|---|---|
| `/hr/attendance` | Correct | "Attendance corrected — the reason is on the record" | status unchanged, `attendance_correction` **0 rows**, no audit |
| `/hr/leave` | Recommend | "Recommended to HR" | state still `applied` |
| `/hr/leave` | Approve | "Leave approved — the balance is spent" | state still `recommended`, balance unspent |
| `/hr/payroll` | Mark reviewed | "Exceptions reviewed — ready to send for approval" | state still `generated` |
| `/hr/policies` | Save payroll policy | "Payroll policy saved" | no row written |
| `/hr/roster` | Assign a shift | (redirect) | `roster_entry` **0 rows** |

The payroll one is the worst of them. `Review` is the first step of §11's chain, so a run
generated inside the product can never be reviewed, never approved, never locked, never posted.
**Payroll cannot be run.** The demo's locked run exists only because `HrDemoSeed` calls
`SaveChangesAsync` itself — the seed compensated for the defect and so concealed it.

The same list by inspection also covers `EmployeeService.AssignAsync` and `SeparateAsync`,
`LeaveService.AvailAsync` and `CancelAsync`, and `PayrollService.RequestApprovalAsync`,
`ApproveAsync`, `LockAsync` and `PostAsync`.

**Both SKUs are affected.** The HR screens are shared, and the ERP host reaches them through
`HrTxAdapter` over `HmsTx`, which has the identical shape.

### Two screens answer ordinary input with a 500

- **`/hr/employees/new` with the name left blank** → `NullReferenceException`. Every other field
  on that form is validated; the one field the screen cannot do without is not.
- **`/admin/users`, creating a user against a role that does not exist** → 500, **and the account
  is created anyway.** `UserManager.CreateAsync` runs outside any transaction and commits; the
  `AddToRoleAsync` that follows throws. What is left is a roleless account that can sign in and
  sees an empty product — which is precisely the symptom spec 0036 was written to end.

### The roster loses the operator's place and grows rows on every click

`OnPostAssignAsync` never loads, so `WeekStart` is `default`. Three consequences:

- Each assignment inserts a `hr.roster` header spanning `-infinity … 0001-01-07`. No real date
  falls inside it, so the "reuse the existing roster" lookup can never match one and a fresh
  orphan is written **per click** — ten clicks, ten headers, measured.
- The redirect is `?WeekOf=01 Jan 0001&OrgUnitId=`, so the operator is thrown off the week and the
  unit filter they were working on.
- The board silently renders 60 of 100 employees (`.Take(60)`), with nothing on screen saying so.
  Forty people cannot be rostered and no one is told.

### Smaller things the sweep found

- `/notifications/tray` is in the chrome of **every** HRM screen and returns 404 on that host.
- `/hr/roster` is 587 KB — 420 inline forms, each carrying its own antiforgery token — served per
  view to a §16 box of 2 vCPU / 3 GB.
- Saving the payroll policy twice in one day would raise an uncaught `HrException` (a 500), masked
  today only because the first save does nothing.
- Masters' rename and retire resolve by id alone, without a branch scope.

## Requirements

- **R1 — a commit is durable.** Completing a business action persists everything it staged, on
  both hosts. No screen may report success for a write that did not happen.
- **R2 — the six confirmed writes persist**, proven by reading the record back through the product,
  not by a status code.
- **R3 — a payroll run generated in the product can be walked to Locked and Posted.**
- **R4 — no screen answers ordinary input with a 500.** A blank employee name and an unknown role
  are refused with a message.
- **R5 — creating a user is all-or-nothing.** No account is ever left without a role.
- **R6 — the roster keeps the operator's place**, writes one roster header per week rather than one
  per click, and either shows every employee or says how many it is showing.
- **R7 — no dead link in the chrome** of either host.
- **R8 — a guard that fails when a write is staged and never saved**, so the next one is caught by
  CI rather than by a customer.

## Acceptance criteria

1. **AC1** For each of the six controls: drive it over HTTP as an operator, then re-read the
   rendered screen and the row — the change is there.
2. **AC2** A run generated through `/hr/payroll` reaches `Posted` through the screen's own buttons.
3. **AC3** A blank employee name and an unknown role each return a rendered page with a message,
   and create nothing.
4. **AC4** After ten roster assignments, `hr.roster` has one header for the week, and the operator
   is still on the week and unit they started on.
5. **AC5** `/hr/roster` states its own row count against the headcount.
6. **AC6** Every link in the chrome of both hosts resolves.
7. **AC7** The new guard fails on the pre-fix tree and passes after.
8. **AC8** `eng/verify/hrm-thread.py` — the sweep that found all of this — passes end to end
   against a fresh database, and against the deployment.

## Out of scope

- Payslip PDF, punch-import screen, employee document upload, employee edit (0036's follow-ups).
- Employee↔user linking, so `/hr/me` stays an empty state for every login. It is honest about why.
- HR payroll *arithmetic* tests (proration, rounding residue, night-shift pairing). Still the
  largest outstanding risk, and still not this spec.
- The roster's page weight is recorded, not redesigned — a paging or per-row-save redesign is a
  separate change.

## Risks / open questions

- Making the commit flush changes behaviour for **all fourteen ERP modules**, not only HR. The
  recommended default is to do it anyway: it makes the invariant G19 already claims actually true,
  and the ERP's regression suite is the evidence. Every ERP module currently saves explicitly, so
  the flush should be a no-op there — which the suite must confirm before the ERP is redeployed.
