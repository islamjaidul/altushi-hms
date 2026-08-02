# 0039 — Remediation plan

**Audience:** the senior engineer taking this on. **Source of findings:**
[`docs/qa/full-audit-2026-08.md`](../../qa/full-audit-2026-08.md) ·
[PRD matrix](../../qa/full-audit-2026-08-prd-matrix.md) · probes in `eng/verify/audit/`.

Nothing here is speculative: every item closes a finding that was reproduced, and each work
package names the probe that proves it closed.

---

## How this is sequenced

Ordered by **where a patient's journey breaks first**, not by severity label. An operator locked
out at the registration desk stops the lifecycle before it begins, so the input tier leads; a
payroll that cannot pay staff is a Blocker but does not block a patient, so it comes second.

| WP | Package | Closes | Lifecycle stage protected |
|---|---|---|---|
| **1** | The input tier | 2 Blockers (VAL-04, VAL-05), 51 VAL defects | Every stage — all data entry |
| **2** | Schema can defend itself | VAL-02/06/08, ARCH-02/03/04, M16-10 | Every stage — all persistence |
| **3** | Payroll completes a cycle | 3 Blockers, 4 High (M16-01/03/04/05/06/07/08/09, AUZ-01) | Staff, not patients — but it is money |
| **4** | Reversal propagates | M11-01, M9-01, M10-01 | Refund, lab, radiology |
| **5** | Branch and identity | ARCH-01, LC-REG-16 | Admission, readmission, reporting |
| **6** | Platform | XCUT-02, VAL-01/07, M20-01, M1-01, PHI-01/02, XCUT-01 | Cross-cutting |

WP1 and WP2 are the load-bearing ones: between them they close **2 of the 5 Blockers and all 51
validation defects**, and they do it by removing the *class* of failure rather than its instances.
**Do not start WP3–WP6 before WP1 and WP2 land** — several later fixes depend on the constraints
WP2 adds, and fixing instances first means fixing them twice.

---

## WP1 — The input tier

**Root cause.** The model binder writes `default` into a bare `[BindProperty]` when parsing
fails, and **no handler in the tree inspects `ModelState`**. Where a handler happens to guard the
field afterwards (`Amount <= 0`, `Price < 0`) the outcome is correct; where it does not, `default`
is accepted as though the operator typed it. That single mechanic produced all 51 validation
defects.

There is no declarative layer to lean on: **0 DataAnnotations, 0 `asp-validation-for`, 1
`ModelState` use** across ~290 bound properties.

### 1.1 Fail closed on a binding failure — *the one change that matters most*

Make an unparseable value an error rather than a `default`. Two viable shapes; pick one and apply
it uniformly:

- **A base-class gate.** `HmsPageModel` gains a handler filter (`IPageFilter` /
  `IAsyncPageFilter`) that inspects `ModelState` before any handler runs and re-renders the page
  with a field-level message when it is invalid. One place, covers all 141 handlers, including the
  115 the audit never probed.
- **Nullable binding.** Bind money and quantity as `long?`/`int?` so a parse failure is `null`
  rather than `0`, then require the value explicitly. Safer semantically, but touches every page.

**Recommendation: the base-class gate**, because it is structural and cannot be forgotten on the
next screen. Add an architecture test asserting that every `PageModel` deriving from
`HmsPageModel` is covered by it.

> Watch for: `/pharmacy/pos` throws in the **view**, not the handler — `Model.Qtys[i]` indexes a
> `List<int>` that bound short. A handler-level gate must run before the view renders, or that
> page still 500s.

### 1.2 Bound every string

`HasMaxLength` appears **zero** times, so every string column is unbounded `text`. Set a length on
every bound string property and add the matching migration. Confirmed damage: two service names of
**100,000 characters**; a patient name of the same size that then locked the session (WP1.3).

