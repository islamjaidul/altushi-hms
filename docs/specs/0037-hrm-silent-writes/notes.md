# 0037 — Notes

## How the sweep was run, and why that mattered

Every screen of the HRM was crawled, then every control on it was driven the way an operator drives
it — the form's own fields, posted with the page's own antiforgery token — and then **the row was
read back**. That last step is the whole finding. Six controls returned 302, showed their success
toast, and had changed nothing. A route smoke test, a permission probe and an entitlement check all
pass over them, which is the same blind spot spec 0035's and 0036's notes each recorded. This is the
third spec in a row to hit it, and the first to hit it with a script that reads the database.

The first version of that script was itself wrong in a way worth recording: it looked for a `500`
marker in `body[:9]`, but the body of a 500 is the exception page, not a marker. Four "not a 500"
checks were passing over real 500s. **A green check is only as good as the thing it looks at** —
which is the same sentence as the defect it was written to find.

## The root cause, and why the fix is at the seam

`HrTx.RunAsync` opened a connection, began a transaction, handed out three `DbContext`s, and
committed. It never flushed. EF stages changes in the change tracker and writes them when told;
committing an unflushed tracker writes nothing. Every handler in the module was written against the
belief that commit persists — `EmployeeService` says so in a comment, in those words.

Fixing eleven call sites would have left the trap armed for the twelfth. The flush went at the seam,
so "one business action, one transaction" (G19) is a property of the boundary rather than something
each author has to remember. `HmsTx` got the same treatment, because the HR screens are shared and
the ERP host reaches them through `HrTxAdapter` over the identically-shaped `HmsTx`.

**The ERP was the risk that had to be discharged, not assumed.** Its full lifecycle suite — 14
scripts, all tiers, 12/12 roles — was run against a fresh local database with the flush in place,
and was green. Every ERP service already saves for itself, so the flush is a no-op there, and now
there is evidence rather than an argument.

## Two red things on main that were nobody's bug report

- `BanglaSpikeTests` pointed at `src/Hms.Web/wwwroot/fonts`. Spec 0035 moved the assets to
  `Hms.Shell` and the test has been red ever since — through three specs.
- `role-journeys.py` carried a hand-written copy of the seed's grants that had never been updated
  for the HR claims spec 0034 gave the MD and the Pharmacist. So the ERP suite reported a permission
  **leak** on `/hr`, `/hr/employees` and `/hr/me` that the seed and the `[Authorize]` attributes
  agree does not exist. A suite that is red for a false reason is worse than no suite: it teaches
  people to skip it.

Neither was caused by this work. Both were found by running things that had stopped being run.

## Decisions

**The roster keeps its 60-row cap and says so.** Sixty rows of seven `<select>`s, each in its own
form with its own antiforgery token, is a 676 KB page — the heaviest either SKU serves. Removing the
cap makes that worse, not better. The cap stays, the board now states "showing 60 of 101", and
`hrm-thread.py` holds an 800 KB ceiling so it cannot creep. A single-form board or a paged one is a
redesign, and it is recorded below rather than smuggled into a defect fix.

**The payroll policy amends today's row rather than raising.** An operator correcting a typo and
pressing Save again is the ordinary case, not an error; effective-dating a second policy from the
same day is what the database refuses. Yesterday's policy is still closed off and preserved — hard
rule 5 holds, because a historical salary sheet still resolves the convention that was in force.

**Masters still cannot be deleted, and now cannot 500 either.** `SingleAsync` per tab threw whenever
the id was not on the open tab, which two browser windows is enough to produce. One branch-scoped
lookup through an `IMasterRow` interface replaces six, and a stale tab gets a sentence.

## Verification performed

- `eng/verify/hrm-thread.py` — 37 cases, 0 failed, on a fresh local database; then **green twice in
  a row against the same dirty database**, which is spec 0029's rule and which the first cut broke.
- `dotnet test hms-erp.slnx` — 343 passed, 0 failed. `TransactionDurabilityTests` was seen to fail
  2 of 3 on the pre-fix tree, with the row simply absent after the commit returned.
- ERP `lifecycle-suite.py --tier all` — 14 scripts, 0 failed, 12/12 roles, fresh database.
- Guards: css-classes, ui-tokens, icon-glyphs (60/60 ligatures), no-native-date, fkeys,
  no-external-hosts, lifecycle-traceability.

---

## Deployment record — 2026-08-02

Built and deployed to `hrm.specshipper.com` from `5eabd79` via RUNBOOK §10 (`compose.hrm.yml` +
`compose.hrm.vm.yml`, `up -d --no-deps app`). **The database was not dropped this time** — the
change adds no migration, so the existing demo seed was kept and the fixes were proven against data
that predates them.

VM after: 1370 MB available (1345 before), `hrm-app-1` at 164 MiB against its 500 MiB limit, one
Postgres. The ERP was not rebuilt and still runs its old image; `main` carries the `HmsTx` flush,
the moved font path and the corrected grants table for its next rebuild.

### Re-QA against the deployment — 37 cases, 0 failed

The six writes that did nothing, verified live and then read back out of `hrm` on `hms-db-1`:

| | Before | Live now |
|---|---|---|
| Attendance correction | status unchanged, 0 correction rows | 1 correction row, visible on the register |
| Leave recommend | state stayed `applied` | moves out of Applied |
| Leave approve | state stayed `recommended` | Approved, on the queue |
| Payroll review | state stayed `generated` | **`PR-2026-27-0002` generated → reviewed → approval requested → approved → locked → posted**, journal balancing at ৳ 43,06,000 |
| Payroll policy | no row written | effective-dated: the old policy closed at 2026-08-01, a new one from today at ৳ 500, and the screen shows it |
| Roster assign | 0 entries, one orphan header per click | 3 entries, **2 headers for 2 weeks**, and the operator lands back on 23 Aug 2026 |

Also live: a blank employee name and an unknown role are refused with a sentence instead of a 500;
a user created against a bad role leaves no account behind; the roster says "showing 60 of 101"; and
no link in the chrome 404s.

The run's manifest is `eng/verify/runs/hrm.specshipper.com-20260802T093153Z/hrm-thread.json` — one
employee, seven masters, a `qat093153z` login, a `QA Probe Role` and `PR-2026-27-0002`. Nothing is
deleted; hard rule 4 applies to a QA run too.

## Not done

- **The roster's 676 KB page.** One form for the whole board, or paging, is the fix. Recorded, not
  attempted.
- **HR payroll arithmetic tests** — split-period proration, rounding-residue allocation, post-lock
  arrears, night-shift punch pairing, punch-import idempotency, negative-net floor. Still the
  largest outstanding risk on this module, and now the *only* one of that size: the state machine
  and the durability of every write are covered, the arithmetic inside them is not.
- Employee↔user linking, so `/hr/me` is an empty state for every login on both SKUs. It says why.
- Payslip PDF, punch-import screen, employee document upload, employee edit.
- **The `hrm` database still has no backup** (RUNBOOK §10). Unchanged since 0035, and now holding a
  posted payroll run.
- `hr.leave.apply` is seeded to the Pharmacist alone, though `DevSeed`'s own comment says §12 gives
  it to every clinical role. Not touched here — it is a scope question for the PM, not a defect.
