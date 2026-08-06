# 0052 — Tasks

## WP3 — State machine takes a lock
- [x] Failing test: a second lock/post/reverse raises `HrException`, not `DbUpdateException`
      (`Two_operators_locking_one_run_serialize_and_the_loser_is_refused`)
- [x] `PayrollService.LoadAsync` → `FOR UPDATE` via `SqlQuery`, the `FolioService.LockAsync` idiom
- [x] Partial unique index on `payroll_run.reversal_of_run_id WHERE NOT NULL`
- [x] Watched to fail: with the `FOR UPDATE` removed, the test reports
      "the second operator did not wait for the row lock"

## WP1 — Unpaid leave deducts
- [x] Failing test: unpaid-leave employee paid less than identical paid-leave peer
- [x] Control assertion in the same test: paid leave deducts nothing
- [x] Batched paid/unpaid resolution above the employee loop (day → application → `LeaveType.Paid`)
- [x] Populate `PayrollLine.LeaveWithoutPayDaysBp`
- [x] `ComputedComponent.LeaveWithoutPay` branch using `DeductionRule.PerLeaveWithoutPayDayBp`
- [x] Missing-rule case deducts nothing and says so (ADR-0027)

## WP2 — Shortfall becomes a durable debt
- [x] Failing test: a floored line's shortfall is readable from `employee_ledger_entry` after lock
- [x] `LedgerKind.Advance`
- [x] Ledger write at lock; mirroring entry on reverse

## Found while doing the work — see notes.md
- [x] `ReverseAsync` never copied `CarriedShortfallTaka`, so `ck_payroll_line_net` refused every
      reversal of a floored run. Fixed with the rest of WP2; watched to fail with 23514.

## WP4 — Migration + verification
- [x] One additive migration (`20260806062101_HrPayrollGuards0052`, index only)
- [x] Reproduction test: a locked unpaid-leave deduction survives the type being made paid
- [x] `dotnet test` — all five projects, 559 passed / 0 failed
- [x] `eng/check-additive-migrations.sh` against the generated script — OK
- [x] `eng/check-no-hard-deletes.sh`, `check-lifecycle-traceability.sh`, `check-fkeys.sh` — OK
- [x] `qa-lifecycle` t0 + t1 against the ERP host — both GREEN, 12/12 roles
- [x] `hrm-thread.py` against the HRM SKU — 36/38; the 2 reds proven pre-existing at `HEAD`
- [x] Out-of-spec: `Policies.cshtml` clean-build repair (see notes.md) — `dotnet build hms-erp.slnx` 0 errors