Suggested bounds — agree them once and apply: names 200 · codes 40 · phone 20 · address 500 ·
notes/reasons 4,000 · free clinical text 10,000.

### 1.3 The registration lockout (**Blocker**, AUD-VAL-05)

A 100 KB name is interpolated into the success toast (`New.cshtml.cs:122`), which goes into
`TempData`. **No TempData provider is registered**, so ASP.NET's cookie provider applies: 37
cookies, 136,172 bytes, and Kestrel then answers HTTP **431 to every request from that session
including `/logout`**. The operator cannot recover from inside the product.

Three independent fixes; **do all three**, because each alone leaves a hole:
1. Bound the name (WP1.2) so the payload cannot get large.
2. Register a **session-backed** TempData provider so toast size cannot become header size.
3. Do not interpolate unbounded user input into a toast — truncate for display.

### 1.4 The dropped payment (**Blocker**, AUD-VAL-04)

`PaidNow = 100.55` binds to `0` on `/billing/opd`, `/pharmacy/pos` and `/diagnostics/order`; the
invoice saves and the success toast prints while **0 Tk is receipted**. WP1.1 fixes the binding;
additionally require that a save which claims a payment either produces a receipt or fails —
assert it in the handler, not only in the binder.

Copy the pattern from **`/billing/dues?handler=Collect`**, which refused all seven payloads
correctly. It is the reference implementation already in the codebase.

### 1.5 Whole-taka entry

The PRD specifies whole-taka entry, which guarantees operators will type decimals. Decide the
behaviour once — reject with a message, or round with a visible confirmation — and apply it to
every money input rather than per screen.

**Proof of completion:** `probe-validation.py` reports zero failed checks, and its corpus is
extended to cover handlers it did not sample.

---

## WP2 — Let the schema defend itself

**Finding.** The ERP has 12 `HasCheckConstraint`s and **every one is structural** —
`num_nonnulls(parent_a, parent_b) = 1` (×6), the invoice money identity
`net = gross - discount + tax + rounding_adj`, `ck_approval_state`, and
`scheduled_to > scheduled_from`. **None constrains a domain value.** The schema encodes relational
shape rigorously and domain values not at all.

That is the database-level twin of WP1: the application assumes the database validates, the
database assumes the application does, and neither does.

### 2.0 The pattern you need is already in this repository

Do not design this from scratch. **HR (specs 0034–0037, the newest module) has markedly stronger
schema discipline than the ERP modules built before it**, and it is the standard to raise the
others to. `src/Modules/Hr/Hms.Hr/Data/Migrations/20260731184553_InitHr.cs:1330-1375` loops over
every effective-dated policy table and adds, per table:

```sql
ALTER TABLE hr.{table} ADD CONSTRAINT ck_{table}_effective_order
  CHECK (effective_to IS NULL OR effective_to >= effective_from);

ALTER TABLE hr.{table} ADD CONSTRAINT ex_{table}_no_overlap
  EXCLUDE USING gist (
    {scope} WITH =,
    daterange(effective_from, COALESCE(effective_to, 'infinity'::date), '[]') WITH &&);
```

plus `ck_payroll_line_net` and `ck_payroll_line_non_negative` — the money identity applied to a
payroll line exactly as billing applies it to an invoice, with the comment *"arithmetic that
cannot be wrong is better than arithmetic that is checked."* That sentence is the brief for this
whole work package.

The ERP is **not** uniformly behind, and the plan should not pretend otherwise:
`adm.rate_version` carries `ex_rate_version_no_overlap` over
`(catalog_kind, catalog_id, scope, branch_id, daterange(valid_from, valid_to))`
(`InitAdm.cs:97`), so two overlapping prices are already impossible. What let
`valid_from = infinity` through is that `infinity` is a valid date overlapping nothing — the
missing guard is a **bound on the value**, not on the overlap. Read that constraint before
touching it; the work is to add value bounds alongside good structural ones, not to replace them.

