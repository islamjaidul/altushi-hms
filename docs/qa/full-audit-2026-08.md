# Full product QA audit — 2026-08-02

**Spec:** [0038-full-qa-audit](../specs/0038-full-qa-audit/spec.md) · **Target:** local, `main` @ `54a2fec`,
freshly seeded `hms` + `hrm` on `hms-dev-db` · **Both hosts booted from source** (ERP `:5199`,
HRM `:5299`) · **Nothing was fixed** — this document reports.

> **How to read a verdict.** This audit separates three things the previous documents blurred:
> a **defect** (the product computes or permits something wrong), a **gap** (the product may be
> right but nothing asserts it), and an **absence** (the requirement was never built). An absence
> is only a finding when the PRD marks it `[M]`. Severity is about consequence to a hospital, not
> about how hard the fix is.

| Severity | Means |
|---|---|
| **Blocker** | Money is wrong, a required business process cannot complete, or PHI leaks |
| **High** | Wrong money/state reachable by an ordinary operator, or an authorization bypass |
| **Medium** | Data-integrity or correctness risk with a workaround |
| **Low** | Weight, UX, or resilience under load |
| **Info** | Standard-of-care or `[S]`/`[C]` absence — product sequencing, not a bug |

---

## 1. Executive summary

**The ERP's money spine is in good shape. The HRM SKU's payroll engine is not shippable.**

Everything the existing machinery tests, passes: a full `--tier all` lifecycle run is green
(14 scripts, 12/12 roles, ward census restored), 343 unit/integration tests pass, and the
orphaned HRM operator thread passes 37/37. Independent reconciliation over the whole database
found **zero** arithmetic errors in the ERP's invoices, folios and receipts.

The findings are concentrated where nothing was ever asserted, and they fall into **two clusters
with two root causes** — which is the most useful thing in this report, because it means the
remediation is not fifty separate tickets.

**Cluster one — M16 payroll.** `docs/qa/module-coverage.md` already named it "the single largest
known risk in the product"; this audit confirms that and puts numbers on it. **Payroll cannot
complete a cycle**, and the parts of it that do run compute pay from an employee's salary
structure alone — every attendance-driven and statutory element (absence, overtime, late,
provident fund, income tax, gratuity) sits behind a policy table with **no write path anywhere in
`src/`**.

**Cluster two — input binding.** 123 malformed payloads across 26 POST handlers produced **51
confirmed defects**, and every one traces to a single mechanic: the model binder writes `default`
into a bare `[BindProperty]` when parsing fails, and **no handler in the tree inspects
`ModelState`**. Handlers that happen to guard the field afterwards behave correctly; the rest
accept `default` as though the operator had typed it. That is how a typed payment becomes a
0 Tk receipt, a typed discount becomes no discount, and a mistyped stock count writes a batch off
the shelf.

A third, smaller theme cuts across both: **absent infrastructure that looks like scattered
feature gaps** — no background worker (AUD-XCUT-02) and no foreign keys (AUD-M16-10).

### Top findings

| # | Sev | Module | Finding |
|---|---|---|---|
| AUD-M16-04 | **Blocker** | M16 | A run carrying any minimum-net-pay shortfall **can never be locked** — the journal is unbalanced by construction, so payroll dead-ends at `approved` |
| AUD-M16-01 | **Blocker** | M16 | Six payroll policy tables (absence, overtime, grace, PF, tax, gratuity) are read by the engine and **writable by nothing** — no screen, no seed, no service |
| AUD-M16-08 | **Blocker** | M16 | The attendance-capture pipeline is unreachable: `ImportAsync` and `DeriveDayAsync` have **zero callers** |
| AUD-VAL-04 | **Blocker** | M4/M8/M11 | A decimal payment (`100.55`) is **silently dropped on all three cash screens** — invoice saves, success toast prints, 0 Tk receipted |
| AUD-VAL-05 | **Blocker** | M1 | A 100 KB name registers the patient and then **locks the operator out of the whole app** — 136 KB of TempData cookie, HTTP 431 on every request including `/logout` |
| AUD-M16-05 | **High** | M16 | A percent-of earning is paid to employees whose pay structure omits it — **6,000 Tk of phantom HRA** on one staged line |
| AUD-M16-03 | **High** | M16 | An employee hired through the product **can never be given a salary** — `SetPayAsync`'s only caller is the demo seed |
| AUD-M16-07 | **High** | M16 | Overtime pays **600 Tk where 850 Tk is due** — the minute rate is integer-divided, and reaches zero below a 480 Tk day rate |
| AUD-M16-06 | **Medium** | M16 | `WorkingDaysAsync` accepts `branchId` and never uses it — one branch's holiday reprorates every branch |
| AUD-M4-01 | **Medium** | M4 | `CollectAsync` still stores an unvalidated `tender` string straight into day-close totals (M4-F3, open since spec 0032) |
| AUD-M3-01 | **Medium** | M3 | Doctor capacity (`MaxSerials`) is configured and displayed as "Capacity" and **compared against nothing** (M3-R1, PM as P25) |
| AUD-ARCH-01 | **High** | — | **Branch is the literal `1`**, never overridden, with zero query filters — a full multi-branch design (78 entities, `Branch` master, ADR-0007) resting on a constant |
| AUD-ARCH-03 | **High** | — | Hard rule 4's database enforcement (immutability trigger + `REVOKE DELETE`) protects role `hms_app`, which **nothing connects as** — dev uses `postgres`, deployments use `hms_migrator` |
| AUD-ARCH-05 | **High** | M3/M21 | **No doctor master table** — ids minted `MAX + 1`, so two concurrent additions merge two clinicians into one identity across nine tables |
| AUD-ARCH-02 | **High** | M4 | The product's **only concurrency token is inert** — `Invoice.Version` is declared and never assigned, so both racers match and both win |
| AUD-XCUT-02 | **High** | — | **No background worker exists** — nothing calls `JobQueue.Enqueue` and no `IHostedService` is registered, so approval escalation, the EOD digest, due reminders and SMS delivery are all unimplemented |
| AUD-M11-01 | **High** | M11 | A **partial** refund restocks **100%** of the pharmacy sale — `RestockAsync` takes an invoice id and no quantity |
| AUD-M20-01 | **High** | M20 | `HMS_SMS_MODE=live` **stops** SMS rather than sending it — no gateway, no drain; only simulation marks messages `Sent` |
| AUD-M1-01 | **High** | M1 | The printed "barcode" is a fixed CSS gradient encoding nothing — the `[M]` scan workflows cannot work from printed output |
| AUD-VAL-02 | **High** | M8 | Invalid item and referrer ids are accepted and stored — six orphan-reference classes, including who commission is owed to |
| AUD-VAL-06 | **High** | many | The same `default` substitution reaches a **299,999,700 Tk** folio charge, a stock batch written 500 → 0, an SpO₂ of 999, and `deleted` accepted as an appointment state |
| AUD-AUZ-01 | **High** | M16 | The §12 payroll approver **cannot open** `/hr/payroll` — only `admin` holds both grants, so maker-checker on payroll is unenforceable |
| AUD-PHI-01 | **Medium** | R3 | `/public/report-status` is **anonymously enumerable** — 9 of 9 live orders read back by walking order numbers, each listing that patient's tests |
| AUD-XCUT-01 | **Medium** | — | Npgsql's default 100-connection pool against a deployed `max_connections=40` shared by two SKUs; `40P01`/`53300` are caught nowhere (CONC-1/CONC-4) |

---

## 1A. The patient lifecycle, stage by stage

The audit was commissioned to answer whether the lifecycle is robust enough to run a hospital on.
This is that answer, walked in the order a patient experiences it. **Money and state hold at every
stage; what fails is the layer between an operator's keyboard and the database.**

