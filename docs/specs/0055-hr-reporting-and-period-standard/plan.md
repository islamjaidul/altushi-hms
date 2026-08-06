# Implement `docs/m16-hr-payroll-prd.md` — M16 HR & Payroll to industry standard

## Context

M16 shipped as the HRM SKU's Wave A (specs 0034–0037, 0039, 0052). It works: employees, attendance
import and correction, rosters, leave, and a five-state payroll run that reconciles against
attendance and refuses to be locked twice. That is *a payroll engine with an employee record
attached* — not a product an HR department chooses.

The module PRD (v1.0, 6 Aug 2026) names 62 gaps against "industry-standard HR & Payroll for the
Bangladesh market" and phases them into five deliveries. Three problems dominate: **it cannot
report** (11 nav entries, zero reports), **nothing has a period** (attendance takes one date, the
dashboard is hardcoded to today), and **the employee record is four tables on one page** rather than
a readable history.

Spec `0054` produced the PRD and explicitly excludes implementation. This plan is the build.

**Scope decision (yours, this session): all five phases as a rolling program, one spec per phase,
committed as each lands.** Phase 1 is detailed below and starts now; phases 2–5 are scoped here and
each gets its own spec + plan when it begins.

---

## Sequencing — five specs

| Spec | Phase | Delivers |
|---|---|---|
| **0055** | 1 — Make it answerable | Period standard · report centre + registers · 3 dashboards · employee timeline · activity log · menu restructure |
| 0056 | 2 — Close the lifecycle | Employment type · probation · dependants/nominees · typed documents + expiry · licence `[hospital]` · separation, clearance, final settlement · letters · notifications |
| 0057 | 3 — Complete the money | Loans & advances · PF/welfare/tax member surfaces · tax statements + TDS · bonus · increment & promotion runs · variance review · salary hold · disbursement + bank file · payslip document |
| 0058 | 4 — Time depth | Device feed · regularization · OT approval, OT bank, comp-off · grace & late policy · roster templates/patterns/coverage · short leave · shift swap |
| 0059 | 5 — Self-service & the rest | Full ESS/MSS · leave calendar, year-end close, encashment · appraisal · training · disciplinary · assets · notice board · expense claims · saved reports |

Phase 1 first because everything in 2–5 is *displayed through* its period control, report grammar
and drill-through contract (PRD handoff note). Building those twice is the expensive mistake.

---

## Decisions taken

| # | Decision | Why |
|---|---|---|
| E1 | **CSV + `.xlsx` server-side; PDF via the existing browser-print path** | The print system is mature (`_Letterhead`, `_SheetFooter`, `.sheet` CSS, `@media print`, 8 documents + the payslip already use it). QuestPDF stays unwired — 60 hand-written PDF layouts on a 2-vCPU box is not worth it. |
| E2 | **No new NuGet package.** `.xlsx` is a hand-rolled minimal SpreadsheetML writer (~200 lines, `ZipArchive` + `XmlWriter`, both in the shared framework) | `Directory.Build.props` sets `RestorePackagesWithLockFile` + `TreatWarningsAsErrors`; ClosedXML drags an image stack onto a 3 GB box, EPPlus is commercial. Escalate to `DocumentFormat.OpenXml` only if formulas/multi-sheet are ever needed. |
| E3 | **~60 reports are 60 *classes*, not 60 *pages*** | One page renders the grammar; each report returns a `ReportTable`. Collapses the route-table guard to one row, makes the grammar undriftable, makes every report unit-testable without Razor. |
| E4 | **Reports over inert tables are deferred, not stubbed** | `hr.loan`, `hr.loan_installment` and `hr.employee_ledger_entry` (PF/welfare/tax) have **zero writers** today. Their reports land in spec 0057 alongside the writers. Listed explicitly in 0055's Out of scope. |
| E5 | **The audit gaps get fixed** | `Masters.cshtml.cs` (create/rename/retire), `Roster.cshtml.cs` (publish + entries) and `Policies.cshtml.cs` (payroll policy) write with **no `audit.Append`**. An activity log claiming "every write in M16" would be false. |