### 2.1 Domain CHECK constraints

> **Sequencing warning — read before writing a migration.** SQLSTATE **`23514`** (check violation)
> is caught **nowhere** in `src/`; only `23505` (unique violation) is handled, in `Submission.cs:24`.
> Adding these constraints on their own converts today's *silent corruption* into tomorrow's
> *visible HTTP 500s*. **WP1's input gate and a `23514` handler must ship in the same change as the
> constraints**, not after them. Ship WP1 first, or ship them together — never the constraints
> alone.

Add value constraints where the audit proved a bad value reaches storage:

| Table.column | Constraint | Proven damage |
|---|---|---|
| `bill.charge_line.qty` | `> 0` and a sane ceiling | qty 999999 → **299,999,700 Tk** on a folio |
| `bill.receipt.tender` | `IN (…)` from `Tenders` | 12 receipts tendered `bitcoin` |
| `adm.rate_version.price` | `>= 0` | — |
| `adm.rate_version.valid_from` | a plausible calendar window, e.g. `BETWEEN '2000-01-01' AND '2100-01-01'` — **not** an overlap rule, which already exists | a price frozen at `infinity`, unreachable by any UI path |
| `emr.vitals.*` | SpO₂ 0–100, pulse 20–300, systolic/diastolic > 0 | SpO₂ 999, BP −1/−9 (30 rows) |
| `ipd.admission.service_charge_pct` | `0–100` | stored as −50 |
| `reg.patient.dob` | between 1900-01-01 and today | 28 patients at ±`infinity` |
| `pharm.stock_audit_line.counted_qty` | `>= 0` | batch counted 500 → 0 |

### 2.2 Constrain every state column

State columns are `text` and mostly unconstrained. `appt.appointment.state` accepted the arbitrary
string **`deleted`** — `AdvanceAsync` runs `SET state = {to}` with no check that `to` is legal, and
`to` is a **hidden form field**. Twelve rows now hold it.

Two layers, both needed: a CHECK restricting each state column to its legal set, **and** validation
of the target state in `AdvanceAsync` rather than trusting the form. `ck_approval_state` in the
kernel is the pattern to copy.

### 2.3 Intra-schema foreign keys

No foreign keys exist outside ASP.NET Identity's own. Six orphan classes are already in the
database — appointments naming a doctor that does not exist (12), indent lines naming a missing
product (12), purchase orders naming a missing supplier (9), diagnostics orders naming a missing
**referrer** (8, which is *who commission is owed to*), a prescription drug line, and six verified
lab reports whose **signature block names a consultant that does not exist**.

Add FKs **within** each schema (`invoice_line→invoice`, `payroll_line→payroll_run`,
`order_test→test_order`, and the six above where the parent is same-schema). Leave cross-module
references without FKs — that boundary is deliberate — and instead validate those at the service
edge, since a hospital cannot tolerate a lab report signed by nobody.

**Backfill first:** existing orphans must be reconciled or quarantined before a constraint can be
added. Under hard rule 4 they are not deleted — decide a disposition per class and record it.

### 2.4 Index where the traffic actually is (AUD-ARCH-04)

**Index distribution is close to inverse to traffic.** `bill.charge_line`, `bill.encounter`,
`bill.invoice_line` and the **entire `diag` schema** have no indexes beyond their primary keys —
and `charge_line` carries the hottest predicate in the product, since every folio view, invoice and
day-close reads it. HR, by contrast, declares 57.

Specifically:

- **`appt.appointment`** declares one index, `(DoctorId, OnDate, SerialNo)` unique. The queue
  board, the front desk's "today's doctors" panel and `/public/queue` all filter `OnDate == today`
  **across all doctors**, which that index cannot serve — each is a sequential scan, on a
  waiting-room display that reloads continuously against a table that grows forever. Add an index
  leading with `OnDate`.