| Stage | Holds | Breaks |
|---|---|---|
| **Register** | Duplicate detection is a proper soft gate — lists candidates, requires acknowledgement (`New.cshtml.cs:98`) | A 100 KB name **locks the operator out of the app entirely** (431, AUD-VAL-05). 28 patients carry ±infinity dates of birth. The printed barcode encodes nothing, so the ID card cannot be scanned |
| **Appointment** | Serial allocation survives cancellation (spec 0032 fix holds) | Any string is accepted as a state — **12 appointments sit in `deleted`**. Capacity is displayed and enforced nowhere. 12 appointments name a doctor that does not exist |
| **OPD bill** | Invoice arithmetic **exact across every invoice**; approval engine gates over-limit discounts | A decimal payment receipts **0 Tk** (AUD-VAL-04). A garbage discount silently becomes 0 and the bill saves at full price. Tender is an unvalidated free string |
| **Consult / EMR** | Prescriptions supersede rather than overwrite; a reader cannot correct one | **SpO₂ 999, pulse 32767, BP −1/−9 stored** — no range constraint anywhere. A drug line can name a product that does not exist |
| **Order tests** | Price snapshots onto the invoice line, so history reproduces | A decimal payment receipts 0 Tk. An invalid test id is silently dropped with no message. **8 orders name a referrer that does not exist** — that is who commission is owed to |
| **Lab** | Verify/amend retains both versions; e-signature carries a hash | **Non-numeric results stored with a blank abnormality flag** — 30 of them. A refunded test stays on the worklist. **6 verified reports are signed by a consultant that does not exist** |
| **Radiology** | Worklist correctly filters refunded tests — the lab does not | An amended report prints an **empty findings body**; a narrative-only report cannot be amended at all; signing notifies nobody |
| **Pharmacy** | FEFO batch pick, expiry block, quarantine all sound | A **partial refund restocks 100%** of the goods. A typo in a stock count wrote a batch **500 → 0**. Quantity −3 sells one unit. Pressing "Start count" twice returns a 500 |
| **Admit / IPD** | Bed-day accrual resolves **each date's own effective-dated rate**; transfers order their locks; terminal exits (death, abscond) handled | Quantity 999999 posted **299,999,700 Tk** to a folio. Service charge accepts −50%. An indent can name a product that does not exist |
| **OT** | Double-booking refused; consumables deduct stock | ABBA lock ordering against pharmacy stock remains unfixed and `40P01` is caught nowhere (CONC-1) |
| **Discharge** | **Folio settlement reconciles exactly** — all 11 locked folios carry a settlement invoice, no invoice over-receipted; bill-block gate works | — |
| **Readmission** | UHID is stable and hospital-wide | If a duplicate was ever created, **merge is never written** — the history stays split permanently |

Two conclusions worth stating plainly.

**The clinical stages are the least defended.** A blank abnormality flag on a lab result, an SpO₂
of 999, and a report signed by a non-existent consultant are not data-entry cosmetics — they are
what a doctor reads to make a decision. The money path has an identity constraint enforced in the
database; the clinical path has no value constraints at all.

**The core transaction spine is genuinely strong.** Every defect above is at the *edge* — input
binding, a missing constraint, a downstream module not learning about a reversal. Nothing was
found wrong in the double-entry logic, the settlement arithmetic, the state machines, or the
authorization model. That is the difference between a product that needs hardening and one that
needs rebuilding. This one needs hardening.

---

## 2. Module scoreboard

Fifteen of the 22 PRD §5 modules carry code. The seven without are unbuilt product sequenced in
`docs/architecture/11-build-plan-phase2.md` — recorded here as absence, not as gaps.

| # | Module | Routes | POSTs | `[M]` done | Audit verdict |
|---|---|---:|---:|---:|---|
| M1 | Patient Registration & ID | 3 | 1 | 20% | Duplicate detection real; **barcode decorative** (AUD-M1-01) |
| M2 | Front Desk / Help Desk | 1 | 0 | **75%** | Strongest conformance in the product |
| M3 | Appointment & Queue | 1 | 2 | 17% | **Capacity unenforced** (AUD-M3-01) |
| M4 | OPD & Emergency Billing | 8 | 9 | 67% | Arithmetic exact; **tender unvalidated** (AUD-M4-01) |
| M5 | Prescription & EMR | 7 | 13 | **71%** | Clean |
| M6 | IPD Management | 8 | 28 | 40% | Folio/settlement reconciles exactly |
| M7 | OT Management | 5 | 9 | **71%** | Clean |
| M8 | Investigation / Test Order | 3 | 5 | 25% | **Decimal payment collects 0 Tk; invalid ids stored** (AUD-VAL-04/02) |
| M9 | LIS | 5 | 6 | 29% | **Worklist not refund-aware** (AUD-M9-01); analyzer `[M]` absent |
| M10 | Radiology & Imaging | 4 | 5 | 40% | **Amendment loses findings** (AUD-M10-01) |
| M11 | Pharmacy | 9 | 27 | 56% | **Partial refund over-restocks** (AUD-M11-01) |
| M16 | **HR & Payroll** | 11 | 17 | 17% | **Not shippable** — see §3 |
| M20 | SMS / Notification | 1 | 0 | **0%** | **Live mode stops SMS** (AUD-M20-01) |
| M21 | Administration, Security & Audit | 11 | 19 | **0%** | Approval engine good; 5/8 consumers, escalation unenforced. **Catalogue accepts a 100 KB name and a bad price** (AUD-VAL-03) |
| M22 | Management Dashboards & MIS | 1 | 0 | **0%** | M22-D1 fix genuine; EOD digest needs a worker |
| — | R3 public displays | 2 | 0 | — | See §4 |
| M12–M15, M17–M19 | — | 0 | 0 | — | No code (sequencing, not a gap) |

The `0%` rows deserve care: M21's approval engine is one of the better-built things in the
product (threshold auto-approve, policy snapshot, state-guarded decide, audit on both sides). It
scores zero because every one of its five `[M]` bullets is missing one clause — usually
notification or escalation, both of which need AUD-XCUT-02's worker.

---

## 3. Findings ledger

### AUD-M16-04 — A payroll run carrying a shortfall can never be locked · **Blocker**

**Surface:** `src/Modules/Hr/Hms.Hr/PayrollService.cs:334-338` (the floor) and `:588-596`
(the journal), against `LockAsync` at `:456`.

`BuildJournalAsync` emits `debit = gross` but
`credit = deductions + (−shortfall) + (net − shortfall)`. Since a floored line has
`net = gross − deductions + shortfall`, the credit side comes to `gross − shortfall`. The journal
is therefore unbalanced by exactly the carried shortfall whenever **any** employee hits
`MinimumNetPayTaka`, and `LockAsync` refuses the entire run.

**Observed:** run for 01/12/2026, 101 employees.

```
Review          -> exceptions_reviewed
RequestApproval -> exceptions_reviewed
Approve         -> approved
Lock            -> approved   (unchanged)
  "The payroll journal does not balance (4311700 vs 4268528).
   Nothing was locked — this is a bug, not a data-entry problem."
```

The 43,172 Tk difference is exactly the sum of carried shortfalls. Payroll dead-ends at
`approved`: it cannot be locked, therefore never posted, therefore nobody is paid — and the
product tells a non-technical HR officer that it is "a bug". Note the state machine itself is
sound; spec 0037 proved that. It is the arithmetic behind it that stops.

**Repro:** `BASE_URL=http://localhost:5299 python3 eng/verify/audit/probe-payroll-staged.py`

---

### AUD-M16-01 — Every attendance- and statute-driven payroll rule is unconfigurable · **Blocker**

**Surface:** `PayrollService.cs:217-219, 226-266, 289-319`; `src/Modules/Hr/Hms.Hr.Screens/Pages/Hr/Policies.cshtml.cs:26-27`.

`PayrollService` reads six policy tables. None has a writer anywhere in `src/`:

```
$ grep -rn "DeductionRules.Add\|OvertimeRules.Add\|GraceTimeRules.Add\|\
PfPolicies.Add\|TaxSlabs.Add\|GratuityRules.Add" src/
(no matches)
```

`/hr/policies` binds exactly two fields — `DayCount` and `MinimumNetPay`. The consequence is
visible in the shipped demo's own **posted** run `PR-2026-27-0001`:

| Employee | Absent days | OT minutes | Lates | Gross | Deductions | Net |
|---|---:|---:|---:|---:|---:|---:|
| EMP-00025 | 4 | 0 | 3 | 19,000 | **0** | 19,000 |
| EMP-00037 | 3 | 150 | 2 | 65,500 | **0** | 65,500 |
| EMP-00001 | 2 | 360 | 6 | 26,500 | **0** | 26,500 |

