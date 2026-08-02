# 0036 — Notes

## What the four reported symptoms actually were

The report was "no create button, buttons overlapping inputs, no roles anywhere." Those are four
independent defects with nothing in common except that **every one of them returns HTTP 200 with
correct markup**, which is why the route, permission, entitlement and layout checks all passed over
them. That is now the third spec in a row to record the same blind spot.

| Reported as | Actually |
|---|---|
| "no create button" | `System Admin` seeded with 2 of 11 HR claims; the button is inside `@if (Can("hr.employee.manage"))` and the guard was working correctly |
| "no roles like the ERP" | `/admin/users` existed only in `src/Hms.Web` — the ERP host. §5 M21 filed under the wrong assembly. |
| "buttons overlapped with input" | `.filter-row`, `.check`, `.inline-form` used by seven pages and defined in no stylesheet |
| icon names as text | the Material Symbols subset was cut in spec 0012 and never regrown |

## Two bugs found while fixing those, that nobody reported

### Employee codes collide at every fiscal-year boundary

`EmployeeService.HireAsync` issued `EMP-{n:D5}` against `FiscalYearOf(JoinedOn)`. The number series is
keyed `(branch, doc_type, fiscal_year)`, so the counter restarts every year — and with no `{fy}` in
the format, the first hire of each new fiscal year is issued a string that already exists.
`ix_employee_employee_code` rejects it.

On a live install this arrives as a **500 on the first hire after a fiscal-year rollover**, on a
screen that had worked all year. It survived every test because fixtures hire people inside one year.
Seeding a hundred with joining dates spread over seven is what broke it — the demo data was the test.

The fix is the scope, not the format: an employee code is a person's permanent identifier, not a
document number within a period, so it is issued against a literal `"all"` scope.
`NumberSeriesScopeTests` now asserts the general rule over all fourteen modules — a fiscal-year-scoped
series must carry `{fy}` in its format — and was proven to fail when the defect shape is reintroduced.

### Four undefined CSS classes in the ERP, not just HR

`check-css-classes.sh` was written for the three HR classes and found seven. `.empty` (13 usages
across five modules), `.btn-lg` (the pharmacy POS's Save Bill — the commit action on a screen whose
whole purpose is that button), `.grid-2col` and `.print-sheet` (the IPD gate pass) have been
undefined since specs 0012–0017. And `key` — the password-reset toast icon on the ERP's own Users
screen — was among the ten missing glyphs, alongside `local_shipping`.

**The ERP has been shipping these for four specs.** It has not been rebuilt this session, so none of
it is live; its next rebuild picks up the fixes.

## Decisions

**The ERP's `Admin` role was left alone.** It holds `admin.*` and `notifications.read` and nothing
else, which is §12's segregation of duties in a hospital where the administrator is not also the
billing supervisor. The HRM is the opposite case — one employer, and the person who installs it is
the HR head — so there the superuser is granted the whole catalogue. Both hosts can now reach
`/admin/users`, so an ERP administrator who needs more can grant it to themselves and the grant is
audited. That is the difference between "cannot" and "must not".

**The superuser grant is reconciled on every start, not only on first run.** The one install with the
problem is one that already has the role, so a first-run-only fix would have repaired nothing —
and "run this SQL by hand" is not a fix a customer can apply.

**No delete on masters.** An `employee_assignment` written in March references its unit forever.
Retiring takes it out of the pickers and leaves history readable — hard rule 4's reasoning applied to
masters rather than to money.

**Still no statutory rates.** The demo seeds a payroll policy and pay components, because those are
the employer's own numbers. It seeds no tax slab, PF rate or leave entitlement. ADR-0027 and P26 hold:
a demo is not a licence to invent Bangladeshi law. `/hr/policies` still reports what is missing.

## The demo seed is driven through the real services

`HireAsync` issues the codes, `SetPayAsync` effective-dates the pay, and the payroll run goes
generate → review → approve → lock through the actual state machine, including the balanced-journal
check. A seeder writing rows directly would have produced a database the product itself could not
have produced — and would have hidden the numbering bug above, which is the whole argument.