- **`bill.charge_line`, `bill.invoice_line`, `bill.encounter`** and every `diag` table: add indexes
  on the columns their services actually filter by.
- **`appt.doctor_schedule.DoctorId`** is unindexed and is the column the `MAX + 1` identity mint
  scans (2.5).

Derive each from the module's real `.Where(...)` predicates rather than adding blindly.

### 2.5 Concurrency tokens, and the doctor master

**The existing token is inert (AUD-ARCH-02).** `Invoice.Version` is declared
`IsConcurrencyToken()` and **never assigned** — EF does not auto-increment a plain `int` the way it
does a `rowversion`, so both racers match on `WHERE Version = N` and both win. Fix this first: it is
the one place the code *claims* protection it does not have. Prefer PostgreSQL's `xmin` as the token
(HR already does this) so it cannot be forgotten again.

Then add a token to the aggregates a second operator can reach concurrently: `Admission`, `Folio`,
`TestOrder`, `Batch`, `StockAuditLine`, `Appointment`, `PayrollRun`.

Existing protection is real and good where present — **11 `FOR UPDATE` row locks and 37 correct
compare-and-swap sites** — but the state guard protects **transitions only, not field edits**, and
being convention rather than structure it is bypassed by the next write path someone adds.

**Doctor identity (AUD-ARCH-05, High).** There is no doctor master table; a doctor is a row in
`appt.doctor_schedule` and a new id is minted `MAX(DoctorId) + 1`
(`Pages/Admin/People.cshtml.cs:69`), which does not serialise under `READ COMMITTED`. Two
administrators adding a doctor concurrently produce **two clinicians sharing one identity**, and
`doctor_id` propagates to nine tables including prescriptions, OT teams and radiology reports.

This is also why the orphan `doctor_id` values in 2.3 cannot be fixed by a foreign key alone —
there is nothing to point at. **Create the doctor master, then add the key.**

### 2.6 Put hard rule 4's enforcement into force (AUD-ARCH-03)

The mechanism is already written and is good: `trg_receipt_immutable` makes a closed session's
receipt immutable, and `REVOKE DELETE ON ALL TABLES IN SCHEMA bill` /
`REVOKE UPDATE, DELETE ON kernel.audit_event` make financial deletion and audit mutation
impossible rather than merely forbidden (`InitBill.cs:265-291`).

**Nothing connects as the role it protects.** Dev connects as `postgres` (superuser — bypasses all
grants); both deployments connect as `hms_migrator`. `deploy/compose.yml:33-34` documents this as a
deliberate deferral pending "the cross-schema grant migration", open since spec 0005.

Two pieces of work: complete that grant migration so runtime traffic uses `hms_app`, and widen the
revocation beyond `bill` + `kernel.audit_event` to the other schemas holding financial or clinical
rows — `ipd`, `pharm`, `diag`, `lis`, `emr`, `radiology`, `ot`, `hr`.

Until then hard rule 4 rests on code convention alone. The convention **does** hold today —
`.Remove(`/`RemoveRange(` appear nowhere in billing, IPD, pharmacy or kernel — so this is
defence-in-depth, not an active breach. Worth adding a guard script asserting it stays that way.

### 2.7 Record the decision

There is no ADR saying the schema is FK-free by choice, and `docs/architecture/03-data-model.md`
argues the opposite instinct for money ("the CHECK constraint makes the identity structural").
Whatever is decided in 2.1–2.4, **write the ADR** so the next maintainer can tell design from
omission.

> Checked and discarded: `eng/check-fkeys.sh` sounds like a guard for this and is not — it
> validates reserved **function keys** (F2/F3/F9/F10).

**Proof of completion:** each constraint is negative-tested (attempt a violation, expect refusal),
per the precedent already set by HR's 8 exclusion + 12 check constraints.

---

## WP3 — Payroll completes a cycle

Three Blockers and four High findings, all in M16. The **state machine is sound** — spec 0037
proved that, and `hrm-thread.py` passes 37/37. What is broken is the arithmetic and the
configuration surface around it.