The product recorded the absences, the overtime and the lateness, printed them on the payroll
line, and paid as though none had happened — then posted the run. Twelve lines in that run
recorded overtime; **zero** were paid for it. Income tax and provident fund likewise never
compute, which for a Bangladeshi payroll is a statutory exposure, not only an arithmetic one.

**Repro:** `BASE_URL=http://localhost:5299 python3 eng/verify/audit/probe-payroll-math.py`
(cases AUD-M16-01, AUD-M16-02)

---

### AUD-M16-08 — Attendance cannot be captured · **Blocker**

**Surface:** `src/Modules/Hr/Hms.Hr/AttendanceService.cs:39` (`ImportAsync`), `:114` (`DeriveDayAsync`).

Both have **zero callers** in `src/`. Only `CorrectAsync` is wired, to
`/hr/attendance?handler=Correct`. `hr.punch` and `hr.punch_import_batch` are empty; all 9,000
`attendance_day` rows were written directly by `HrDemoSeed`. So punch pairing, night-shift
spanning, break deduction, late/OT derivation and import idempotency have **never executed
against product input** — the review screen edits data the product has no way to acquire.

PRD §5 M16 `[M]` reads "Biometric attendance capture (live punch feed from devices) **+** admin
review/correction with reason". Only the second half exists.

---

### AUD-M16-05 — A percent-of component is paid to employees who do not have it · **High**

**Surface:** `PayrollService.cs:198`.

```csharp
if (!byId.TryGetValue(def.Id, out var configured) && def.CalcMethod != PayComponentCalc.PercentOf)
    continue;
```

The `PercentOf` exemption means a percent-of earning is **not** skipped for an employee whose
structure omits it; it is then computed off whatever base *is* configured. Staged: an employee
whose pay structure contains BASIC/MED/CONV and explicitly **no** HRA was paid **6,000 Tk of
HRA** (50% of a 12,000 Tk basic). This is money created from nothing, on every run, for every
employee not configured for a percent-of component. It is invisible in the shipped demo only
because the seed gives all 100 employees an identical component set.

---

### AUD-M16-03 — An employee hired in the product can never be paid · **High**

**Surface:** `src/Modules/Hr/Hms.Hr/EmployeeService.cs:118` (`SetPayAsync`).

Its only caller in the entire tree is `src/Hms.Hr.Web/HrDemoSeed.cs:344`. `/hr/employees/{id}`
renders pay **read-only** and has zero `<form>` elements and zero `OnPost` handlers. So an
operator can hire through `/hr/employees/new` and can never set that person's salary. Confirmed
live: the employee created by `hrm-thread.py` is 1 of 101 with no pay structure, and payroll
notes `"No pay structure is effective for this period — nothing was computed."`

> Caution for whoever fixes this: an earlier version of this check matched the string `/hr/pay`
> and **passed** on the sidebar's `/hr/payroll` link. Assert a POST form, not a substring.

---

### AUD-M16-07 — Overtime is paid at a truncated rate · **High**

**Surface:** `PayrollService.cs:258` — `var minuteRate = dayRate > 0 ? dayRate / (8 * 60) : 0;`

Integer division to whole taka **per minute**, before the multiplier. Staged: day rate 680 Tk →
`680/480 = 1` Tk/minute → 300 minutes at 2.0× paid **600 Tk against 850 Tk due**, a 29%
underpayment. Below a 480 Tk day rate — every salary under ~14,400 Tk/month on the Fixed30
convention, i.e. much of a Bangladeshi hospital's staff — the rate truncates to **zero** and
overtime is unpaid however many hours are worked.

---

### AUD-M16-06 — Proration ignores the branch it was given · **Medium**

**Surface:** `PayrollService.cs:616-623`.

```csharp
private static async Task<int> WorkingDaysAsync(
    HrDbContext hr, long branchId, DateOnly from, DateOnly to, CancellationToken ct)
{
    var holidays = await hr.Holidays.AsNoTracking()
        .Where(h => h.OnDate >= from && h.OnDate <= to)   // branchId never used
        .CountAsync(ct);
```

Under the `WorkingDays` convention, a holiday declared at any branch shrinks the denominator for
**every** branch, inflating the day rate and every day-rate-derived amount. Single-branch
deployments are unaffected today, which is why nothing has surfaced it.

---

### AUD-M16-09 — No payslip · **Medium** (PRD `[M]`)

`hr.payslip` is a table with no writer and no screen; one posted run produced zero payslips.
PRD §5 M16 `[M]` lists "pay slips" explicitly. There is no PDF generation anywhere in the
product — all printing is browser-print of HTML — so this is a genuine build gap, not a
rendering choice.

---

## 3E. Schema design

Added after the behavioural audit, because the defects above kept resolving to the same few
structural causes. Full dimension-by-dimension review:
**[full-audit-2026-08-schema.md](full-audit-2026-08-schema.md)**. The finding in one sentence:

> **The schema encodes relational *shape* rigorously and domain *values* not at all.**

All 12 ERP check constraints are structural — `num_nonnulls(parent_a, parent_b) = 1` (×6), the
invoice money identity `net = gross − discount + tax + rounding_adj`, `ck_approval_state`, and
`scheduled_to > scheduled_from`. **Not one constrains a value range.** Combined with §3D's root
cause — `ModelState` inspected nowhere — the product has *no validation tier at any layer*: the
application assumes the database validates, the database assumes the application does.

| Dimension | State |
|---|---|
| String length bounds | **0** `HasMaxLength` in the entire model — every string column is unbounded `text` |
| Foreign keys | **0** declared (`HasForeignKey`/`HasOne`/`WithMany`); ~6 in the database, all ASP.NET Identity's own |
| Domain value CHECKs | **0** — no bound on quantity, percentage, clinical measurement or date |
| State-column CHECKs | 1 (`ck_approval_state`); every other state column accepts any string |
| Concurrency tokens | **1** in the whole product (`Invoice.Version`) |
| Branch isolation | **0** `HasQueryFilter` across 78 branch-scoped entities |
| Effective-dating | **Good** — GiST exclusion constraints in `adm` and across all HR policy tables |
| Immutability | **Well designed, not in force** — see AUD-ARCH-03 |

### AUD-ARCH-01 — Branch is a hardcoded constant, and nothing enforces branch isolation · **High**

**Surface:** `src/Hms.Shell/HmsPageModel.cs:17`

```csharp
public virtual long BranchId => 1;
```

It is **never overridden** — `grep` for `override long BranchId` across `src/` returns nothing. So
every write in both SKUs stamps `branch_id = 1`, regardless of who is signed in or where they work.

Around that constant sits a full multi-branch design: a `Branch` master entity in the kernel,
`BranchId` on **78 entities**, and branch-scoped number series, counters and rate plans (ADR-0004).
ADR-0007 chose "single-tenant install + `branch_id` from day one" and specifies that every business
table carries "`branch_id` (FK to a `branch` master)". Two things differ from that decision in
practice:

1. **There is no such FK** — there are no foreign keys at all (AUD-M16-10).
2. **There is no enforcement of scoping.** `HasQueryFilter` appears **zero** times, so branch
   isolation rests entirely on each of ~78 entities' queries remembering `.Where(x => x.BranchId ==
   branchId)`. One omission is a cross-branch data leak, and no test can currently detect one
   because every row is in branch 1.

Today this is latent: one branch exists, so nothing misbehaves. It stops being latent the day a
customer opens a second branch — at which point every write still lands in branch 1 while queries
that *do* filter return the wrong set. This is the single largest gap between the architecture as
designed and the architecture as built.

Related and unbuilt: PRD §5 M21 `[M]` requires "per-user counter/department binding". With branch
a constant and no per-user binding, an operator cannot be tied to a counter or a department.

### AUD-ARCH-03 — Hard rule 4's database enforcement protects a role nobody connects as · **High**

**Surface:** `src/Modules/Billing/Hms.Billing/Data/Migrations/20260726125007_InitBill.cs:265-291`,
`deploy/db-init/01-roles.sh`, `deploy/compose.yml:33-35`.

The enforcement itself is **excellent** — better than most products of this size manage:

```sql
CREATE TRIGGER trg_receipt_immutable
  BEFORE UPDATE OR DELETE ON bill.receipt ...   -- a closed session's receipt cannot change
