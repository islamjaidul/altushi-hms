# 0052 — Notes

## The review that produced this spec

A principal-architect review of M16 on 2026-08-06 covering database design, index coverage, and the
standalone/embedded seam. Eleven findings; this spec takes the three that were wrong about money and
leaves the rest, deliberately, to keep one spec reviewable. The review's own summary of what is
*right* is worth keeping: the GiST exclusion constraints, the policy stamp on every payroll line, the
integer-taka discipline, and the dual-SKU dependency boundary are all sound and were not touched.

## Two things found by doing the work, not by reading

**`ReverseAsync` never copied `CarriedShortfallTaka`.** The reversal negated gross, deductions and
net, and left the shortfall at zero — so `ck_payroll_line_net` (`net = gross − deductions +
shortfall`) refused the row. A run carrying any floored employee could be **locked** (spec 0039
fixed that half as AUD-M16-04) and then **never reversed**: the one correction hard rule 4 permits
was the one the database refused. Nothing in the audit record or the 0038 sweep had found it,
because no test had ever reversed a floored run. Found by writing the WP2 test, watched failing with
`23514`, fixed alongside WP2.

The general lesson is the one 0039's notes already recorded and this repeats: an invariant enforced
in the database is only as good as the number of *paths* that have been driven through it. Lock was
driven. Reverse was not.

**The estimate of WP3's blast radius was wrong, in the safe direction.** The review first described
a double `Post` as leaving a partial write. It does not — `HrTx` flushes once at the commit
boundary, so the second transaction rolls back whole on the payslip unique index. Re-checking each
transition against the index that happens to sit beside it gave the true picture: `Post` and
`Reverse` collide on an index and produce a 500; **`Lock` collides with nothing and both succeed**,
writing two `hr.payroll.lock` audit rows for one lock. So the defect was never corruption — it was
an audit trail that states something untrue, plus two operator-facing 500s. That is still worth the
row lock, and it is why WP3 ranks below WP1 and WP2 rather than above them.

## Decisions

**Unpaid leave is a deduction line, not a reduced earning.** Both were on the table at approval. A
deduction keeps the PF and tax bases computed off the full earning, matches how §5 M16 and US16.1
both describe the behaviour ("deductions traceable to attendance"), and uses
`DeductionRule.PerLeaveWithoutPayDayBp`, which already existed for exactly this and had no reader.

**`LeaveWithoutPayDaysBp` is a subset of `LeaveDaysBp`, not a sibling.** Runs locked before this
spec recorded every leave day — paid or not — in `LeaveDaysBp`. Redefining that column would make
two locked runs disagree about what the same word means, which is the opposite of hard rule 5. The
new column counts the unpaid subset and its own doc comment says so.

**The shortfall lands on `EmployeeLedgerEntry`, not on a new loan lifecycle.** The `[S]` loan module
— request, approve, disburse, schedule installments — is real scope and is not this spec's. What
this spec owed was a *true journal*: the debit to Employee Advances now has a durable, queryable row
behind it. Recovery in a later run remains open and is named in the spec's out-of-scope list.

**The ledger is written at lock, not at generate.** A generated run is a draft that can be
regenerated or cancelled, and a draft must not move an employee's ledger.

## Out-of-spec repair: `Policies.cshtml` did not compile from clean

While verifying, `dotnet build` of `Hms.Hr.Screens` failed with 49 errors from six `RZ1021`s in
`Pages/Hr/Policies.cshtml` — the six `@if (…) { <span …>not configured</span> }` card headings, the
only inline-markup-in-a-same-line-code-block in the repository. Razor parsed the markup as C#
(`'not' could not be found`, `configured<>(span)`).

**This was pre-existing and is not 0052's doing** — proven by stashing every change in this spec and
clean-building at `HEAD`, which produced the identical 49 errors. The working tree had been building
only because `Hms.Hr.Screens/obj` held artifacts from an earlier successful compile; deleting them
during this work is what surfaced it. CI runs `dotnet build hms-erp.slnx -c Release` on a fresh
checkout, so **CI on `main` cannot have been passing this step.**

The six headings were rewritten to the ordinary multi-line `@if` block. No behaviour change, no
markup change — whitespace and line breaks only, which is why it was repaired here under hard rule
0's exemption for formatting rather than deferred: leaving the repository unable to build from clean
was the worse outcome. It deserves a follow-up look at why CI was green, which is **not** answered
by this spec.

## Verification record

| What | Result |
|---|---|
| `dotnet build hms-erp.slnx` from clean | succeeds, 0 warnings (was 49 errors, pre-existing) |
| `dotnet test`, all five projects | 559 passed, 0 failed |
| WP1 unpaid-leave pair + control | passes; asserted against a paid-leave peer in the same run |
| WP1 no-rule case | passes; deducts nothing, note explains why |
| WP2 shortfall ledger + reversal | passes; entries net to nil after reversal |
| WP3 concurrent lock | passes; **watched to fail** with the `FOR UPDATE` removed — "the second operator did not wait for the row lock" |
| Reproduction over unpaid leave | passes; locked figures survive the type being flipped to paid |
| `eng/check-additive-migrations.sh` | OK against the generated script |
| `check-no-hard-deletes` · `check-lifecycle-traceability` · `check-fkeys` | OK |
| ERP lifecycle t0 | GREEN |
| ERP lifecycle t1 | GREEN — 11 scripts, 12/12 roles, ward census unchanged |
| HRM operator thread | 36/38; 2 failures **pre-existing**, see below |

### Two reds that are not this spec's

- **The local `hms` database fails t0.** It holds 135 `adm.permission` rows against a freshly seeded
  90 — Admin was granted 45 extra permissions at some point, so `grant-drift.py` correctly reports
  drift and `role-journeys.py` correctly reports leaks. It is local state, not code. The suites above
  were run against freshly created `hms_qa0052` / `hrm_qa0052` databases; **no existing local data
  was dropped.**
- **`hrm-thread.py` fails two permission-matrix checks** ("0 permissions on the matrix", "a matrix
  cell carries role, permission and the direction"). Proven pre-existing by running the same thread
  against a clean-built `HEAD` on a fresh database: identical two failures. The scrape expects an
  HTML shape `/admin/users` no longer emits. Unrelated to M16; belongs with spec 0049's editor.

## Still open after this spec

From the same review, in the order it recommended, none of them started here:

1. Employee search runs `ILIKE '%…%'` on three columns with no trigram index — `pg_trgm` is already
   installed and unused. Plus six missing composite indexes for filters the screens actually run.
2. Payroll generation is ~3,900 round trips for 300 employees; ~1,800 of them are branch-level policy
   lookups repeated per employee inside the loop.
3. ~30 intra-schema relationships still carry no foreign key (`AUD-M16-10`, partially closed).
4. 80 unbounded `text` columns — HR is the only module in the product with zero `HasMaxLength`.
5. `LeaveService.LeaveYearOf` discards its argument, so `PayrollPolicy.LeaveYearStartMonth` is dead
   configuration; six `LeavePolicy` fields have no reader.
6. No exclusion constraint on overlapping leave applications.
7. No self-service payslip: `/hr/me` shows leave only, and payslips sit behind `hr.salary.read`.
8. Confirmed with the product owner on 2026-08-06 that installs are **single-branch, today and
   planned**. Two findings are latent only under that assumption — globally unique document numbers
   over branch-scoped counters (product-wide, not HR's: `bill`, `ipd` and `reg` share it), and
   `payroll_component_line` carrying no `BranchId` and so escaping branch isolation. Both become
   live the day a second branch is provisioned. The assumption deserves an ADR rather than a line in
   a spec's notes.