### 3.1 The journal does not balance (**Blocker**, AUD-M16-04)

`BuildJournalAsync` emits `debit = gross` but
`credit = deductions + (−shortfall) + (net − shortfall)`. A floored line has
`net = gross − deductions + shortfall`, so credit comes to `gross − shortfall` and the journal is
short by exactly the carried shortfall. `LockAsync` then refuses **the whole run**.

Observed: 43,172 Tk out on a 4.3 M Tk run; state stuck at `approved`; the operator is told
*"this is a bug, not a data-entry problem."* Nobody gets paid.

Fix the algebra so the carried shortfall appears once, not twice, and add a test that locks a run
containing an employee at the floor.

> Note the asymmetry before you change anything: the **payroll line itself is internally
> consistent** and constrained to be so — `ck_payroll_line_net` enforces
> `net = gross − deductions + shortfall` at the database. It is the **journal derived from those
> lines** that double-counts the shortfall. Fix `BuildJournalAsync`, not the line identity.

### 3.2 Six policy tables have no writer (**Blocker**, AUD-M16-01)

`DeductionRule`, `GraceTimeRule`, `OvertimeRule`, `PfPolicy`, `TaxSlab`, `GratuityRule` are read
by the engine and written by **nothing in `src/`** — no screen, no seed, no service.
`/hr/policies` binds two fields. The demo's own **posted** run pays employees with 4 absences and
360 OT minutes their full gross, deductions 0.

Build the configuration screens. Income tax and provident fund are statutory in Bangladesh, so
this is compliance exposure, not only arithmetic.

### 3.3 Attendance cannot be captured (**Blocker**, AUD-M16-08)

`AttendanceService.ImportAsync` and `DeriveDayAsync` have **zero callers**. Only `CorrectAsync` is
wired. Every attendance row was written by the demo seeder, so punch pairing, night-shift spanning,
break deduction and import idempotency **have never executed against product input**. Wire the
import path, then test those rules — they are unproven, not merely untested.

### 3.4 Salary cannot be set (**High**, AUD-M16-03)

`EmployeeService.SetPayAsync`'s only caller is `HrDemoSeed.cs:344`. The employee record renders pay
read-only with zero forms. An operator can hire and can never pay. Build the pay-structure screen;
`SetPayAsync` already effective-dates correctly and is tier-1 audited, so this is UI work over a
sound service.

### 3.5 Arithmetic defects

| Finding | Cause | Effect |
|---|---|---|
| AUD-M16-05 | `:198` exempts `PercentOf` from the "component not configured" skip | **6,000 Tk of phantom allowance** paid to an employee whose structure omits it |
| AUD-M16-07 | `:258` integer-divides the minute rate before the multiplier | 600 Tk paid where 850 Tk is due; **zero** below a 480 Tk day rate |
| AUD-M16-06 | `WorkingDaysAsync` takes `branchId` and never uses it | One branch's holiday reprorates every branch |
| AUD-M16-09 | `hr.payslip` has no writer and no screen | PRD `[M]` payslip absent; no PDF anywhere in the product |

### 3.6 Separation of duties (**High**, AUD-AUZ-01)

`/hr/payroll`'s page policy is `hr.payroll.run`; the approve handler needs `hr.payroll.approve`.
The Accounts Manager holds approve and **not** run, so the screen is refused outright — only the
superuser holds both. It fails safe, but maker-checker is *unusable*: the only account that can
approve a run is the one that generated it. Split the surface so an approver can reach the
approval without the ability to generate.

**Proof of completion:** `probe-payroll-math.py` and `probe-payroll-staged.py` report zero failed
checks, and the M16 row in `docs/qa/module-coverage.md` is rewritten with evidence.

---

## WP4 — Reversal propagates out of Billing

Billing's own reversal semantics are correct — the defect is that downstream modules never learn.

