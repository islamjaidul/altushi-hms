# 0052 — Plan

## Approved: 2026-08-06

Test-first per `tdd-loop`; each work package's failing test lands before its production line.

Two decisions were carried at approval, as the recommended defaults recorded in the spec's risks:
unpaid leave becomes a **deduction line** (not a reduced earning), and the carried shortfall lands
on **`EmployeeLedgerEntry`** (not the `[S]` loan lifecycle).

### WP1 — An unpaid leave day deducts pay

*Serves criteria 1–3. Highest value: this is the one paying real money wrongly.*

- `AttendanceDay` already carries `LeaveApplicationId`. Resolve paid-vs-unpaid at payroll time by
  joining day → application → `LeaveType.Paid`, as **one batched query per run**, not per employee —
  it goes above the employee loop next to the other run-level lookups.
- Populate `PayrollLine.LeaveWithoutPayDaysBp` (column exists, always 0 today).
- Add the `ComputedComponent.LeaveWithoutPay` branch in `BuildLineAsync`, mirroring the
  absence-deduction shape exactly: `Taka.ApplyBp(dayRate * days, deductionRule.PerLeaveWithoutPayDayBp)`,
  with `Basis` reading `"N unpaid leave day(s)"`.
- No `DeductionRule`, or no component with that `ComputedKind` → deduct nothing and say so on the
  line note. No invented rate (ADR-0027).
- **No schema change.**

### WP2 — The shortfall becomes a debt that exists

*Serves criterion 4.*

- Write one `EmployeeLedgerEntry` per floored line, at **lock** time (generate produces a draft that
  may be discarded), with a new `LedgerKind.Advance`, the shortfall as a negative employee-side
  movement, `PayrollRunId` set, and a narration naming the run.
- Reversing a run writes the mirroring entry — reversal, never delete (hard rule 4).
- Collection in a later run stays out of scope; the `[S]` loan lifecycle is a separate spec. This
  makes the journal's *Employee Advances* debit true, no more.
- **No schema change** — `LedgerKind` is a string column.

### WP3 — The state machine takes a lock

*Serves criterion 5.*

- `LoadAsync` becomes `FROM hr.payroll_run WHERE id = {runId} FOR UPDATE` via `FromSqlInterpolated` —
  the house pattern at 17 existing sites, and the module's first. One change covers Review, Approve,
  Lock, Post and Reverse, since all five load through it.
- The existing `Require(run, expected, verb)` then refuses the loser with its `HrException` sentence
  instead of a database error.
- Belt and braces for reversal: partial unique index
  `reversal_of_run_id WHERE reversal_of_run_id IS NOT NULL`, so double-reverse is refused by the
  database too, not only by the read.
- Single-lock path throughout — no lock-ordering obligation incurred.

### WP4 — Migration

- One additive migration carrying WP3's partial unique index. That is the whole schema delta; WP1 and
  WP2 use columns and tables that already exist.

### Verification

1. `HrPayrollTests`: unpaid-leave deduction, paid-leave control at the same length, missing-rule
   case, shortfall ledger row read back, and one concurrency test per transition.
2. Existing reproduction test stays green, plus a new one over a period containing unpaid leave
   (criterion 6).
3. `dotnet test` across all five projects.
4. `qa-lifecycle` against the ERP host — HR screens are shared, and spec 0037 is the precedent for
   discharging that risk rather than assuming it.

**Order:** WP3 → WP1 → WP2 → WP4. WP3 first because it is small and makes the concurrency tests for
the others safe to write.
