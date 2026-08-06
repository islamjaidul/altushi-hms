# 0055 — M16 Phase 1: make it answerable (period standard, report centre, dashboards, timeline, activity log)

- **Status:** Done
- **Date:** 2026-08-06
- **PRD ref:** `docs/m16-hr-payroll-prd.md` §12.3, §12.4, §12.5, §13, §14, §15, §7.12–7.14;
  main PRD §5 M16, §5A-17, §5A-20, §7 U1/U9/U10/U11/U12, §12, §16
- **Parent:** `docs/specs/0054-hr-payroll-industry-standard/` — 0054 wrote the module PRD and
  excluded implementation. This is the first of five build specs (0055–0059) delivering it.
- **MVP:** n/a — the §9A.2 freeze was lifted 2026-07-27.

## Problem

M16 shipped and works, but it is a payroll engine with an employee record attached. Three things a
buyer notices in the first ten minutes, all of them read-side:

**1. It cannot report.** Eleven nav entries, not one a report. No muster roll, no salary sheet, no
leave register, no headcount movement. The data exists in `hr.*`; there is no way to get it out.
Elsewhere in the product there are five bespoke report pages with no shared scaffolding — four
independent re-implementations of date-range resolution, one of which
(`src/Hms.Web/Pages/Ipd/Reports.cshtml.cs:36`) has already drifted off the kernel helper and
hand-writes `TimeSpan.FromHours(6)`.

**2. Nothing has a period.** `/hr/attendance` takes one date; `/hr/payslips` takes a run; `/hr` is
hardcoded to `DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6))` and this month. There is no
`DateRange` type anywhere in the codebase, no period resolver, and no fiscal-year range helper —
`FiscalCalendar` labels a string and gives no dates. Selecting a month, a quarter or a year is the
single most common thing an HR administrator does, and the module cannot do it.

**3. The employee record is four tables on one page.** `hr.employment_event` is already append-only
and attributed, and assignment, pay-structure, leave and attendance history all carry dates — the
substrate for a service record exists and is rendered as four lists nobody can read as one life.

Underneath: writes in `Masters.cshtml.cs`, `Roster.cshtml.cs` and `Policies.cshtml.cs` emit **no
audit row at all**, so any "activity log" built today would be quietly incomplete.

## Requirements

- [M] **A period standard** — day / week / month / quarter / year / leave year / financial year /
  custom, with presets, arrows, comparison, the resolved range stated in words with the timezone,
  persistence across screens, and the period in the URL so it is a shareable link. Binding on every
  report, register, dashboard, calendar and log in M16 (§12.3).
- [M] **A report grammar** shared by every report — header, filter chips, sortable columns with
  totals and a stated row count, an explanatory empty state, drill-through from every summary figure,
  export, print, and a permission model where salary-bearing reports are *listed as unavailable with
  the reason* rather than silently truncated (§12.4, D3).
- [M] **The Phase-1 report inventory** — every §13 report whose data exists today, across People,
  Time & attendance, Leave, Payroll and Governance.
- [M] **Export** — CSV and Excel server-side; PDF via the product's existing browser-print path with
  letterhead. The export contains exactly what the screen showed, including its filters.
- [M] **Three dashboards** — HR command centre, manager, employee; period-selectable, comparison
  available, every figure drilling through to the rows behind it (§14, D4).
- [M] **An employee timeline** — one chronological service record, filterable by category and year,
  printable as a Service Record, with salary-bearing entries hidden entirely (and their absence
  stated) without salary read (§15, D5, D6).
- [M] **An activity log** over every M16 write, filterable by employee, actor, action, area and
  period, exportable, never editable (§7.14, G3).
- [M] **The audit gaps closed** so the log's claim is true: master, roster and payroll-policy writes
  become audited.
- [M] **The menu restructured** into the §12.1 groups; a group with no permitted entry does not render.
- [M] **New permissions** — `hr.reports.view`, `hr.reports.salary`, `hr.audit.view`, `hr.team.view`,
  `hr.self` — with an upgrade grant, since existing roles hold none of them.
- [M] Salary confidentiality (D6) preserved on every new surface: screen, report, export, print,
  timeline and log.
- [M] Both SKUs (N-HR10). Every new surface works in the standalone HRM host with no hospital module
  present.
- [S] Saved report views (G5) — deferred to 0059 unless it falls out cheaply.

## Acceptance criteria

1. One period control resolves identically on every M16 surface; its selection is in the URL, is
   stated in words with `Asia/Dhaka`, and survives navigation between screens.
2. A period URL sent to another user renders the same range for them.
3. A future period returns an empty result with a sentence and **200**, never an error and never a
   hanging query. A range beyond the configured maximum warns before running.
4. Every report reaches the same header, filter, export, print and empty-state grammar; adding a
   report cannot bypass it.
5. A user without `hr.reports.salary` sees every salary-bearing report listed as unavailable with a
   reason, and no query runs against `hr.payroll_line` for them.
6. Every dashboard figure opens the rows behind it, in the same period.
7. An employee's timeline shows employment, assignment, pay, leave, attendance-correction and
   payroll entries in one stream, prints as a Service Record, and states how many entries are hidden
   when the reader lacks salary read.
8. The activity log shows every M16 write for a selected period — including master, roster and
   policy writes, which are audited by this spec.
9. No statutory rate, slab, entitlement or formula is embedded (N-HR11, D2); the leave-year and
   financial-year starts are read from employer configuration and fall back to a stated default.
10. All guards pass: `check-css-classes.sh`, `check-ui-tokens.sh`, `check-no-native-date.sh`,
    `check-lifecycle-traceability.sh`, plus the full test suite.

## Out of scope