- **AUD-M11-01 (High).** `RestockAsync(pharm, branchId, invoiceId, actorId)` takes **no quantity**.
  The refund amount reaches `RefundAsync` and never reaches it, so refunding 100 Tk of a 5,000 Tk
  sale restocks **all** the medicine and marks every line fully refunded. The patient keeps the
  goods; the ledger says they came back; the allocations are now exhausted so the error is
  permanent. Pass the refunded quantity and restock proportionally.
- **AUD-M9-01 (Medium).** `RadiologyReporting.cs:48` filters `!t.Refunded`; `LabBoard.cs:61-63`
  filters only on order state. A refunded test stays on the lab worklist and can be collected,
  resulted and verified. Two modules read the same data with different rules — make the lab match
  radiology.
- **AUD-M10-01 (Medium).** `AmendAsync` never carries `Narrative`, so an amended radiology report
  prints an **empty findings body**; and the amend screen requires a template parameter, so a
  narrative-only report — every seeded imaging exam — cannot be amended at all. `ReportReady` SMS
  fires only on `/lis/verify`, so a radiologist signing on their own screen notifies nobody.

---

## WP5 — Branch and patient identity

### 5.1 Branch is a constant (**High**, AUD-ARCH-01)

`HmsPageModel.cs:17` — `public virtual long BranchId => 1;`, **never overridden**. Every write in
both SKUs stamps branch 1 regardless of who is signed in. Around it sits a full multi-branch
design: a `Branch` master, `BranchId` on **78 entities**, branch-scoped number series and rate
plans, and ADR-0007 specifying "`branch_id` (FK to a `branch` master)". Neither the FK nor any
enforcement exists — `HasQueryFilter` appears **zero** times.

Latent today because one branch exists. It stops being latent the day a customer opens a second:
every write still lands in branch 1 while queries that *do* filter return the wrong set.

Three parts: resolve `BranchId` from the signed-in user; enforce isolation with global query
filters rather than 78 entities' worth of remembering; add an architecture test that fails when a
query omits its branch predicate. PRD §5 M21 `[M]` "per-user counter/department binding" lands
naturally with the first part.

### 5.2 Patient identity repair (`[S]`, LC-REG-16)

Duplicate **detection** is well built — a soft gate listing candidates and requiring
`DuplicatesAcknowledged` before proceeding (`New.cshtml.cs:98-102`), exactly as PRD M1 specifies.
But `MergedInto` and `Active` are **read in five places and written in none**, so once a duplicate
exists the patient's history is permanently split across two UHIDs with no repair path.

For a product whose value is the longitudinal record, that is the identity gap that most limits
lifecycle robustness. PRD marks it `[S]`, so it is an absence rather than a defect — flagged here
because it bears directly on the goal.

---

## WP6 — Platform