---

## Phase 1 — spec 0055, in build order

### 1. Kernel time primitives — delete the duplication first

New in `src/Hms.Kernel/Time/` beside `FlexibleDate.cs` / `BusinessDayCalendar.cs` / `FiscalCalendar.cs`:

- **`Dhaka.cs`** — `Zone`, `ZoneName`, `Local`, `Today(TimeProvider)`, `MidnightUtc(DateOnly)`.
  `TimeSpan.FromHours(6)` is currently hand-written in 4 places including
  [Ipd/Reports.cshtml.cs:36](src/Hms.Web/Pages/Ipd/Reports.cshtml.cs#L36) (already drifted off the
  kernel helper), `Hms.Hr/AttendanceService.cs:320` and `CsvPunchSource.cs:34`.
  `Ui.Local` / `Ui.DhakaMidnightUtc` become one-line forwarders — **zero call-site churn** across 50+ files.
- **`DateRange.cs`** — `readonly record struct`, private ctor so `Of(a,b)` is the only door and an
  inverted range is unrepresentable (the swap guard is duplicated in 3 page models today).
  `Instants()` returns the half-open UTC interval — the one place `+06:00` is written.
- **`Period.cs` / `PeriodResolver.cs` / `PeriodWords.cs`** — `PeriodGrain { Day, Week, Month,
  Quarter, Year, LeaveYear, FinancialYear, Custom }`, `PeriodCompare`, `PeriodHealth { Ok,
  PartlyFuture, WhollyFuture, TooLong }`. Resolution is a **pure function** of
  `(PeriodQuery, today, PeriodCalendar)` — no clock, no DI, no I/O.
  - Leave-year and financial-year are **grains, not presets**, so the arrow steps to the previous
    *leave year* rather than by 365 days.
  - "Last 12 months" is `Grain.Month` with `Units = 12`.
  - `PeriodCalendar(LeaveYearStartMonth, FinancialYearStartMonth, WeekStartsOn, MaxRangeDays)` —
    `WeekStartsOn = Saturday` as a named commented constant (**not** `CultureInfo`, which gives
    Sunday and is quietly wrong for Bangladesh).
- `FiscalCalendar` gains `RangeOf` / `RangeStartingIn` **additively** — `FiscalYearOf` is untouched
  because ADR-0004 numbering depends on its label.

### 2. Period delivery seam

- `IPeriodCalendarSource` in `src/Hms.Shell/Reporting/` (+ a `Default` implementation).
- `HrPeriodCalendarSource` in `src/Modules/Hr/Hms.Hr/` reads the effective
  `hr.payroll_policy.LeaveYearStartMonth`, falls back to 7 when the employer has configured none
  (never an invented value — the rule `PolicyResolver` already lives by), caches 10 min.
- One registration line per host. Dependency points inward, exactly as `HrTxAdapter` implements `IHrTx`.

### 3. The period selector — `src/Hms.Shell/Pages/Shared/_PeriodBar.cshtml`

A **partial**, not a tag helper (a tag helper's markup is invisible to `eng/check-css-classes.sh`,
which greps `.cshtml` only) and not a view component (which would invite a second period resolution).

- The grain control **stays a `<select data-submit>`** with the two custom-date inputs carrying
  `hms-date hms-date-implies="#PeriodGrain"`. `hms-date.js` requires an `HTMLSelectElement` — chips
  would silently break "a typed date always wins", ADR-0020 rule 4's banned defect class.
- Arrows, presets and comparison are server-computed `<a href>`. **Zero new JavaScript.**
- **URL is truth, cookie is memory.** A request with no period param → read cookie `hms.period` →
  **302 to the canonical URL**. Any explicit param wins outright. That 302 is what makes a period a
  shareable link (two people opening one URL must see one thing) and satisfies §12.3's "it is shown,
  never silently reused". Presets store as the preset key, so returning in September gives September.