| Deferred | Reason | Goes to |
|---|---|---|
| Loan, advance, PF, welfare and tax reports | `hr.loan`, `hr.loan_installment` and `hr.employee_ledger_entry` have **zero writers** today — these reports would render permanently empty. They land with their capture side. | 0057 |
| Bonus, increment, promotion, settlement registers | Same: the processes do not exist yet. | 0056 / 0057 |
| Comp-off ledger, roster-vs-actual, shift coverage | Depend on OT banking and roster patterns. | 0058 |
| Server-rendered PDF (QuestPDF) | The browser-print path is mature and already carries 8 documents plus the payslip. 60 hand-written PDF layouts is not a defensible cost on a 2-vCPU box. Revisit only if a customer rejects print output. | — |
| Background job for very large exports | A full-year muster roll exceeds the display cap. The interim answer is a stated ceiling and a confirm interstitial; a job + file delivery is a real design decision. | 0057 or later |
| Scheduled / saved report views | Should, not Must. | 0059 |
| Charts beyond the §14 dashboard set | §12.5 governs when they appear; the dashboards' trend charts are in scope, a general charting capability is not. | — |

## What landed

| Area | Delivered |
|---|---|
| Period standard | `Hms.Kernel.Time`: `Dhaka`, `DateRange`, `Period`, `PeriodCalendar`, `PeriodResolver`, `PeriodWords`; `FiscalCalendar.RangeOf`/`RangeStartingIn`. `Hms.Shell.Reporting`: `PeriodBinding` (URL + cookie + the canonicalising 302), `PeriodBar`, `IPeriodCalendarSource`; `HrPeriodCalendarSource` reads the employer's leave year. `_PeriodBar.cshtml` renders it, with zero new JavaScript. |
| Report grammar | `ReportTable`/`ReportRow`/`ReportCell`/`ReportColumn`/`ReportDescriptor`/`FilterSet`/`FilterChip`/`ReportView`; partials `_ReportHeader`, `_ReportTable`, `_FilterChips`. |
| Report centre | One page at `/hr/reports/{key?}` + `ReportCatalog`. **26 reports**: 6 People, 8 Time, 5 Leave, 5 Payroll, 2 Governance. |
| Export | `ReportCsv` (RFC 4180, UTF-8 BOM, formula-injection defused), `ReportXlsx` (hand-rolled SpreadsheetML, no new package, numeric money cells with lakh–crore format), `Download` with a sanitised filename. Print via the existing letterhead path plus A4 portrait/landscape `@page` and a running head. |
| Dashboards | `/hr` rebuilt as the HR command centre (action-required panel, 8–10 drill-through tiles, period-selectable); `/hr/team` new manager dashboard, scoped to the holder's reporting line, showing no salary at all. |
| Timeline | `/hr/employees/{id}/timeline` — one chronological stream over employment events, assignments, leave, attendance corrections, ledger and compensation; category chips; printable as a Service Record with letterhead and signature block; compensation entries hidden with their count stated. |
| Activity log | `activity-log` and `employee-audit` registers over `kernel.audit_event`, gated on `hr.audit.view`. |
| Audit backfill | `audit.Append` added to `Masters` (create/rename/retire/restore), `Roster` (assign) and `Policies` (payroll policy) — all three previously wrote nothing. |
| Permissions | `hr.reports.view`, `hr.reports.salary`, `hr.team.view`, `hr.audit.view`, `hr.self`, in both forms plus `Claim.All`, with seed grants. `/hr/me` moved to `hr.self`; `hr.leave.apply` now gates the apply handler rather than the page. |
| Menu | Nine groups (Dashboard, People, Time, Leave, Payroll, Reports, Setup, My space, Governance), one level, every route under `/hr`. |
| Defect fixed on the way | The dashboard's exception count read `Incomplete` only, while `AttendanceService` and the Attendance screen count `Incomplete OR Absent` — it had been under-reporting exactly what blocks payroll. |

**Verification:** 505 tests green (156 Kernel, 257 Web, 92 Architecture) plus the integration suite;
`check-css-classes`, `check-ui-tokens`, `check-no-native-date`, `check-fkeys`,
`check-no-external-hosts` and `check-lifecycle-traceability` all pass. Architecture tests added to
stop phases 2–5 drifting: exporters may reach no data type, no report may build a report URL, every
period screen renders `_PeriodBar`, the payroll registers are all salary-bearing, and nobody
hand-writes the Dhaka offset.

## Deferred within Phase 1

| Deferred | Why |
|---|---|
| Employee dashboard beyond the existing `/hr/me` | `/hr/me` was re-doored on `hr.self` and keeps its leave surface; the fuller "my attendance / my payslips / my requests" space is ESS work and belongs with 0059. |
| Saved report views (G5) | `[S]`, and cheaper once the catalogue has settled. |
| Leave calendar and org chart | `[M]` in §14.1 row 4 / §7.2 but calendar-shaped rather than register-shaped; they need a month-grid component that phase 4's roster work will also want. |
| Trend charts on the command centre | §12.5 governs them; the tiles and registers carry the same figures today, and a chart with no drill-through would breach D4. |

## Notes

Sequencing is deliberate: the Kernel time primitives land first and **delete** existing duplication
(the swap-if-inverted guard in three page models, `TimeSpan.FromHours(6)` in four files) rather than
adding a sixth way to do it. The period control is proven on `/hr/attendance` before any report
exists, because the cookie/redirect/`hms-date-implies` interaction is the riskiest part and the
cheapest place to be wrong.

The load-bearing structural decision is that ~60 reports are 60 **classes**, not 60 pages: one page
renders the grammar, each report returns a `ReportTable`. This is what keeps the route table at one
row, makes the grammar undriftable across four later phases, and lets a report be tested without
Razor. The architecture decision is recorded as an ADR (rule 1 — the period control, report grammar
and drill-through contract are architecture, not product).
