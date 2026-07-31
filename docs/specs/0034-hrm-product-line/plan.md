# 0034 — Plan

## Approved: 2026-07-31

Archived from the approved session plan. Waves A–D each get their own spec (0036–0039); the platform
enablers are spec 0035. This plan holds the traceability matrix and the cross-wave decisions.

## 1. Traceability matrix (PRD → surface; built first, per DoD rule 1)

Status is **Build (wave)** or **Deferred: reason**.

### §5 Module 16 — `[M]`/`[S]`/`[C]` sub-features

| # | PRD item | MoSCoW | Surface | Status |
|---|---|---|---|---|
| 16.1 | Employee records (personal, official, salary grade, department, designation, document attachments) | M | `/hr/employees`, `/hr/employees/{id}` | Build (A) |
| 16.2 | Biometric attendance capture (live punch feed) | M | `IPunchSource` adapter + `/hr/attendance/import` | **Partial (A)** — file import + manual; live feed Deferred: devices not installed (§9A.3) |
| 16.3 | Attendance admin review/correction with reason | M | `/hr/attendance`, `/hr/attendance/corrections` | Build (A) |
| 16.4 | Shift & roster management (24/7 rotating) | M | `/hr/shifts`, `/hr/roster` | Build (A) |
| 16.5 | Leave: types, balances, applications, approvals, without-pay | M | `/hr/leave/*` | Build (A) |
| 16.6 | Payroll: auto salary sheet from attendance, bonus entry, pay slips, posts to M15 | M | `/hr/payroll/*`, `IPayrollPosting` | Build (A); bonus → B; posting → contract only (M15 absent) |
| 16.7 | Loan/advance with installment deduction | S | `/hr/loans` | Build (C) |
| 16.8 | Provident fund setup, employee PF history & PF ledger | S | `/hr/pf/*` | Build (C) |
| 16.9 | Online leave application (self-service) | S | `/hr/me/leave` | Build (A) |
| 16.10 | Birthday/anniversary lists | C | `/hr/reports/occasions` | Build (D) |
| 16.11 | Honor-duty tracking | C | — | Deferred: PrimeMIS-only, no BD-market evidence of demand; revisit on customer pull |

### §5A-16 (Should) and §5A-17 (Could)

| # | PRD item | Surface | Status |
|---|---|---|---|
| 5A-16a | Comp-off requests | `/hr/comp-off` | Build (B) |
| 5A-16b | Overtime bank ledger | `/hr/overtime/bank` | Build (B) |
| 5A-16c | OT assist fee | payroll component | Build (B) |
| 5A-16d | Weekly off | `/hr/masters/weekly-off` | Build (B) |
| 5A-16e | Grace time | policy row + `/hr/policies/grace-time` | Build (B) |
| 5A-16f | Holiday-work pay policy | `/hr/policies/holiday-pay` | Build (B) |
| 5A-16g | Tiered leave approval | `ApprovalEngine` policy rows | Build (A) — tier count is data |
| 5A-16h | Leave policy / balance setup | `/hr/leave/policies`, `/hr/leave/balances` | Build (A) |
| 5A-16i | Bonus (register/create/sheet) | `/hr/bonus/*` | Build (B) |
| 5A-16j | Increment policy | `/hr/increments` | Build (B) |
| 5A-16k | Promotion management | `/hr/promotions` | Build (C) |
| 5A-16l | Welfare & tax ledgers | `/hr/ledgers/welfare`, `/hr/ledgers/tax` | Build (C) |
| 5A-16m | PF withdrawal | `/hr/pf/withdrawals` | Build (C) |
| 5A-16n | Salary-deduct settings | `hr.deduction_rule` + `/hr/policies/deductions` | Build (A, engine) / (C, ledger-backed kinds) |
| 5A-17a | Appointment / experience / termination letters | `/hr/documents/*` | Build (D) |
| 5A-17b | Employee auth (login) history | `/hr/employees/{id}/auth-history` | Build (D) |
| 5A-17c | Job age limit | policy row, validated at hire | Build (D) |
| 5A-17d | Joinee / resigned / salary-compare / dept-summary reports | `/hr/reports/*` | Build (D) |