- `WhollyFuture` → the query never runs, empty + a sentence. `TooLong` → confirm interstitial before
  running. Both in the base, so no report author can get them wrong.
- Nine new CSS classes into `app.css` **in the same commit** or CI fails.
- Proven on **`/hr/attendance` first**, before any report exists — cheapest validation of the
  cookie/redirect/`hms-date-implies` interaction.

### 4. Report grammar + the one page

`src/Hms.Shell/Reporting/`: `ReportColumn`, `ReportCell`, `ReportRow`, `ReportTable`,
`FilterChip`, `FilterSet`, `ReportDescriptor`.
`src/Modules/Hr/Hms.Hr.Screens/Reports/`: `IHrReport`, `ReportContext`, `HrReportFilters`,
`ReportCatalog`, then one file per report.
One page: `Pages/Hr/Reports.cshtml` at `@page "/hr/reports/{key?}"`.

Three invariants make §12.4 structural rather than review discipline:

- `ReportTable`'s constructor **throws if `Rows` is empty and `EmptyReason` is blank** — §12.4.4.
- `ReportCell.Href` can only be built by `ReportContext.Drill`, which always folds in the period and
  live filters — §12.4.5 cannot be forgotten.
- `ReportCsv.Write` / `ReportXlsx.Write` have one signature, `(ExportHeader, ReportTable) → byte[]`,
  with **no** `DbContext`, `IHrTx` or `HrScope` in reach. They cannot fetch a row the screen did not
  have. An arch test reflects over both and fails on any EF/data type — that test *is* §12.4.6's
  "exactly what the screen showed".

Salary confidentiality (D6) is enforced once, in the page, for all sixty: a `SalaryBearing`
descriptor requires `hr.reports.salary`, and a user without it gets the report **listed as
unavailable with the reason** — never a silently truncated version — with zero commands issued
against `hr.payroll_line`. Salary-report access is itself audited (N-HR8).

### 5. Reports — the Phase-1-feasible inventory

Every §13 report whose data exists today. People (directory, headcount summary + movement, joiners,
separations, attrition, service length, service record), Time (**muster roll**, daily summary, late
register, absence register, OT register, exception log, correction log, device/import health), Leave
(register, balance statement, availed analysis, pending ageing, LWP register), Payroll `$` (salary
sheet, summary by unit/designation/grade, component register, deduction register, employer cost,
comparison across periods, run audit), Governance (activity log, per-employee audit, approval
history).

Deferred to their phases: loans, PF/welfare/tax ledgers, TDS (0057); bonus, increment, settlement
registers (0057/0056); comp-off, roster-vs-actual, coverage (0058).

### 6. Three dashboards

Rebuild `/hr` as the HR command centre + two new: manager and employee. All period-selectable,
comparison available, **every figure drills through** (D4). Row 1 is the action-required panel
(exceptions blocking payroll + days to cut-off, leave awaiting decision with oldest age, probations
and contracts due, expiring licences/documents, the open run and its next action) and says "nothing
needs you today" when empty. Manager dashboard is scoped to the holder's reporting line and shows
**no salary anywhere**.

