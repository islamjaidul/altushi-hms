# 0029 — One period control, one report grammar, and reports as classes rather than pages

- **Status:** Accepted
- **Date:** 2026-08-06
- **Answers:** module PRD `docs/m16-hr-payroll-prd.md` §12.3, §12.4, §12.5, D3, D4; PRD §7 U9/U10/U11
- **Spec:** `docs/specs/0055-hr-reporting-and-period-standard/`
- **Constrains:** every reporting surface added by M16 phases 2–5 (specs 0056–0059)

## Context

M16 shipped with no reports, and with ten different notions of "when": `/hr/attendance` took one
date, `/hr/payslips` took a run, `/hr` was hardcoded to today. The module PRD asks for roughly sixty
registers, three dashboards, a timeline and an activity log — all of which display through whatever
period and table conventions we choose now.

The rest of the product shows what happens without a decision here. Five report pages exist across
Billing, Pharmacy, IPD and OT. Each declares its own `From`/`To`/`Range` properties, its own
resolution `switch`, its own row records and its own filter bar. The swap-if-inverted guard is
written out in three of them. `Ipd/Reports.cshtml.cs` has already drifted off the shared helper and
hand-writes `TimeSpan.FromHours(6)`. There is no `DateRange` type anywhere in the codebase, no
shared table or filter partial, and no export of any kind — `QuestPDF` is installed and wired to
nothing but a spike test.

Sixty more reports built that way would be sixty screens to learn, sixty places for the Dhaka offset
to be wrong, and sixty chances to forget that a salary column needs a permission.

## Decision

### 1. The period is a Kernel value type, resolved by a pure function

`Hms.Kernel.Time` gains `Dhaka`, `DateRange`, `Period`, `PeriodCalendar`, `PeriodResolver` and
`PeriodWords`. Resolution is a pure function of `(PeriodQuery, today, PeriodCalendar)` — no clock, no
DI, no I/O — so the entire period surface is testable in milliseconds without a database or a host,
which is where nearly all of the correctness risk lives.

`DateRange` has a private constructor and one factory that orders its arguments, so an inverted range
is not merely discouraged but unrepresentable. `DateRange.Instants()` is the single place the
`+06:00` boundary enters a query.

**Leave year and financial year are grains, not presets.** If they were presets flattened to a custom
range, the back arrow would step by the range's length rather than to the previous year — wrong for
any employer whose year does not start in January. Both start months are employer configuration
supplied through `IPeriodCalendarSource`, which HR implements and the Shell only declares; the Kernel
never learns what a payroll policy is.

### 2. The URL is the period; a cookie is only memory

A request naming a period gets that period. A request naming none is **302'd to the canonical URL**
for the remembered one. That redirect is the whole mechanism: without it two people opening the same
link see different data, which is not a shareable link. With it the address bar always states a
concrete period — §12.3's "it is shown, never silently reused".

The cookie stores the *preset* where there was one, so an operator who left on "This month" and
returns in September gets September. Session is not used: it is in-process memory on a 3 GB box,
does not survive a restart, and cannot be shared.

### 3. ~60 reports are 60 classes sharing one page

`/hr/reports/{key?}` is a single Razor page. A report is a class implementing one method that returns
a `ReportTable`. It contains no permission code, no period code, no export code, no Razor and no DI
beyond the transaction scope it is handed.

This is the highest-leverage decision here:

- The route-table guard needs **one** row instead of sixty hand-synced ones.
- The grammar cannot drift, because a report author cannot reach it.
- Every report is unit-testable without Razor or a host.
- "Learn one report and you have learned all of them" (§7 U9, D3) becomes a property of the build
  rather than of review discipline.

### 4. Three guarantees are structural, not procedural

Each of §12.4's easily-forgotten requirements is expressed so that the only way to do the thing also
does the right thing:

| Requirement | How it is enforced |
|---|---|
| §12.4.4 — an empty report explains itself | `ReportTable.Validated()` throws when `Rows` is empty and `EmptyReason` is blank |
| §12.4.5 / D4 — a drill-through carries its period and filters | `ReportCell.Href` can only be built by `ReportContext.Drill`, which folds both in; an architecture test forbids a report composing a report URL |
| §12.4.6 — the export is what the screen showed | `ReportCsv.Write` / `ReportXlsx.Write` take `(ExportHeader, ReportTable)` and can reach no `DbContext`, transaction or report class; an architecture test reflects over both and fails on any data type in reach |

Salary confidentiality (D6) is enforced once, in the page, for all sixty: a `SalaryBearing` descriptor
requires `hr.reports.salary`, and a reader without it gets the report **listed as unavailable with
the reason** — never a silently truncated version — with no query issued against `hr.payroll_line`.
Salary-report access is itself audited (N-HR8).

### 5. Export: CSV and .xlsx hand-rolled; PDF is the existing print path

CSV is RFC 4180 with a UTF-8 BOM — without the BOM Excel on a Bangladeshi desktop mojibakes every
Bangla name. `.xlsx` is a minimal SpreadsheetML writer over `ZipArchive` and `XmlWriter`, both in the
shared framework: **no new package**. Numbers are written as numbers with the lakh–crore
`numFmt`, so the file both sums and reads the way the screen did.

PDF is the browser-print path the product already uses for eight documents plus the payslip
(`_Letterhead`, `_SheetFooter`, `.sheet` CSS, `@media print`), extended with A4 portrait/landscape
`@page` rules and a fixed-position running head that repeats the period on every printed page.

## Consequences

**Accepted:**

- `QuestPDF` stays installed and unwired. Revisit only if a customer rejects browser-print output;
  sixty hand-written PDF layouts is not a defensible cost on 2 vCPU (§16).
- Page numbers on print come from the browser's own print profile. CSS `@page` counters are
  implemented by no browser, and faking them would print a number that is wrong — ADR-0009 already
  documents a per-counter print profile, so this rides that.
- Row caps are real. `MaxRangeDays = 400` warns before running; `ReportLimits.MaxRows` truncates with
  "showing N of M" stated on screen and in the export. At 1,000 employees a five-year attendance
  range is 1.8M rows — an out-of-memory failure, not a slow page. **Aggregation happens in SQL**
  (`GroupBy` before `ToListAsync`, never after).
- The export and the display cap collide for very large registers: a full-year muster roll cannot be
  exported in full. The honest interim answer is a stated ceiling; a background export job is a
  separate decision and is recorded as an open question rather than discovered later.
- Week start is `Saturday` as a named, commented constant. No employer "week starts on" configuration
  exists yet; it becomes one when the Shifts & weekly-offs setup screen lands (spec 0058).
  `CultureInfo` is deliberately not used — under the invariant culture it returns Sunday, which is
  quietly wrong for Bangladesh.

**Obligations on phases 2–5:**

- A new report is a class in `ReportCatalog`, never a page.
- A new period-bearing screen renders `_PeriodBar`; an architecture test lists them.
- The Dhaka offset is `Hms.Kernel.Time.Dhaka` and nowhere else. Three pre-existing offenders are on
  an allowlist that only ever shrinks.

**Superseded practice:** the five existing bespoke report pages in Billing, Pharmacy, IPD and OT are
now the old way. They are not migrated by this ADR — that is separate work — but no new one should be
written, and `Ipd/Reports.cshtml.cs`'s hand-written offset is a tracked cleanup.
