# 0042 — Notes

Deviations from `plan.md` and what the build actually established.

## 1. The red run was real

The tests-first discipline paid out exactly once, which is once more than zero: with WardGuard
and `PostConsultantVisitAsync` implemented but `AdministerAsync` untouched,
`A_dose_cannot_be_recorded_from_another_branch` failed with *"No exception was thrown"* — the
F9 cross-tenant write demonstrated live against Postgres — and passed after the one-line
`AND branch_id = {branchId}`. The nine seam tests passed on first implementation.

## 2. Testing the composition root needed a project reference, and that was the finding

`IpdBilling`'s orchestrators were untested not because nobody wrote the tests but because
`tests/Hms.Integration.Tests` did not reference `src/Hms.Web`, so `TxScope` and the orchestrators
were unreachable from the only test project with a database. One `ProjectReference` (the
`Hms.Hr.Web` precedent from spec 0037) and a `TxScope` constructed exactly the way `HmsTx` does
it closed a structural coverage hole that had silently exempted every money seam at the
composition root.

Related trap for future seam tests: `MigrateAsync` must run on its own connection — running it
inside the test's explicit transaction aborts the transaction and poisons every later statement
(`25P02`).

## 3. Deviations

- **The folio double-submit guard on the diagnostics counter is a repeat window, not a token.**
  The invoice submission token cannot cover an invoice-less path, so the folio branch refuses a
  same-operator/same-folio order within one minute as a repeat click. Recorded in the gap
  register; a folio-order submission token is the proper fix if it ever bites.
- **LC-NUR-14's screen assertion counts presence and no-growth, not exactly-one.** The folio
  page legitimately renders each charge line twice (screen table + printable statement), which
  the first version of the case misread as a double charge. The database was verified correct
  (one `ipd.consultant_visit`, one `bill.charge_line`); exactly-one is pinned by
  `WardMoneySeamTests`, and the thread asserts the screen shows it and that a second sign grows
  nothing.
- **`Indoor` gained an `IpdException` catch** alongside `EmrException`: the visit-posting path
  crosses into ipd/billing, and its refusals must land as sentences on the same screen.
- **The stale-instance trap fired again** (third time this project): an `Hms.Web` from a prior
  session held :5199 during the first live run. `lsof` before believing anything.

## 4. Verification performed

- `dotnet test hms-erp.slnx`: **498 passed, 0 failed** (488 → 498: 9 `WardMoneySeamTests` + 1
  branch-scope test in `NursingStationTests`).
- Guards: additive-migrations (ipd + reg scripts), ui-tokens, css-classes, no-native-date,
  no-hard-deletes, fkeys, no-external-hosts, lifecycle-traceability — all green
  (187 cases, 175 covered, 12 gaps).
- `nursing-thread.py`: **13 cases, 0 failed**, then re-run green inside the suite.
- `role-journeys.py`: 15 cases, 12/12 roles. `lifecycle-suite.py --tier t1`: **11 scripts
  green**, ward census unchanged at 13 free beds.
- Idempotency of the visit charge verified at the database:
  `ipd.consultant_visit` and `bill.charge_line` carried exactly one row per admission after
  two signs by the same doctor on the same day.

## 5. Still open, deliberately (all in the QA gap register)

Ward/bed for the pharmacy porter and LIS phlebotomist; `ot.ot_case.admission_id`; nurse-side
service charging from care tasks; the dead `TestOrderPaid` outbox writes and the absence of any
push notification to pharmacy/pathology; receive-note prompting from the admit flow;
`DutyAssignment.EmployeeId` service-level validation; `MarDose.ProductId` for dose-vs-issue
reconciliation; a doctor-facing test-ordering UI on `/emr/indoor`.

**Not deployed** — rides the next ERP image rebuild with 0038–0041.