### Competitive additions `[obs: PiHR]`

| # | Item | Surface | Status |
|---|---|---|---|
| PiHR-1 | Digital notice board | `/hr/notices` | Build (B) |
| PiHR-2 | Expense management → reimbursement | `/hr/expenses/*` | Build (C) |
| PiHR-3 | Remote/GPS attendance, face recognition | — | Deferred: needs mobile/PWA platform (P29) |
| PiHR-4 | Task management | — | Deferred: not HR core |

### Cross-cutting PRD obligations

| Ref | Obligation | Where satisfied |
|---|---|---|
| §11 | Leave Application state machine | `LeaveService` guarded transitions |
| §11 | Payroll Run state machine (`Approved⚿` = approval engine) | `PayrollService` + `ApprovalEngine` type `payroll-lock` |
| §12 | HR Officer row; `U (own leave)` for Nurse/Technologist/Pharmacist | permission set + self-service role |
| §12 | Payroll run lock: HR Officer → Accounts Manager / MD | `kernel.approval_policy` seed row |
| §10 | Employee owned by M16, consumed by M15/M14/M21 | `Hms.Hr.Contracts` |
| §3.4 | TDS + TR Form 6, PF, Welfare, BEFTN bank master data | Wave C ledgers; bank file behind config (P14 precedent) |
| §13 I8 | Biometric devices | `IPunchSource`; live feed deferred |
| rule 4 | No financial hard deletes | reversal runs, arrears lines, append-only audit |
| rule 5 | Effective-dated | pay structures, policies, assignments — with Postgres exclusion constraints |

## 2. Decisions

Three ADRs are written with this spec:

- **ADR-0025 — two-host product line.** Extends ADR-0003/0005: one codebase, two composition roots,
  shared kernel + shared UI library. Modules stay assembly-per-module; a host is a packaging choice.
- **ADR-0026 — entitlement enforcement completion.** Delivers ADR-0016 choke point 2 and the P6
  grace/read-only behaviour. Without it, "sold separately" is honour-system-only.
- **ADR-0027 — payroll policy as effective-dated configuration.** Every rate, slab, entitlement and
  formula is customer data with an effective date; no statutory constant in C#.

PM questions raised: **P26** (BD statutory sources), **P27** (general-business SKU = new scope),
**P28** (multi-branch for the HRM SKU, ADR-0007 amendment), **P29** (per-module pricing; mobile
attendance).

## 3. Architecture

```
src/Hms.Kernel                      auth · permissions · audit · numbering · approvals ·
                                    jobs · entitlements · PDF · fiscal/business-day
src/Hms.Shell               NEW  RCL   _Layout · _Letterhead · _PrintTools · _SheetFooter ·
                                    tokens.css · app.css · js · fonts · HmsDateTagHelper ·
                                    Ui.cs · HmsPageModel · OrgIdentity
src/Modules/Hr/Hms.Hr    NEW        domain services + `hr` schema.  References Kernel ONLY.
src/Modules/Hr/Hms.Hr.Contracts NEW IPayrollPosting, PayeeRecord — what M15/M17 will consume
src/Modules/Hr/Hms.Hr.Screens NEW  RCL   every /hr/* Razor Page + HrPerm + HrNav

src/Hms.Web              host  ERP SKU      → Kernel + Hms.Shell + all 14 modules + Hms.Hr.Screens
src/Hms.Hr.Web           NEW host HRM SKU   → Kernel + Hms.Shell + Hms.Hr + Hms.Hr.Screens
```

### The transaction seam

`TxScope` is **not** refactored. `src/Hms.Web/HmsTx.cs` gains one line like every other module:
`public HrDbContext Hr => Attach<HrDbContext>(o => new(o), "hr");`

HR pages live in an RCL and cannot inject `HmsTx` (it needs all fourteen assemblies), so `Hms.Hr`
defines a narrow seam both hosts satisfy:

```csharp
public sealed class HrScope(KernelDbContext kernel, AuthDbContext auth, HrDbContext hr) { … }

public interface IHrTx
{
    Task<T> RunAsync<T>(Func<HrScope, Task<T>> body, CancellationToken ct = default);
    Task RunAsync(Func<HrScope, Task> body, CancellationToken ct = default);
}
```

The ERP host registers an adapter wrapping `HmsTx` (one connection, one transaction, G19 intact); the
HRM host registers a three-context implementation using the same `Attach` pattern. HR domain services
keep the house signature `(HrDbContext, KernelDbContext, long branchId, …, long actorId, string
actorName, CancellationToken)` and are indifferent to which host built the scope.

**Zero churn on the 76 existing pages.**

### Permissions and nav live in the RCL

`Hms.Hr.Screens` cannot reference `Hms.Web`, so HR permission constants and `NavItem`s live in
`HrPerm.cs` / `HrNav.cs` inside the RCL. Each host concatenates registries: `ModuleNav.Registry +
HrNav.Registry` in the ERP, `HrNav.Registry` alone in the HRM host.

## 4. Data model — `hr` schema

House rules: singular snake_case tables, `long Id`, money as `long` whole taka, `DateTimeOffset` UTC,
state as `string` + `static class` constants, no cross-schema FKs, `jsonb` for documents, `branch_id`
on every business row.

- **Masters** `org_unit · designation · grade · pay_scale · pay_component · location ·
  holiday_calendar · holiday · weekly_off_pattern · shift · leave_type`
- **People** `employee · employee_assignment` (effective-dated) · `employee_pay_structure`
  (effective-dated) · `employment_event` (append-only)
- **Time** `roster · roster_entry · punch` (immutable) · `attendance_day` (derived; unique
  `(employee_id, on_date)`) · `attendance_correction · errand · overtime_entry · overtime_bank ·
  comp_off`
- **Leave** `leave_policy` (effective-dated) · `leave_balance · leave_application · leave_encashment`
- **Pay** `payroll_run · payroll_line · payroll_component_line · payslip · bonus_run · bonus_line ·
  increment · loan · loan_installment · pf_ledger · welfare_ledger · tax_ledger · payout_batch ·
  final_settlement`
- **Expense / comms** `expense_claim · expense_claim_line · notice`
- **Policy-as-data (ADR-0027)** `tax_slab · pf_policy · gratuity_rule · overtime_rule ·
  grace_time_rule · holiday_pay_policy · deduction_rule` — all effective-dated, all customer-entered

Effective-dated ranges use Postgres **exclusion constraints** (`btree_gist` is already installed by
`deploy/db-init/01-roles.sh`) so overlapping validity is impossible, not merely checked.

## 5. Build order

**Wave 0 — platform** (spec 0035) · *nothing HR-specific ships; everything after depends on it*
1. Extract `src/Hms.Shell` RCL from `src/Hms.Web`. ERP behaviour must be unchanged — the existing
   Playwright suite is the regression net.
2. `IHrTx`/`HrScope` seam; `HmsTx.Hr` property.
3. ADR-0026: `RequireModule` filter, grace/read-only gating, entitlement admin upload.
4. `src/Hms.Hr.Web` host skeleton + `deploy/hrm.Dockerfile`, `compose.hrm.yml`,
   `entitlements/hrm-only.json`, RUNBOOK section.
5. Branch resolution for the HR host (P28); ERP host returns 1.
6. `--dept-hr` token; shared bootstrap `AddHmsPlatform()`/`UseHmsPlatform()` in Kernel so two
   `Program.cs` files cannot drift.

**Wave A — sellable core** (spec 0036) — matrix rows 16.1–16.6, 16.9, 5A-16g/h
**Wave B — time & pay policy depth** (spec 0037) — 5A-16a–f, i, j + PiHR-1
**Wave C — money ledgers** (spec 0038) — 16.7, 16.8, 5A-16k–n + PiHR-2
**Wave D — documents & reports** (spec 0039) — 5A-17a–d, 16.10

## 6. Reuse — build none of this