Verified on a clean database: 100 employees, 100 assignments, 100 pay structures, 9,000 attendance
days, 300 leave balances, 28 leave applications across four states, and `PR-2026-27-0001` **locked**,
100 employees, 54 exceptions, ৳43,06,000 gross. The exceptions are deliberate: an empty exception list
would make the demo tidier and US16.1's pre-listing invisible.

## Verification performed

- `dotnet build hms-erp.slnx -c Release` — clean, warnings are errors.
- 189 tests pass (Kernel 22, Web 116, Architecture 51).
- Guards: `ui-tokens`, `css-classes`, `icon-glyphs`, `no-external-hosts`, `fkeys`, `no-native-date`,
  `lifecycle-traceability` — all OK. The two new guards were each proven to fail on the pre-fix tree.
- HRM host booted on a clean database; all 15 routes 200 with shell, stylesheet and 10 nav entries.
- Writes exercised over HTTP: created a unit, an evening shift, a role copied from HR Officer, and a
  user — then signed in as that new user and confirmed they see the nine HR entries and **not**
  `/admin/users`, which is the permission filter still doing its job after the superuser change.
- ERP host booted on a clean database; `/admin/users` and `/admin/masters` render with the shell,
  confirming the moved page works in its original host.

## Not done

- Payslip PDF, punch-file upload screen, employee document upload, employee edit.
- `/admin/entitlement` upload screen; employee↔user linking; P28 per-user branch resolution.
- **The `hrm` database still has no backup** (RUNBOOK §10). Unchanged, and still the smallest
  high-value item outstanding.
- HR payroll arithmetic tests — split-period proration, rounding-residue allocation, post-lock
  arrears, night-shift punch pairing, punch-import idempotency, negative-net floor. Money code with
  no arithmetic coverage.

---

## Deployment record — 2026-08-02

Built and deployed to `hrm.specshipper.com` (103.132.96.250) from `1609c03`, via the RUNBOOK §10
path: `compose.hrm.yml` + `compose.hrm.vm.yml`, `--no-deps app` against the ERP's Postgres.

**The `hrm` database was dropped and recreated** so the demo seed would run: its guard is
`employee count == 0`, and the box already held the first seeded set. Only seeded data was lost —
four seeded logins and no customer records — which is the state RUNBOOK §10 already describes
("do not put anything on this deployment you would mind losing"). The one-time SQL in §10 was
re-applied verbatim, which incidentally rehearsed it.

VM memory after: 1345 MB available, against 1187 MB before the deploy. No second Postgres.

### Verified live, signed in as `admin`

| | |
|---|---|
| Sidebar | **10** entries — nine HR plus Administration. Was four. |
| Every route | 200, with `<html>`, version-stamped stylesheet, and the sidebar |
| `/hr/employees` | 100 employees; every name resolves to a rendered record |
| `/hr/attendance` | today's register, 1 exception pre-listed, 100 rows under *show all* |
| `/hr/payroll` | `PR-2026-27-0001`, July 2026, 100 staff, ৳ 43,06,000 net, **Locked** |
| `/hr/masters` | six tabs; created a unit over HTTP and it appeared |
| `/admin/users` | created a role copied from HR Officer, then a user on it |
| Font | the served subset carries all 60 ligatures; the ten that were missing — including `key` and `local_shipping`, both the ERP's — resolve |
| Chrome | `HR & Payroll · 2 module(s) licensed`, `F2 New employee · F3 Search · F10 Payroll` |

The new `shirin` account signs in and sees the nine HR entries and **not** `/admin/users`, because
Payroll Assistant was copied from HR Officer, which does not hold `admin.users.manage`. That is the
permission filter still enforcing after the superuser change — the check worth keeping, since the
whole spec loosened a role.

**The ERP was not rebuilt** and still runs its old image. `main` now carries the moved
`/admin/users`, the four ERP CSS classes and the two ERP glyphs, so its next rebuild picks all of
them up.