REVOKE DELETE ON ALL TABLES IN SCHEMA bill FROM hms_app;
REVOKE UPDATE, DELETE ON kernel.audit_event FROM hms_app;
```

A trigger making receipts immutable once their session closes, and grant-level revocation making
financial deletion and audit mutation impossible rather than merely forbidden. That is exactly how
hard rule 4 should be enforced.

**It is not in force in any environment.** The revocations target the role `hms_app`. Nothing
connects as `hms_app`:

| Environment | Connects as | Effect of the REVOKE |
|---|---|---|
| Local dev | `postgres` | none — superusers bypass all grants |
| Deployed (both SKUs) | `hms_migrator` | none — a different role, granted `ALL` |

To the project's credit this is **a known, documented deferral, not an oversight** —
`deploy/compose.yml:33-34` says so plainly: *"Runs as `hms_migrator` (owner of migrated objects)
for now; switching runtime traffic to least-privilege `hms_app` needs the cross-schema grant
migration — tracked in spec 0005 notes."* It has been open since spec 0005.

Two things follow. First, the belt exists but is not worn: hard rule 4 currently rests entirely on
code convention — which **does** hold, as `grep` for `.Remove(`/`RemoveRange(` across billing, IPD,
pharmacy and kernel returns nothing. Second, when the switch is finally made, the revocation's
scope is `bill` and `kernel.audit_event` **only**; `ipd`, `pharm`, `diag`, `lis`, `emr`,
`radiology`, `ot` and `hr` all hold financial or clinical rows and are not covered.

### AUD-ARCH-04 — The queue board has no index to use · **Medium**

`appt.appointment` declares exactly **one** index: `(DoctorId, OnDate, SerialNo)` unique. It serves
`Where(a => a.DoctorId == … && a.OnDate == …)` and `Where(a => a.DoctorId == …)` by prefix.

It does **not** serve `Where(a => a.OnDate == today)` — the query behind the queue board, the front
desk's "today's doctors" panel, and `/public/queue`. No index leads with `OnDate`, so each of those
is a sequential scan of the appointment table.

`/public/queue` is a waiting-room display that reloads continuously, and appointments accumulate
forever. At §14's volumetrics on a 2 vCPU / 3 GB box this is the clearest index gap on the
lifecycle path. Add an index leading with `OnDate`.

Index distribution is uneven more generally — HR declares 57, EMR 14, IPD 11, while Appointments,
Notifications and LIS declare 1, 1 and 2.

### AUD-ARCH-05 — There is no doctor master table, and doctor ids are minted `MAX + 1` · **High**

`class Doctor` and `DbSet<Doctor>` **do not exist**. A doctor is a row in `appt.doctor_schedule`,
and a new one's identity is minted at `src/Hms.Web/Pages/Admin/People.cshtml.cs:69`:

```csharp
var nextId = (await s.Appt.Schedules.MaxAsync(x => (long?)x.DoctorId) ?? 0) + 1;
```

`MAX + 1` inside a `READ COMMITTED` transaction does not serialise. Two administrators adding a
doctor concurrently both read the same maximum and produce **two different doctors sharing one
identity** — and `doctor_id` propagates to nine tables, including appointments, encounters,
prescriptions, OT teams and radiology reports. Merging two clinicians' patients under one id is a
clinical-safety problem, not a data-tidiness one.

This is also the root of the orphan `doctor_id` values found in §3D: with no master table there is
nothing for a foreign key to point at, so the reference cannot be validated even in principle.

### AUD-ARCH-02 — The one concurrency token in the product is inert · **High**

`IsConcurrencyToken` appears exactly once — `Invoice.Version` (`BillDbContext.cs:230`) — and
**nothing ever assigns or increments it.** A `grep` for `.Version` across billing and the web host
finds the declaration, a unique index, and LIS's unrelated result-version reads; no write.

EF Core does not auto-increment a plain `int` concurrency token the way it does a `rowversion`; the
application must. Because nothing does, two concurrent updates both read `Version = N`, both issue
`UPDATE … WHERE Version = N`, and **both match**. The token provides no protection at all — which
is worse than having none, because the model configuration reads as though the invoice is guarded.

Every other aggregate — `Admission`, `Folio`, `TestOrder`, `Batch`, `StockAuditLine`,
`Appointment`, `PayrollRun` — has no version column at all.

The services compensate in two ways, and where they do the protection is real and well built —
**11 `FOR UPDATE` row locks and 37 correct compare-and-swap sites**. But the state-guarded update
protects **transitions**, not field edits; the coverage is per-handler rather than structural; and
it is convention, so any second write path added later bypasses it silently. Two operators editing
the same admission or counting the same stock line are last-write-wins with no detection — which is
how AUD-VAL-06's stock line went 500 → 0 with nothing to object.

### AUD-M16-10 — The database declares almost no referential integrity · **Medium**

**Surface:** schema-wide.

| Database | Schemas | Tables | Foreign keys |
|---|---|---:|---:|
| `hms` | 15 | ~120 | **6**, all in `adm` |
| `hrm` | 3 | 63 | **6**, all in `adm` |

The `hr` schema carries **42 tables and zero foreign keys**. Nothing at the database level ties
a `payroll_line` to its `payroll_run`, or a `payroll_component_line` to its line. During this
audit a single `DELETE FROM hr.payroll_run` left **200 orphaned payroll lines**, silently
accepted.

Two honest qualifications. First, the orphans were created by *this audit's* SQL staging, not by
the product — the product has no delete path (hard rule 4), so the risk is latent rather than
live. Second, cross-module foreign keys would violate the modular-monolith boundary and their
absence is defensible; **intra-schema** ones have no such justification. What is missing is the
decision: no ADR and nothing in `docs/architecture/03-data-model.md` records that this schema is
FK-free by choice, so a future maintainer cannot tell design from omission. The data model doc
argues the opposite instinct for money, calling a CHECK constraint's identity "structural".

> Checked and discarded: `eng/check-fkeys.sh` sounds like a guard for this and is not — it
> validates reserved **function keys** (F2/F3/F9/F10), a keyboard concern.

---

### AUD-M4-01 — `CollectAsync` accepts an unvalidated tender · **Medium** (was M4-F3)

**Surface:** `src/Modules/Billing/Hms.Billing/BillingService.cs:371, 405`; consumed at
`DayCloseService.cs:46`.

`tender` is a bare `string`, stored verbatim. A `Tenders` constants class exists at
`Data/BillDbContext.cs:86` and is never used to validate. `DayCloseService` groups receipts by
the raw value, so an unexpected string silently creates a reconciliation bucket that matches no
drawer. Amounts *are* validated (zero, and against the due balance) — the gap is the tender
alone. Still open exactly as spec 0032 recorded it.

---

### AUD-M3-01 — Doctor capacity is displayed and enforced nowhere · **Medium** (was M3-R1)

`MaxSerials` is set at `/admin/people`, defaulted to 40, and rendered on `/appointments` as
"Capacity @d.MaxSerials". No code compares it to the issued count; `IssueSerialAsync` allocates
without reference to it. Serials can be issued past a doctor's stated capacity indefinitely.
US3.1's `[M]` acceptance criterion is unmet. Routed to the PM as **P25** under hard rule 2 —
recorded here as still unmet, not re-litigated.

---

### AUD-XCUT-01 — Connection-pool and deadlock exposure · **Medium** (CONC-1 + CONC-4)

No `Maximum Pool Size` is configured in any `appsettings*.json`, so Npgsql defaults to **100
connections per process**, while `deploy/compose.yml:67` and `compose.hrm.yml:73` both set
`max_connections=40` (≈37 usable) — **shared by both SKUs and the backup container**. The client
ceiling is roughly 5× the server's, so the pool can never apply backpressure: the caller past the
limit gets `53300 too many clients` rather than a short wait.

Neither `40P01` (deadlock) nor `53300` is caught anywhere — `grep -rn "40P01\|53300" src/` returns
nothing; only `23505` is handled, in `Submission.cs:24`. Both therefore surface as an unhandled
**500** to a non-technical operator. This is invisible locally because `hms-dev-db` runs the
PostgreSQL default `max_connections=100` — **the constrained ceiling exists only in the
environment nobody load-tests.** Not locally reproducible; reported as deployment risk.

---

## 3A. PRD conformance

Full per-requirement matrix: **[full-audit-2026-08-prd-matrix.md](full-audit-2026-08-prd-matrix.md)**
(96 `[M]` sub-features across the 15 built modules, each with route/file evidence).

| | Count |
|---|---:|
| `[M]` sub-features in built modules | 96 |
| **DONE** | 37 (39%) |
| **PARTIAL** | 54 |
| **ABSENT / UNENFORCED** | 5 |

Read `PARTIAL` carefully: it almost always means a working screen missing one named rule or
artifact, not a stub. There is no `TODO`, `FIXME` or `NotImplementedException` anywhere in
`src/` — the gaps are whole capabilities, not half-written code.

**Strongest:** M2 Front Desk 75%, M5 EMR 71%, M7 OT 71%, M4 Billing 67%.
**Weakest:** M20, M21, M22 at **0% fully DONE**; M3 and M16 at 17%.

### AUD-XCUT-02 — No background worker exists · **High**

`JobQueue` is fully implemented and DI-registered in **both** hosts, but nothing calls `Enqueue`
and no `IHostedService`/`BackgroundService` is registered anywhere:

```
$ grep -rn "\.Enqueue(" src/ | grep -v JobQueue.cs                → no matches
$ grep -rn "IHostedService\|BackgroundService\|AddHostedService" src/ → no matches
```

Every requirement phrased "automatically" or "daily" is therefore unimplemented, and they look
like unrelated gaps until you see the common cause: approval escalation
(`ApprovalPolicy.EscalationMinutes` is stored, snapshotted and acted on by nothing), the M22
end-of-day digest, due reminders, and SMS delivery. **One fix unblocks four `[M]` requirements.**

> **Correction.** An earlier draft of this finding also listed bed-charge accrual. That is wrong:
> `IpdBilling.PostBedDaysAsync` accrues **lazily and correctly** whenever the folio or the front
> desk estimate is opened, resolving the rate per individual date
> (`rates.ResolveAsync(..., date, ...)`) so each bed-day carries its own effective-dated price.
> That design needs no worker. Its one consequence, recorded as AUD-M6-01 below, is that accrual
> depends on somebody opening a screen.

### AUD-M6-01 — Bed charges accrue only when a screen is opened · **Low**

`PostBedDaysAsync` is called from `/ipd/folio` and `/frontdesk` only. For a long-stay patient
whose folio nobody opens, bed-days remain unposted, so revenue and occupancy reports understate
until someone visits the screen. Settlement is safe — discharge opens the folio — but MIS figures
can lag reality. Recorded as a design consequence, not a defect.

### AUD-M20-01 — `HMS_SMS_MODE=live` stops SMS rather than sending it · **High**

`SmsQueue.Queue` promotes a message to `Sent` **only when `Simulated` is true**
(`SmsQueue.cs:82-86`). There is no gateway — zero `HttpClient` in `src/` — and no worker to drain
the queue. So the switch intended to go live is the switch that silently stops delivery: messages
sit at `Queued` forever. Only 3 of the 9 `[M]` event triggers are wired at all.

### AUD-M1-01 — The printed barcode is decorative CSS · **High**

`/registration/{id}/card` renders `<div class="barcode"></div>` — an empty div filled by a fixed
`repeating-linear-gradient` (`src/Hms.Shell/wwwroot/css/app.css:845`). The pattern is **identical
on every card** and encodes nothing; the UHID appears separately as plain text beneath it. The
same applies to lab labels. PRD §5 M1 `[M]` "Barcode ID card printing" and "Patient search … by
barcode scan", and M9's scan-to-collect flow, cannot work end to end from printed output.

### AUD-M11-01 — A partial refund restocks the whole pharmacy sale · **High**

**Surface:** `src/Modules/Pharmacy/Hms.Pharmacy/StockService.cs:114-134`, called unconditionally
at `src/Hms.Web/Pages/Billing/Refund.cshtml.cs:204`.

```csharp
public async Task RestockAsync(
    PharmDbContext pharm, long branchId, long invoiceId, long actorId, …)
```

It takes an **invoice id and no quantity**. The refund `amount` is passed to `RefundAsync` and
never reaches `RestockAsync`, which then puts back `a.Qty - a.RefundedQty` for *every* allocation
on the invoice and sets `a.RefundedQty = a.Qty`. So refunding 100 Tk of a 5,000 Tk medicine sale
restocks **all** the medicine and marks every line fully refunded. The patient keeps the goods,
the ledger says they came back, and because the allocations are now exhausted a later refund
restocks nothing — the divergence is permanent. Money and inventory disagree after any partial
pharmacy refund.

### AUD-M9-01 — The lab worklist is not refund-aware; radiology is · **Medium**

`RadiologyReporting.cs:48` filters `&& !t.Refunded`. `LabBoard.cs:61-63` filters only on *order*
state (`InProgress`, `Reported`, `Delivered`) and never consults `order_test.Refunded`, so an
individual refunded test on a still-active order stays on the lab worklist and can be collected,
resulted and verified after the patient was refunded for it.

Two modules read the same data with different rules. Worth noting the earlier draft of this
finding claimed *both* worklists were unaware; radiology was checked and is correct. The defect
is the inconsistency, and the lab side of it.

### AUD-M10-01 — An amended radiology report loses its findings · **Medium**

`AmendAsync` never carries `Narrative`, so an amended radiology report's v2 has a NULL narrative
and prints an empty findings body. Separately the amend screen requires at least one *template
parameter* value, so a narrative-only report — every seeded imaging exam — cannot be amended at
all. And `ReportReady` SMS is enqueued only on `/lis/verify`, so a report signed by the
radiologist on their own screen notifies nobody.

---

## 3D. Input validation and error handling

Probe: `python3 eng/verify/audit/probe-validation.py` — **123 payloads across 26 POST handlers.
51 confirmed defects. 7 handlers clean.**

Every payload asserts (a) no 500 and no stack trace, (b) a readable error or a clean refusal, and
(c) nothing written — by `count(*)` over 25 tables either side of the POST. Where a row *is* the
correct answer, the case asserts on the **stored value** instead (the invoice's receipted total,
the vitals row's SpO₂, the rate version's `valid_from`), so a silent substitution of `default` is
still caught. Field names were discovered by parsing each page, not guessed.

> **Methodology note.** Other probes were writing to the same `hms` database throughout, so row
> deltas alone are not trustworthy here. Every defect below is backed by an **attributing query**
> against the specific row, not by a count difference.

### The single root cause

Every one of the 51 traces to the same mechanic:

> The model binder writes `default` into a bare `[BindProperty]` when parsing fails, and **no
> handler in the tree inspects `ModelState`.** Where a handler happens to guard the field
> afterwards (`Amount <= 0`, `Qty <= 0`, `Price < 0`) the outcome is correct; where it does not,
> `default` is accepted as though the operator had typed it.

That is why the results are bimodal rather than uniformly bad — and why the fix is one decision,
not fifty.

### The seven clean handlers

Named because they are the pattern to copy, not merely an absence of findings.

`/billing/dues?handler=Collect` is **the best-validated money handler in the product** — all seven
payloads (garbage, negative, overflow, decimal, empty, unknown invoice, script-tag tender) refused
with "Enter the amount being paid." and wrote nothing. Also clean: `/billing/refund` `Request` and
`Execute`, and the cart `Add`/`Remove` handlers on OPD billing, pharmacy POS and diagnostics,
which ignore bad ids and out-of-range indices without writing.

Clean behaviour *inside* otherwise-failing handlers is worth noting too:
`/admin/masters?handler=Reprice` correctly rejected `31/02/2026`, `2026-13-45`, `abc`, a past date
and a negative price; `/pharmacy/purchase?handler=Create` refused all seven bad quantity and cost
payloads; `/ipd/admit` refused every bad bed and patient id; `/pharmacy/pos` refused a quantity
larger than stock with a real message.

### AUD-VAL-04 — A decimal payment is silently dropped on all three cash screens · **Blocker**

`/billing/opd?handler=Save`, `/pharmacy/pos?handler=Save` and `/diagnostics/order?handler=Save`.
`PaidNow = 100.55` (or `abc`, or a 64-bit overflow) fails to bind to `long`, stays 0, and no
handler checks `ModelState`. **The invoice saves and the success toast prints.**

```
bill.invoice 198, 199, 200   net 300,  receipted 0
bill.invoice 204             net 40,   receipted 0
bill.invoice 207             net 1200, receipted 0
```

The patient hands over cash; the system records a due against them. This is every cash-collection
path in the product, and the PRD's whole-taka rule guarantees an operator will eventually type a
decimal. Raised from High to **Blocker** once it proved to affect all three screens rather than
one.

Contrast `/billing/dues?handler=Collect`, which handles the identical input **correctly** — all
seven payloads refused with "Enter the amount being paid." The gap is per-handler, which is
precisely what the missing declarative layer produces.

### AUD-VAL-05 — A long name registers the patient, then locks the operator out of the app · **Blocker**

`/registration/new`. The name input has no `maxlength`, `reg.patient.full_name` has no length
bound, and the success toast — which interpolates the name (`New.cshtml.cs:122`) — goes into
`TempData`. No TempData provider is registered in `Program.cs`, so ASP.NET's **cookie** provider
is in force. Reproduced independently:

```
register -> HTTP 431
  GET /registration/new  -> 431
  GET /                  -> 431
  GET /logout            -> 431   <-- cannot even sign out
  cookies: 37 totalling 136,172 bytes
```

The patient **is** created and a UHID issued; then Kestrel rejects every subsequent request from
that session, including the one that would clear it. The operator cannot recover from inside the
product — they must clear cookies in the browser. A 100 KB paste into a name field is an ordinary
clipboard accident for the §7 persona.

> Worth recording: my first attempt to reproduce this **failed** and I nearly reported the finding
> as unconfirmed — I had posted `AgeText` where the form uses `AgeOrDob`, so the registration
> never took the long name. The defect is real; my first probe was wrong.

### AUD-VAL-06 — Silent substitution reaches money, stock, state and clinical values · **High**

The same `default`-substitution, in eight more places. All verified by attributing query:

| Where | Payload | What was stored |
|---|---|---|
| `/billing/opd?handler=Save` | `DiscountFlat = abc / -1 / 45.50` | Discount **silently 0**; invoices 226–229 saved at full price with an "Invoice saved" toast |
| `/pharmacy/stock?handler=Count` | `CountedQty = abc` | `Math.Max(0, 0)` — audit line 20 went **500 → 0**, writing the whole batch off the shelf, no error |
| `/appointments?handler=Advance` | `to = deleted` | **Any string is a valid appointment state.** `AdvanceAsync` runs `SET state = {to}` with no check; **12 rows** now sit in state `deleted` |
| `/ipd/folio?handler=Service` | `Qty = 999999` | **299,999,700 Tk** posted to a patient's folio, no bound, no confirmation |
| `/ipd/folio?handler=Service` | `Qty = abc / 2.5` | Silently becomes 1 unit and charges 300 Tk |
| `/pharmacy/pos?handler=Save` | `Qtys = ['-3'] / ['0']` | `Math.Max(1, x)` — **sold one unit and deducted stock** |
| `/lis/results` | `abc` as a cholesterol value | Stored, with `"flag": ""` — no range comparison possible, so the abnormality flag prints blank. **30** such values |
| `/emr/vitals` | no range checks at all | `sp_o2 = 999`, `pulse = 32767`, `systolic = -1`, `diastolic = -9`. **30 rows** out of physiological range |
| `/ipd/admit` | `ServiceChargePct = -50` | Stored as −50 |
| `/ipd/folio?handler=Advance` | `AdvanceTender = bitcoin` | Receipt 28 for 100 Tk with tender `bitcoin`; **12 receipts** now carry a tender day-close cannot classify (widens AUD-M4-01) |

The clinical two deserve separate emphasis: a blank abnormality flag on a lab result and a
recorded SpO₂ of 999 are not data-entry cosmetics — they are the values a doctor reads.

### AUD-VAL-07 — Six uncaught exceptions return a blank HTTP 500 · **Medium**

Beyond `StartAudit` (AUD-VAL-01), each with its root cause identified:

| Route | Cause |
|---|---|
| `/billing/opd?handler=Save` | Service id not in the catalogue — the display cart filters unknown ids, the save loop iterates raw `Items` and calls `SingleAsync` |
| `/pharmacy/pos?handler=Save` | `Qtys=['abc']` — a `List<int>` that failed to bind is shorter than the cart, so **the view** throws rendering `Model.Qtys[i]` |
| `/pharmacy/stock?handler=Count` | `LineId = 99999999` → `SingleAsync` |
| `/emr/vitals` | `Temperature = 4000` → `(short)Math.Round(4000m*10)`; decimal→short conversion is always checked in C#, so `OverflowException` |
| `/emr/vitals` | `EncounterId = 99999999` → `FirstAsync` |

### AUD-VAL-08 — Impossible dates of birth are stored · **Low**

`/registration/new`: `0001-01-01` → `dob = -infinity` and `9999-12-31` → `dob = infinity`
(**28 patients** carry one or the other). `2026-13-45` is parsed as **age 2026 years**;
`31/02/2026` as age 31. Every downstream age calculation inherits it.

### AUD-VAL-02 — Invalid foreign ids are accepted and stored · **High**

Same handler. `Items = [valid, 99999999]` and `ReferrerId = 99999999` both return **HTTP 200
with no error** and write a full invoice + `test_order` + `order_test` set. Afterwards **3 test
orders name a referrer that does not exist**.

Nothing rejects the id and — per AUD-M16-10 — no foreign key catches it either, so the dangling
reference is permanent. **Six orphan classes** were confirmed by anti-join across the sweep:

| Route | Orphan reference | Rows |
|---|---|---:|
| `/appointments?handler=Issue` | `doctor_id 99999999` | 12 |
| `/ipd/folio?handler=Indent` | `product_id 99999999` | 12 |
| `/pharmacy/purchase?handler=Create` | non-existent `supplier_id` | 9 |
| `/diagnostics/order?handler=Save` | `referrer_id 99999999` | 8 |
| `/emr/consult?handler=Save` | drug line `product_id 99999999` | — |
| `/lis/verify` | `signature_image_ref = 'consultant:99999999'` | 6 |

Two are worse than dangling data. The referrer is **who commission is owed to**. And a verified
lab report whose signature block resolves to nobody is a clinical document attesting to a
consultant who does not exist.

### AUD-VAL-03 — Bad prices create a service with no price at all · **Medium**

**Exact origin:** `src/Hms.Web/Pages/Admin/Masters.cshtml.cs:146` guards the rate-version insert
with `if (Price > 0)`. An unparseable price binds to `0` (§3D's root cause), the guard is false,
and the service is created priceless and silently unsellable. The same line also means a genuine
zero-price service can never be catalogued.

`/admin/masters?handler=Create` with `Price` = `abc`, `250.75` or `9223372036854775808` writes
the `adm.service` row and **no `adm.rate_version`**. The result is a catalogue entry that exists,
is selectable, and has no price on any date.

Two more from the same handler:

- A **100,000-character** service name was accepted and stored (`adm.service` id 25 and 29). No
  length bound anywhere; that name then renders in every picker and on every printed invoice.
- A reprice with `EffectiveFrom = 9999-12-31` was accepted and stored as
  `valid_from = infinity` (`adm.rate_version` id 37). Under hard rule 5 an effective date is the
  mechanism that makes a historical invoice reproduce its historical price; `infinity` is a price
  that never takes effect, and every later reprice is then refused with "The current price starts
  on 31 Dec 9999 — the new one must start after that." **No UI path undoes it.**

  Worth being precise about why this got through, because the surrounding design is good:
  `adm.rate_version` **does** carry a proper exclusion constraint,
  `ex_rate_version_no_overlap` over `(catalog_kind, catalog_id, scope, branch_id,
  daterange(valid_from, valid_to))` (`InitAdm.cs:97`), so two overlapping prices are impossible.
  `infinity` slipped past it because it is a perfectly valid date that overlaps nothing. The
  missing guard is not the overlap rule — it is a **bound on the date value itself**, which is the
  same theme as every other finding in this section.

The date handling is otherwise **good** and worth preserving: `31/02/2026` and `2026-13-45` give
"Couldn't read that date — try 12/03/2026, 2026-03-12 or 12 Mar 2026", and a past date is refused
with "that would change invoices already issued".

### AUD-VAL-01 — An ordinary repeated action returns HTTP 500 · **Medium**

`/pharmacy/stock?handler=StartAudit` throws
`PharmacyException("An audit is already in progress for this outlet.")`
(`src/Hms.Web/Pages/Pharmacy/Stock.cshtml.cs:174`) — a message written to be *shown to a
pharmacist* — from a handler with **no `try`/`catch`**, while four sibling handlers on the same
page have one. There is no global exception handler either: `grep` for `UseExceptionHandler`,
`IExceptionFilter` and `UseStatusCodePages` across `Program.cs` and `Hms.Shell` returns nothing.

So the deliberate message becomes a crash page. Reproduced twice in a row:

```
attempt 1: HTTP 500  <-- unhandled
attempt 2: HTTP 500  <-- unhandled
```

The trigger is ordinary use: starting a stock count leaves the outlet in `count_started` until
the count is posted, so the next pharmacist to press "Start count" gets a 500. PRD §7 makes the
non-technical operator's experience a binding requirement.

**This is systemic, not isolated.** A scan of every ERP page model found **31 POST handlers that
reach a domain throw with no local catch**, including:

| Page | Handlers |
|---|---|
| `Pharmacy/Stock.cshtml.cs` | `StartAudit`, `Count`, `RequestAdjust`, `RequestWriteoff` |
| `Pharmacy/Products.cshtml.cs` | `Create`, `Company`, `Toggle` |
| `Pharmacy/Transfers.cshtml.cs` | `Outlet`, `Indent`, `Cancel` |
| `Admin/People.cshtml.cs` | `Doctor`, `Consultant`, `ToggleReferrer` |
| `Ipd/Admissions.cshtml.cs` | `RaiseBlock`, `RaiseRelease` |
| `Ipd/Folio.cshtml.cs` | `RaiseLatePost` |
| `Billing/Refund.cshtml.cs` | `Request` |
| `Radiology/Modalities.cshtml.cs`, `Ot/Theatres.cshtml.cs`, `Emr/*`, `Admin/Sms.cshtml.cs` | 12 more |

Only `StartAudit` was driven to a confirmed 500; the other 30 are the **same shape** and are
reported as a pattern to review, not as 30 confirmed defects.

---

## 3C. Authorization at the handler

Probe: `python3 eng/verify/audit/probe-authz-seams.py` — 16 cases, **38 route+handler pairs, 52
POST attempts, 8 users across both hosts. 52 of 52 refused, and the whole-schema fingerprint
(17 row counts + 5 state digests) was byte-identical after every one.** One finding, below.

This was the area expected to yield the most, and it yielded the least. Every page that fronts
money- or state-changing handlers behind a *read*-level `[Authorize]` policy was tested by
opening the page as a user who holds the read grant and lacks the write grant, lifting the
antiforgery token, and posting each named handler. **Every one refused, and in every case the
database was re-checked afterwards and had not moved** — the probe asserts the row, not the
status code.

Covered and clean: `/emr/prescription` `Correct` (the seam carrying an explicit code comment
about it) · `/ipd/folio` all **nine** handlers, against both a read-only consultant and a nurse
who may post services but not settle · `/ipd/discharge` all **six** · `/ipd/board` housekeeping ·
`/ipd/admissions` · `/radiology/worklist` `Done` · `/lis/board` · `/hr/payroll` all **seven** ·
`/hr/leave` `Recommend` vs `Decide` in both directions.

**HR salary masking works**, and was proven with a control rather than an absence: `farid`
(`hr.salary.read`) *does* see `137500`, `45000`, `90000` on `/hr/employees/20`, so the check can
detect a printed figure — and `nasrin` sees none of the five real amounts, and no taka symbol at
all.

### AUD-AUZ-01 — The payroll approver cannot reach the approval screen · **High**

`kamal` (Accounts Manager) holds `hr.payroll.approve` and `hr.salary.read` — the §12 matrix makes
them the payroll approver. But `/hr/payroll`'s page policy is `hr.payroll.run`, which they do not
hold, so **the screen is refused outright**. The probe enumerated the roles that can open it:
only `admin` holds both `hr.payroll.run` and `hr.payroll.approve`.

The consequence is a separation-of-duties failure with the sign inverted from the usual one:
nothing is bypassed — it **fails safe** — but the maker-checker control is **unusable**, because
the only account that can approve a payroll run is the same account that generated it. A
page-level grant is gating a handler-level permission. The Managing Director role is locked out
the same way.

Two incidental notes from the same probe:

- `/hr/payroll`'s `CanSeeMoney` branch is **dead code** — no role can open that page without
  `hr.salary.read`.
- `appt.appointment` id 3 carries `doctor_id = 99999999`, left behind by an earlier thread. It is
  invisible on the queue board and it made an early version of this probe pass by accident —
  worth a look as a data-integrity smell (see also AUD-M16-10 on the absence of foreign keys).

---

## 3B. Public and unauthenticated surfaces

Probe: `python3 eng/verify/audit/probe-public-phi.py` — 5 cases, **2 failed**.

### AUD-PHI-01 — `/public/report-status` is enumerable and lists each patient's tests · **Medium**

Walking order numbers `LB-00001…LB-00033` **anonymously**, with no prior knowledge, returned
**9 of the 9 live orders** — each with a masked name *and the tests ordered for that person*:

```
LB-00001 -> Rahim U.     :: Complete Blood Count, Random Blood Sugar
LB-00004 -> EMR 4.       :: Complete Blood Count, ECG 12-Lead
LB-00005 -> RAD 4.       :: X-ray Chest P/A
```

The masking is real and correct — it was verified against the true names for all nine. But the
**test list is not masked**, and which investigations a person underwent is clinical information.
The order-number format is short, sequential and printed on every money receipt, so the space is
trivially walkable. PRD §8 N5 / P15 allow a public screen to carry a serial and a masked name,
never a diagnosis.

**Cause:** `src/Hms.Web/Pages/Public/ReportStatus.cshtml.cs` strips `orderNo` to its digits and
parses it straight into `diag.test_order.id`. The "secret" printed on the receipt *is* the
zero-padded primary key. The class comment asserting that "probing order ids leaks nothing"
covers only *unknown* ids.

Mitigating, and worth the engineer knowing before choosing a fix: an unknown order number and a
malformed one return the **same** answer, so there is no id oracle and the end of the series
cannot be probed. The leak is the payload, not the lookup.

**Rated Medium, not High, because the name is masked** — re-identification needs someone who
already knows the patient. It escalates to **High if the PM reads a test list as diagnostic
information** under §8 N5, which forbids a diagnosis on a public screen. That is a scope call for
the PM under hard rule 2, not a QA call; flagging rather than deciding.

### AUD-PHI-02 — Signing in from a `/denied` bounce lands back on `/denied` · **Low**

`/denied` carries no `[AllowAnonymous]`, so an anonymous hit redirects to login with
`ReturnUrl=/denied`. It **does not loop** — it settles in 2 hops on a page that renders — but
after signing in, the operator is returned to the access-denied page rather than to a working
screen.

### What the public surface got right

- `/public/queue` prints the **masked** name (`Rahim U.`), never the full identity, and no phone,
  no UHID and no money.
- `/api/typeahead/patients` refuses anonymous callers (302 → login) and returns exactly
  `['label','value']` — no DOB, NID, address, area or guardian. Phone appears only *inside* the
  label the picker already renders, which §7 U5 requires to disambiguate same-name patients.
- `/health` on **both** hosts answers `{"status":"ok"}` and nothing else — no version header, no
  connection detail.

---

## 4. What held

Reported because a bullet-proofing audit that only lists faults misrepresents the product.

- **ERP money arithmetic is exact, and stayed exact under attack.** Across every invoice,
  `gross = Σ lines` and `net = gross − discount + tax + rounding` — **0 mismatches**, re-checked
  *after* the whole garbage-posting sweep. No invoice is over-receipted. All 11 locked folios
  carry a settlement invoice; the 2 open ones correctly do not. Where AUD-VAL found defects, the
  invoices created are still internally consistent — the problem is that some should not have
  been created, and one is missing its receipt.
- **Every one of 52 write POSTs behind a read-level page policy was refused**, with the database
  fingerprint identical afterwards. The handler-in-body pattern, the audit's main suspicion, is
  correctly implemented across 38 route+handler pairs.
- **`lifecycle-suite.py --tier all` is green** on a fresh database: 14 scripts, 0 failed,
  12/12 roles exercised, ward census 13/13 returned.
- **343 tests pass** (kernel 22, architecture 51, web 116, integration 153, print 1) — grown from
  the 306 recorded in `docs/qa/module-coverage.md`.
- **`hrm-thread.py` passes 37/37** on a fresh database, so spec 0037's flush fix holds.
- **M22-D1 is genuinely fixed** — `Dashboard.cshtml.cs` and the day-close statement now share one
  `InvoiceValue.Totalise` definition, so a reversed invoice cannot read as income.
- **M1 duplicate detection exists** (`RegistrationService.FindDuplicatesAsync`, surfaced on
  `/registration/new`) — `module-coverage.md` left this ambiguous.
- **Patient-facing documents render**: ID card, money receipt, patient statement and test slip
  all return 200 with content for real records.
- **`SmsQueueTests` now exists**, so `module-coverage.md`'s "no test referenced `SmsQueue`" is
  **stale**, not a live gap.
- **Effective-dating is properly constrained where it exists.** `adm.rate_version` carries
  `ex_rate_version_no_overlap`, a GiST exclusion constraint making two overlapping prices
  structurally impossible; HR does the same for **every** policy table plus
  `ck_{table}_effective_order`. Hard rule 5 is enforced by the schema, not by hope.
- **Bed-day accrual resolves each date's own rate** (`rates.ResolveAsync(..., date, ...)`), so a
  stay spanning a price change bills each night correctly.
- **The no-hard-delete mechanism is well designed** — a receipt-immutability trigger plus
  grant-level revocation of DELETE and of audit mutation. It is not currently in force
  (AUD-ARCH-03), but it is written and ready.
- **The code convention behind hard rule 4 holds**: `.Remove(`/`RemoveRange(` appear nowhere in
  billing, IPD, pharmacy or kernel.
- **HR's schema is the strongest in the product** and is the standard to raise the older modules
  to — exclusion constraints on every effective-dated table, a payroll-line money identity
  mirroring the invoice identity, and 57 declared indexes.

---

## 5. Drift and process

| Finding | Evidence |
|---|---|
| `_harness.CAST` holds **12** users; `DevSeed.Cast` seeds **14** | `farid` and `shirin` are structurally invisible to the "roles exercised 12/12" completeness gate, which therefore cannot fail on them |
| `hrm-thread.py` is an **orphan** | Absent from `lifecycle-suite.py`'s `TIERS` and from `check-lifecycle-traceability.sh`. The largest script in the tree, covering the riskiest module, is run by nothing automatically |
| `docs/qa/README.md` says "twelve mutating t1 scripts" | Actual: **10** |
| `.claude/skills/qa-lifecycle/SKILL.md` says "169 cases", "nine … threads" | Actual: **175** and **10** |
| `docs/qa/module-coverage.md` M16 row says "no e2e thread … whatsoever" | Superseded by spec 0037's `hrm-thread.py` (37 cases). The *arithmetic* claim stands and is now quantified above |
| `docs/specs/README.md` lists **0034** as `In Progress` | 0035–0037 all closed under it |
| `/hr/roster` is **692 KB** | 12× the next heaviest HRM page (`/hr/employees`, 56 KB); `/hr/payroll` is 11 KB. Capped and labelled, not redesigned. Material on a 2 vCPU / 3 GB box (§16) |
| `dotnet test` dirties the working tree | `eng/spike-artifacts/bangla-sample.pdf` is rewritten by `Hms.PrintGolden.Tests` on every run |

---

## 6. Baseline

| Measure | Result |
|---|---|
| `dotnet build hms-erp.slnx -c Debug` | clean, 0 warnings, 0 errors |
| `dotnet test hms-erp.slnx` | **343 passed**, 0 failed |
| `bash eng/check-lifecycle-traceability.sh --stats` | OK — 175 cases, 162 covered, 13 gaps |
| `python3 eng/verify/lifecycle-suite.py --tier all` | **GREEN** — 14 scripts, 12/12 roles, census unchanged |
| `BASE_URL=…:5299 python3 eng/verify/hrm-thread.py` | 37 cases, 0 failed |
| `probe-payroll-math.py` | 10 cases, **20 failed checks** |
| `probe-payroll-staged.py` | 4 cases, **3 failed checks** |
| `probe-authz-seams.py` | 16 cases, 52/52 POSTs refused, **1 failed check** |
| `probe-public-phi.py` | 5 cases, **2 failed checks** |
| `probe-validation.py` | 26 handlers, 123 payloads, **51 confirmed defects**, 7 handlers clean |

`probe-payroll-math.py`'s exact failure count moves by one or two with database state (whether
`hrm-thread.py` has run, which posted runs exist). The **per-case verdicts** do not. Cite the
cases, not the total. `probe-payroll-staged.py` rewrites one employee's pay structure and
restores it on the way out; if it is interrupted, reseed `hrm` before trusting the other probe.

---

## 7. What was NOT tested

Stated plainly so the next reader does not mistake this document for completeness — the mistake
`patient-lifecycle.md`'s "93% covered" invited, and which that document itself warns about.

- **Load, concurrency under contention, and the connection-pool ceiling.** LC-XCUT-11 remains
  open pending ADR-0024. AUD-XCUT-01 is a configuration reading, **not** a reproduction.
- **Browser matrix and accessibility.** Not attempted.
- **Penetration testing** beyond the named public surfaces.
- **The seven unbuilt modules** (M12–M15, M17–M19) — out of scope by definition.
- **Payroll arithmetic beyond the enumerated cases.** Tax-slab boundaries, PF eligibility edges,
  gratuity and loan installments could not be reached at all: their policy tables have no writer
  (AUD-M16-01), so there is nothing to compute against. Those code paths remain **unexercised**,
  not proven correct.
- **The live deployments.** This audit ran entirely against local hosts on current `main`. The
  VM's ERP image predates 2026-07-29 and was deliberately not touched (hard rule 4).
- **Validation was sampled, not exhaustive** — 26 of the 141 POST handlers, money and PHI first.
  The remaining 115 were not probed, and given that 19 of the 26 probed handlers carried at least
  one defect, **the reasonable prior is that more exist there.** Of the 31 handlers that reach a
  domain throw with no local catch, six were driven to a confirmed 500; the rest are a pattern.
- **Print fidelity** was checked only far enough to establish that the barcode element is a CSS
  gradient (AUD-M1-01). Layout, page breaks and Bangla rendering were not assessed.
- **`dmetaphone`/`pg_trgm`**: the LIS duplicate-detection path depends on extensions created via
  `CREATE EXTENSION IF NOT EXISTS`. The SQL was read; the live extension state was not verified.

### Left behind in the local database

The probes are find-and-report instruments and several write real rows; under hard rule 4 nothing
was deleted. On the local `hms` database this audit leaves roughly 40 probe invoices with
unreceipted balances, ~25 probe patients (two per run with ±infinity dates of birth and one per
run with a 100 KB name), 12 orphan-doctor appointments, 12 appointments in state `deleted`,
12 orphan indent lines, 9 orphan-supplier purchase orders, 30 impossible vitals rows, 30
non-numeric lab values, 12 receipts tendered in `bitcoin`, `adm.rate_version` 37 permanently
freezing one test's price, and `pharm.stock_audit_line` 20 counted down to 0.

**This is the evidence for §3D, not contamination to clean up.** Reseed before any run whose
baseline depends on a clean ledger:

```sh
docker exec hms-dev-db psql -U postgres -d postgres \
  -c "DROP DATABASE IF EXISTS hms WITH (FORCE);" -c "CREATE DATABASE hms;"
```