Fixes a real defect on the way: [Index.cshtml.cs:41](src/Modules/Hr/Hms.Hr.Screens/Pages/Hr/Index.cshtml.cs#L41)
counts `Incomplete` only, while `AttendanceService.ExceptionsAsync` and the Attendance screen count
`Incomplete OR Absent` — the dashboard has been under-reporting exceptions.

### 7. Employee timeline (§15)

One chronological stream on the employee record, from data that already exists: `hr.employment_event`
(append-only, fully attributed), assignment history, pay-structure history, leave applications with
their decision trail, `hr.attendance_correction` (before/after/reason already on the row), payroll
run state timestamps, ledger entries. Filter by category chips and by year; the §12.3 control
applies. Salary-bearing entries **hidden entirely** without salary read, with their absence stated
("3 compensation entries hidden") so the record is never misread as complete. Printable as a
**Service Record** with the signature block a Bangladeshi employer expects.

Known wrinkle: `EmployeeAssignment.CreatedBy` / `EmployeePayStructure.CreatedBy` are ids with no name
snapshot — resolve the actor name from `kernel.audit_event` rather than a cross-context join.

### 8. Activity log + the audit backfill

Read-only over `kernel.audit_event` filtered to HR, with the §12.3 period control, filterable by
employee, actor, action and area, exportable, never editable. Reuses the
[Admin/Audit.cshtml.cs](src/Hms.Web/Pages/Admin/Audit.cshtml.cs) pattern — including its documented
trap that jsonb has no ILIKE, so text search must go through `FromSql` with `after::text ILIKE`.

Backfill (E5): add `audit.Append` to `Masters.cshtml.cs`, `Roster.cshtml.cs` and `Policies.cshtml.cs`.

### 9. Menu restructure + permissions

Eleven flat entries → the PRD §12.1 groups (Dashboard, People, Time, Leave, Payroll, Reports, Setup,
My space, Governance). One level only — that is all `NavGroup` supports, and it matches the PRD's
two-level cap. Collapsible groups already exist from spec 0050.

**All new routes stay under `/hr`** — `ModuleNav.BuildPrefixes()` derives entitlement prefixes from
the first URL segment and **throws at static init on collision**, i.e. a boot failure in both hosts.

Phase-1 permissions: `hr.reports.view`, `hr.reports.salary`, `hr.audit.view`, `hr.team.view`,
`hr.self`. Each needs **both** forms in `HrPerm` plus `Claim.All`, a `ROUTES` row in
`eng/verify/role-journeys.py`, and an enforcement point the guard can **grep as a string literal** —
`Can(HrPerm.Claim.X)` compiles, is functionally correct, and still fails CI.

**Upgrade grant, not optional:** existing roles hold none of these, so the report centre would render
empty for everyone. Grant `hr.reports.view` to every role holding `hr.read`, and `hr.reports.salary`
to every role holding `hr.salary.read`.

---

## Critical files

| File | Change |
|---|---|
| `src/Hms.Kernel/Time/` | new `Dhaka.cs`, `DateRange.cs`, `Period.cs`, `PeriodResolver.cs`, `PeriodWords.cs`; additive methods on `FiscalCalendar.cs` |
| `src/Hms.Shell/Ui.cs` | `Local` / `DhakaMidnightUtc` become forwarders |
| `src/Hms.Shell/Reporting/` | new — grammar types, `FilterSet`, `ReportCsv`, `ReportXlsx`, `Download`, `IPeriodCalendarSource` |
| `src/Hms.Shell/Pages/Shared/` | new `_PeriodBar`, `_ReportHeader`, `_ReportTable`, `_FilterChips` |
| `src/Hms.Shell/wwwroot/css/app.css` | nine new classes + `@page` A4 portrait/landscape + `.sheet-runner` |
| `src/Modules/Hr/Hms.Hr.Screens/Reports/` | new — `IHrReport`, `ReportCatalog`, one class per report |
| `src/Modules/Hr/Hms.Hr.Screens/Pages/Hr/` | new `Reports`, `Timeline`, `ActivityLog`, manager + employee dashboards; rebuild `Index` |
| `src/Modules/Hr/Hms.Hr.Screens/HrPerm.cs`, `HrNav.cs` | 5 permissions (both forms + `Claim.All`); the 8-group menu |
| `src/Modules/Hr/Hms.Hr.Screens/Pages/Hr/{Masters,Roster,Policies}.cshtml.cs` | the missing `audit.Append` calls |
| `eng/verify/role-journeys.py` | `ROUTES` + `ROLE_GRANTS` for every new route/claim |
| `src/Hms.Web/DevSeed.cs`, `src/Hms.Hr.Web/HrSeed.cs` | grant the new claims |

---

## Verification

**Kernel tests** (ms, no DB) — the whole period surface: all ten presets at leave-year start 7/1/4;
arrows across month lengths, year boundaries, 29 Feb, Q4→Q1; `DateRange.Of` swaps an inverted pair;
`Instants()` returns exactly `18:00Z`; the PRD's literal `"1 – 31 July 2026 · Asia/Dhaka"`;
**`Period → ToQuery → Resolve` round-trips** over every grain × preset × compare (~500 cases — this
is load-bearing, the URL *is* the sharing mechanism); `PeriodHealth` boundaries;
`FiscalCalendar.RangeOf` agrees with `FiscalYearOf`.

**Web tests** (pure) — CSV quoting (comma, quote, CRLF, leading `=` formula injection, Bangla),
**UTF-8 BOM present** (without it Excel mojibakes Bangla names); `.xlsx` opens as a `ZipArchive`,
every part parses, money cells are numeric with the lakh-crore `numFmt`, header row is row 7 and
unique; `Download.Name` sanitisation.

**Integration tests** (real Postgres, `BranchScope.Current` first line) — `HrPeriodCalendarSource`
honours effective dating and falls back to 7; **catalog smoke**: every report runs against an empty
and a seeded branch without throwing, and every empty result carries a reason; **branch isolation per
report** (seed two branches, assert no branch-2 identifier leaks — this catches a report reaching
`RosterEntry`/`PayrollComponentLine`/`Holiday` directly, which have no `BranchId` and no global
filter); **salary refusal reads nothing** (assert via an EF command interceptor that zero commands
hit `hr.payroll_line`); **performance budget**: 1,000 employees × 400 days, a full-year report under
a wall-clock budget, never materialising more than `MaxRows`.

**Architecture tests** — `SalaryBearing ⇒ hr.reports.salary`; the salary-bearing key set matches §13's
`$` markers literally; report keys unique/stable/kebab-case; **exporters see only the grammar** (no
EF type in reach); **no report builds a URL by hand**; **every period screen renders `_PeriodBar`**;
**no ad-hoc date range** (`TimeSpan.FromHours(6)`, the swap idiom) outside `Kernel/Time` — starts with
an allowlist of the four known offenders that only ever shrinks.

**Guards** — `dotnet test hms-erp.slnx -c Debug` plus `eng/check-css-classes.sh`,
`check-ui-tokens.sh`, `check-no-native-date.sh`, `check-lifecycle-traceability.sh`,
`check-additive-migrations.sh`.

**End to end** (`eng/verify/hrm-thread.py`, `_harness`) — a report URL renders the exact period
sentence; a cookie-only request 302s with `p=`; a 2099 period returns **200** with the future
sentence and zero rows (not 500, not a hang); a role without `hr.reports.salary` gets the
unavailability sentence and **no taka figure anywhere in the body**.

**Real app** — run both hosts (`Hms.Web` and `Hms.Hr.Web`), walk the report centre, the three
dashboards, a timeline and the activity log, and print one report to confirm the letterhead and the
repeated period.

---

## Open questions carried (not blocking Phase 1)

The PRD's Q-HR-1..10 stay with the PM/customer. Two are ours to raise as they bite: **large exports**
(a year's muster roll exceeds the display cap — background job + file delivery is a phase-2/3
decision, recorded now rather than discovered) and **week start** (no employer configuration exists;
Saturday is a commented default until the Shifts & weekly-offs setup screen lands in 0058).

## Process

Spec-driven per hard rule 0: `docs/specs/0055-hr-reporting-and-period-standard/` gets `spec.md` +
this `plan.md` on approval, a row in `docs/specs/README.md`, and an **ADR** for the period control /
report grammar / drill-through contract (rule 1 — that is an architecture decision, not a PM one).
Specs 0056–0059 open as their phases begin. `graphify update .` after each phase.