| Need | Use |
|---|---|
| Leave & payroll approval chains | `src/Hms.Kernel/Approvals/ApprovalEngine.cs` — new `Type` strings only |
| Payslip / letter / employee numbers | `src/Hms.Kernel/Numbering/NumberSeriesService.cs` |
| Every audited write | `src/Hms.Kernel/Audit/AuditWriter.cs` |
| Punch import as a job | `src/Hms.Kernel/Jobs/JobQueue.cs` |
| Payslips, letters, sheets | `src/Hms.Kernel/Printing/PdfRenderer.cs` + `_Letterhead.cshtml` |
| Money formatting, taka-in-words | `Ui.cs` → moves to `Hms.Shell` |
| Salary month / fiscal year | `src/Hms.Kernel/Time/FiscalCalendar.cs` |
| Night-shift punch → which day | `src/Hms.Kernel/Time/BusinessDayCalendar.cs` |
| Date entry (native `<input type=date>` is banned) | `<input hms-date />` + `FlexibleDate.cs` |
| User ↔ employee link | `AppUser.EmployeeRef` — reserved, still unwritten |
| Effective-dating pattern | `src/Modules/Admin/Hms.Admin/RateResolver.cs` |
| Posting to Accounts later | mirror `Hms.Billing.Contracts/IChargePoster.cs` |

**Promote to Kernel:** `BillingService.RoundHalfUp` and the integer-taka discipline — payroll needs the
same rounding rule and must not reference Billing.

## 7. Edge cases the build must absorb

**Payroll correctness** — mid-month join/exit/increment split-period computation (day-count convention
is itself a policy row); post-lock corrections become arrears lines in the next run, never edits;
negative net pay floors and carries the shortfall on the loan ledger; integer rounding residue
allocated deterministically to the largest earning component; unique `(branch_id, period)` plus
guarded state UPDATEs so two officers cannot double-run a month.

**Attendance & time** — night shifts pair punches by rostered shift window, not calendar day; punch
import idempotent on `(employee_id, device_id, punched_at)`; missing-out punches surface as exceptions,
never silent zeros; BD weekends are Fri–Sat or Fri-only per employer; Ramadan hours are an
effective-dated shift variant, not a code path.

**Confidentiality** — `hr.read` never reveals pay; `hr.salary.read` is separate from day one; payslip
PDFs served through an ownership-or-permission check.

**Self-service at scale** — bulk `AppUser` provisioning writing `EmployeeRef`; admin-driven password
reset (no email infrastructure offline); self-service pages get a responsive pass since staff will use
phones.

**Two-SKU lifecycle** — HRM→ERP upgrade boot test both directions; a `host_kind` guard row so two
different hosts cannot share one database; vendor private key stays off customer machines; shared
bootstrap extracted so two `Program.cs` files cannot drift.

**Resource ceiling** — no batch PDF generation; streaming punch-file parse with a size cap;
attendance-day derivation runs synchronously (hundreds of rows, sub-second) rather than introducing
this codebase's first background worker.

**Employment lifecycle** — rehire is a new record linked by person, never a resurrected one; employee
codes never reused; approver-equals-applicant routes up via approval delegation; whether leave
sandwiches holidays is a policy flag.

## 8. Guards and tests that must learn the new paths

`eng/check-{ui-tokens,no-external-hosts,no-native-date,fkeys}.sh` grep `src/Hms.Web` today — extend to
`src/Hms.Shell`, `src/Modules/Hr/Hms.Hr.Screens`, `src/Hms.Hr.Web`. `ViewGuardPermissionTests`,
`HandlerPermissionTests`, `check-lifecycle-traceability.sh` and `role-journeys.py` scan
`src/Hms.Web/Pages` — they would silently **pass** while covering zero HR pages. Re-point them and add
a meta-test that fails if a `.cshtml.cs` exists outside every scanned root.

Also fix while there: `src/Modules/Registration/Hms.Registration.csproj` project-references
`Hms.Billing` (implementation, not `.Contracts`) — hidden today only by Roslyn's unused-reference
elision. And `ModuleBoundaryTests.Modules[]` omits Emr, Ot and Radiology.

`.github/workflows/ci.yml` scripts only 3 of 14 contexts in its additive-migration gate — add `hr`.
</content>