- **No background worker (High, AUD-XCUT-02).** `JobQueue` is implemented and DI-registered in
  both hosts; nothing calls `Enqueue` and no `IHostedService` is registered. Unblocks approval
  escalation (`EscalationMinutes` is stored, snapshotted, and acted on by nothing), the M22
  end-of-day digest, due reminders and SMS delivery — **four `[M]` requirements from one fix.**
  *(Bed-charge accrual does **not** need this: it accrues lazily and correctly when the folio or
  front-desk estimate opens, resolving each date's own effective-dated rate.)*
- **SMS has no gateway (High, AUD-M20-01).** Messages are marked sent **only in simulation**;
  there is no `HttpClient` in `src/` and nothing drains the queue, so going live *stops* delivery.
  Only 3 of 9 `[M]` triggers are wired.
- **Unhandled exceptions (Medium, AUD-VAL-01/07).** 31 handlers reach a domain throw with no local
  catch and there is no global handler — six confirmed blank 500s, including a pharmacist pressing
  "Start count" twice. Add a global exception boundary that renders domain messages and a
  recoverable error page for the rest; keep the local catches for specific messages.
- **Public PHI (Medium, AUD-PHI-01).** `/public/report-status` parses the receipt's order number
  straight into a primary key; walking it anonymously returned **9 of 9 live orders** with a masked
  name **and each patient's test list**. Masking is correct; the payload is not. Give the lookup a
  non-sequential token, or require a second factor. *The severity depends on whether a test list
  counts as diagnostic information under §8 N5 — that is a PM call, not an engineering one.*
- **`/denied` bounce (Low, AUD-PHI-02).** Signing in from an anonymous denial returns the operator
  to the access-denied page. No loop; just wrong.
- **Connection ceilings (Medium, AUD-XCUT-01).** No `Maximum Pool Size` is configured, so Npgsql
  defaults to 100 per process against a deployed `max_connections=40` **shared by both SKUs**.
  Neither `40P01` nor `53300` is caught anywhere, so both reach the operator as a 500. Invisible
  locally because the dev database runs the default 100 — the constrained ceiling exists only where
  nobody load-tests. Belongs with ADR-0024.
- **Barcodes are decorative (High, AUD-M1-01).** `<div class="barcode"></div>` filled by a fixed
  CSS gradient, identical on every card, encoding nothing. Barcode printing, barcode search and
  scan-to-collect are all `[M]` and none can work from printed output. Needs a real symbology.

---

## Verification

The audit's probes are the acceptance instrument — they found these findings and must be the thing
that proves them closed.

```sh
# fresh database, both hosts from source
docker exec hms-dev-db psql -U postgres -d postgres \
  -c "DROP DATABASE IF EXISTS hms WITH (FORCE);" -c "CREATE DATABASE hms;"
cd src/Hms.Web   && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 dotnet run --no-launch-profile
cd src/Hms.Hr.Web && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5299 dotnet run --no-launch-profile

dotnet test hms-erp.slnx                                   # baseline 343 passing
python3 eng/verify/lifecycle-suite.py --tier all           # baseline green, 12/12 roles
python3 eng/verify/audit/probe-validation.py               # 51 defects -> 0
python3 eng/verify/audit/probe-authz-seams.py              # 1 -> 0
python3 eng/verify/audit/probe-public-phi.py               # 2 -> 0
BASE_URL=http://localhost:5299 python3 eng/verify/audit/probe-payroll-math.py     # 20 -> 0
BASE_URL=http://localhost:5299 python3 eng/verify/audit/probe-payroll-staged.py   # 3 -> 0
BASE_URL=http://localhost:5299 python3 eng/verify/hrm-thread.py                   # stays 37/37
```

Three standing rules for this work, each earned by a mistake spec 0038 made or nearly made:

1. **Assert the row, not the status code.** Every defect in this audit returned HTTP 200 with
   correct markup. Route smoke tests and permission probes pass straight over all of them.
2. **A green probe is only green if it can go red.** Negative-test each new constraint and guard by
   removing the rule and watching the test fail. Two of the audit's own checks passed for the wrong
   reason before this was applied.
3. **Reseed between runs whose baseline depends on a clean ledger.** The probes deliberately leave
   their evidence rows behind; hard rule 4 means nothing is deleted.

Then: run `hrm-thread.py` and the full suite twice consecutively on a database that has already
been used heavily — spec 0029's "three consecutive runs" bar is necessary and not sufficient.

## Follow-ups to route, not to build

- **P25** (doctor capacity, AUD-M3-01) is already with the PM. `MaxSerials` is configured,
  displayed as "Capacity", and compared against nothing.
- Whether a **test list on a public screen** is diagnostic information under §8 N5 (AUD-PHI-01).
- **ADR-0024** owns the concurrency and pool decision (AUD-XCUT-01, LC-XCUT-11, CONC-1's ABBA
  folio/stock lock ordering, which remains unfixed and uncaught).
