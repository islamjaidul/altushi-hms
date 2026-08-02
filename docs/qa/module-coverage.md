# Module coverage — the QA sweep (spec 0032)

`patient-lifecycle.md` walks a **patient's journey** and is the right instrument for finding
defects *between* modules. This document walks the **module list** and asks a different question:
for this module's own rules, is each one asserted by something that would fail if the rule were
removed?

Both are needed. A rule no journey happens to traverse is invisible to the journey document.

## Verdict legend

| Axis | Asks |
|---|---|
| **UI smoke** | Does every route of the module load, for the roles that own it, and refuse the rest? |
| **e2e** | Does the operator's real path through the module work over HTTP, end to end? |
| **Business logic** | Is every enforced rule — money, state machine, permission, invariant — asserted? |

`OK` = covered · `THIN` = partially covered, gaps listed · `GAP` = nothing asserts it

## The 22 modules of PRD §5

| # | Module | Domain | Code? | Routes | UI | e2e | Business logic | Defects found |
|---|---|---|---|---|---|---|---|---|
| M1 | Patient Registration & ID | A | **yes** | 3 | OK | OK | THIN | **2 High** |
| M2 | Front Desk / Help Desk | A | **yes** | 1 | THIN | OK | THIN | 1 Medium + a missing safety interlock |
| M3 | Appointment & Queue | A | **yes** | 1 | OK | THIN | **GAP** | **2 High** + unmet [M] AC |
| M4 | OPD & Emergency Billing | A | **yes** | 8 | OK | OK | THIN | **4 High**, 3 Medium |
| M5 | Prescription & EMR | A | **yes** | 7 | OK | OK | **OK** | none |
| M6 | IPD Management | B | **yes** | 8 | OK | OK | OK | none |
| M7 | OT Management | B | **yes** | 5 | OK | OK | OK | none |
| M8 | Investigation / Test Order | C | **yes** | 3 | OK | OK | OK | none |
| M9 | LIS | C | **yes** | 5 | OK | OK | OK | none |
| M10 | Radiology & Imaging | C | **yes** | 4 | OK | OK | OK | none |
| M11 | Pharmacy | D | **yes** | 9 | OK | OK | OK | none |
| M12 | Inventory (store / reagent / film) | D | no | — | out of scope — no code |
| M13 | Blood Bank | D | no | — | out of scope — no code |
| M14 | Canteen | D | no | — | out of scope — no code |
| M15 | Accounts & Finance | E | no | — | out of scope — no code |
| M16 | HR & Payroll | E | **yes** | 12 | OK | OK — `hrm-thread.py` 38/38 + `probe-payroll-math.py` / `probe-payroll-staged.py` 0 failed | OK — `HrPayrollTests` + `HrAttendanceTests` (13 tests) + `TransactionDurabilityTests` | 3 Blockers + 4 High fixed in 0039 WP3; see below |
| M17 | Consultant Payment | E | no | — | out of scope — no code |
| M18 | Corporate / Panel Billing | E | no | — | out of scope — no code |
| M19 | Marketing & Referral | E | no | — | out of scope — no code |
| M20 | SMS / Notification | F | **yes** | 1 | OK | **GAP** | THIN — `SmsQueueTests` (8 tests) asserts G19 atomicity, skip-no-phone, resend, segments | 1 Medium |
| M21 | Administration, Security & Audit | F | **yes** | 8 | OK | OK | **OK** | none |
| M22 | Management Dashboards & MIS | F | **yes** | 1 | OK | THIN | **GAP** | **1 High** |

Plus **R3 public displays** (`/public/queue`, `/public/report-status`) — a §5A.2 sub-module, swept
with M9/M3.

**Fifteen modules carry code.** The seven with none are not gaps in QA; they are unbuilt product,
sequenced in `docs/architecture/11-build-plan-phase2.md`.

**M16 HR & Payroll was added after this sweep** (specs 0034/0035) and has *not* been swept. Its
row records what is honestly known: eight routes load for the roles that own them, and there is
**no e2e thread and no business-logic test whatsoever** — no `hr-thread.py`, and not one test
covering payroll arithmetic (split-period proration, rounding-residue allocation, post-lock
arrears, night-shift punch pairing, punch-import idempotency, negative-net floor). That is money
code under hard rules 4 and 5 with zero automated coverage, and it is the single largest known
risk in the product. Database-level invariants *are* proven (8 exclusion + 12 check constraints,
each verified by attempting a violation), which is a floor, not a substitute.

M16 also ships as a **standalone SKU** (ADR-0025), deployed at `hrm.specshipper.com`. Wave A is a
working spine rather than a finished product — no payslip PDF, no punch-file upload screen, no
employee↔user linking. Tracked in `docs/specs/0035-hrm-platform/notes.md`.

**Spec 0036 closed the operability gaps** the row above could not see. Four defects that every check
here passed over — a superuser seeded with 2 of 11 permissions, `/admin/users` shipped only in the ERP
host, three stylesheet classes used by seven pages and defined nowhere, and ten icon ligatures absent
from the vendored font — plus org-master CRUD, the employee record page, and a deterministic
100-person demo seed. Two CI guards were added for the classes of defect that return HTTP 200 with
correct markup (`check-css-classes.sh`, `check-icon-glyphs.sh`), each proven to fail on the pre-fix
tree.

That work also found a defect in code this table calls covered: `EmployeeService` issued employee
codes against a fiscal-year-scoped series with no `{fy}` in the format, so **the first hire after any
fiscal-year rollover would have thrown a 500**. `NumberSeriesScopeTests` now asserts the rule across
all fourteen modules. Nothing in the eight route checks could have seen it, because it needs two
fiscal years of data to appear.

**Spec 0037 closed the state machine and durability** (`hrm-thread.py`, 37/37 — every screen driven
the way an operator drives it, every write proven persisted), and **spec 0038's audit then attacked
the arithmetic and its configuration surface** and found the M16 findings ledger of
`full-audit-2026-08.md`: three Blockers (AUD-M16-01 unconfigurable rules, AUD-M16-04 unlockable
floored runs, AUD-M16-08 uncapturable attendance) and four High/Medium arithmetic defects.

**Spec 0039 WP3 closed all of them, with evidence** (row superseded 2026-08-03; "the single largest
known risk in the product" above is history, kept for the record):

- **e2e**: `eng/verify/audit/probe-payroll-math.py` (10 cases, 0 failed — was 20 failed checks) and
  `probe-payroll-staged.py` (4 cases, 0 failed — drives Generate → Review → Approve → Lock over HTTP
  with a floored employee, a percent-of omission and a truncation-prone salary staged);
  `hrm-thread.py` grew to 38/38: HRM-EMP-08 now pays the employee it hires through the 0039
  set-pay screen, so runs no longer accumulate unpayable people and the two instruments can run
  in any order (the math probe's AUD-M16-03 checks that every unpaid employee's record offers
  the set-pay form — the invariant its message always claimed).
- **Business logic**: `tests/Hms.Integration.Tests/HrPayrollTests.cs` — floored run locks *and
  posts* with a balanced journal (shortfall debited once as a recoverable advance), payslips issued
  on post (`PS-` numbered), percent-of paid only where configured, overtime multiplied before
  divided (484 Tk where the old code paid 0), holiday proration branch-scoped, policy writes
  effective-dated with same-day amend, tax band sets replaced whole and validated.
  `HrAttendanceTests.cs` — punch pairing, break deduction, late/OT derivation, night-shift
  spanning across midnight (a 06:00 out-punch lands on the shift's own date, and a morning-only file re-derives
  the previous night via the roster), import idempotency, and honest rejection counts. Every
  arithmetic fix was watched to fail against the pre-fix code.
- **Configuration surface**: `/hr/policies` now writes all six rule tables through
  `PolicyResolver.Set*` (tier-1 audited, effective-dated, GiST-guarded); `/hr/attendance` imports
  punch files or pasted rows through `CsvPunchSource` → `ImportAsync` → per-day derivation; the
  employee record sets pay through `SetPayAsync`; payslips print at `/hr/payslip/{id}`; and
  `/hr/payroll/approvals` gives the §12 approver (Accounts Manager / MD, `hr.payroll.approve`
  without `hr.payroll.run`) a door of their own, so maker-checker is usable (AUD-AUZ-01).
- **Schema**: `HrPayrollIntegrity` migration — intra-schema FKs (`payroll_line→payroll_run`,
  `payroll_component_line→payroll_line/run`, `payslip→payroll_line/employee`,
  `punch→punch_import_batch`), all `ON DELETE RESTRICT` (AUD-M16-10).

Still open for M16, deliberately: Wave C money ledgers (loans, PF/welfare ledger postings), the
OT bank, gratuity *computation* at separation (the rule table now has a write path; the engine
consumes it only via the policy stamp), server-rendered PDF (payslips are browser-print HTML like
every other document), and employee↔user self-service linking.

---

## Findings

### QA-H1 — `--tier all` is red by construction *(harness, Medium)*

`eng/verify/lifecycle-suite.py` runs tiers in the order `t0 → t1 → t2`. Tier t2
(`golden-thread.py`) asserts **absolute** money totals — `golden-thread.py:206` requires the MD
dashboard to read exactly ৳550 — and therefore requires a fresh ledger. The twelve mutating t1
scripts run first and dirty it, so the dashboard reads far more than ৳550 by the time t2 asks.

`docs/qa/README.md` documents `--tier all` as a supported run that needs only a fresh database.
That precondition cannot be met, because the runner destroys it itself.

Proven locally on 2026-07-28:

| Run | Result |
|---|---|
| `--tier all` on a freshly seeded database | **RED** — `golden-thread.py` ✗ *income lands on the dashboard* |
| `--tier t2` alone on a freshly seeded database | **GREEN** |
| `--tier t0` + `--tier t1` on the same database | **GREEN** — 12 scripts |

The failure says nothing about the build. A permanently-red "full" suite is worse than no full
suite: it teaches the team that red is normal, which is exactly how a real regression gets
waved through.

**Fix:** when the tier is `all`, run t2 immediately after t0 and before t1, so t2 meets the fresh
ledger it documents. t2's internal order (`golden-thread` before `discount-and-dues`) must hold —
the latter bills the patient the former registers.

> **FIXED and verified 2026-07-28.** `lifecycle-suite.py` gained `ALL_ORDER = ["t0", "t2", "t1"]`;
> single-tier runs are unchanged. `docs/qa/README.md` now documents the order and why.
> Re-run on a freshly seeded database: **SUITE GREEN — 14 scripts, 0 failed, roles exercised
> 12/12, ward census unchanged.**

---

## M1 — Patient Registration & ID

| Axis | Verdict |
|---|---|
| UI smoke | **OK** — all three routes in `ROUTES`, plus a keyboard-only completion test (`ux-principles.spec.ts` U4) |
| e2e | **OK** — `golden-thread` 1, `lifecycle-thread` 1 (name, digits, tail, duplicate guard) |
| Business logic | **THIN** — one defect, four unasserted rules |

### M1-D1 — a patient aged in months cannot be registered *(High, defect)*

`POST /registration/new` with `AgeOrDob` = `8 months`, `8 mo` or `8m` is refused with *"Either DOB
or age is required unless unknown-identity (edge 25/26)."* The same form accepts `45`, `45y` and
`12/03/1980`. Verified against localhost as `jashim`, all six forms:

```
FAIL  months-word    '8 months'      PASS  plain-years   '45'
FAIL  months-abbrev  '8 mo'          PASS  years-suffix  '45y'
FAIL  months-m       '8m'            PASS  dob-slash     '12/03/1980'
```

`ParseAge` supports months by design (§7 U13) and returns them; `OnPostAsync` then passes
`dob: null, years: null` to `RegisterAsync`, which throws. The line that would have stored the
value — `p.AgeMonths = months` — is unreachable, so `Patient.AgeMonths` is dead code. The
database agrees with the service: `ck_identity` does not mention `age_months`.

**Why it matters.** No infant under one year can be registered by age. The operator's workarounds
are a false DOB or rounding to "1 year", which corrupts the record and any age-banded lab
reference range.

> **FIXED and verified 2026-07-28.** `AgeMonths` now travels on `RegisterPatientCommand` into a
> widened `ck_identity` (migration `WidenIdentityCheckForAgeMonths`); blood group and patient type
> moved into the same call, removing two extra `SaveChangesAsync` round-trips. Re-running the
> original repro: **all six forms PASS**, and the rows read
> `age_months=8, age_years=NULL, age_as_of=2026-07-28` for the three month forms, with the DOB row
> carrying neither age column nor an `age_as_of`. New case **LC-REG-18** records it.

### M1 test gaps

| ID | Gap | Severity |
|---|---|---|
| M1-G1 | `ParseAge` — a pure function with six documented §7 U13 input shapes — has no unit test anywhere | Medium |
| M1-G2 | `NormalizePhone` untested; LC-REG-05 (`+880`, spaces, dashes) works in the product but nothing asserts it — probed live today, all seven variants hit | Medium |
| M1-G3 | "DOB wins over age" (`AgeYears` nulled, `AgeAsOf` stamped only for age-only) is unasserted — LC-REG-11 | Low |
| M1-G4 | Guardian, blood group and patient type are never asserted to persist — LC-REG-12/14 | Low |

> **All four CLOSED and verified 2026-07-28.** New project `tests/Hms.Web.Tests` (48 facts) covers
> `ParseAge` across §7 U13's shapes plus the ways an operator gives nothing, day-first date
> reading, and the estimated/not-estimated distinction; and `NormalizePhone` across thirteen
> forms. The engineer also found a **second** normaliser I had missed —
> `PatientSearch.PhoneDigitsOf`, which decides what a *search* matches while `NormalizePhone`
> decides what is *stored* — and added the contract test tying them together. Five integration
> facts cover the months round-trip, DOB-wins, years-and-months together, and the persistence of
> guardian / blood group / patient type. `lifecycle-thread.py` step 1 now walks seven written
> forms of one number under `case("LC-REG-05")`.
>
> It also repaired a latent hazard it was not asked to: `lifecycle-thread.py` keeps its own
> `fail` list predating `_harness`, so a `case_check` failure would have been silently dropped
> from the exit code. The new code folds `_harness.failures()` back in.

### Correction to the gap register

**LC-REG-16 (patient merge) is not a coverage gap — it is an unbuilt PRD [S] sub-feature.**
`PatientMerge` and `Patient.MergedInto` are read and filtered on in four places
(`PatientSearch.cs:48`, `Registration/Index.cshtml.cs:30`, `HistoryGenerator.cs:169`,
`RegistrationService.cs:35`) and **written nowhere in `src/`**. The same is true of patient
deactivation: `Patient.Active` has no writer, while five other modules do toggle their own
`Active` flags. Filing these as "nothing asserts this" implies a behaviour exists that does not.

---

## M2 — Front Desk / Help Desk

| Axis | Verdict |
|---|---|
| UI smoke | **THIN** — the three panels are asserted to *exist*, never to be *right* |
| e2e | **OK** for the estimate — `frontdesk-check.py` proves posted + accrued − advance and read-only-ness |
| Business logic | **THIN** — a silent catch on the money path, and one falsely-credited case |

### M2-F1 — LC-FD-05 is marked covered by a script that never asserts it *(Medium)*

The lifecycle document credits **LC-FD-05** *"Free bed availability shown — ward occupancy
accurate"* to `auto frontdesk-check`. `frontdesk-check.py` asserts three things: the estimate is
1700, the estimate after a 700 advance is 1000, and a second read changes nothing. It never looks
at the *Beds free now* panel. What it does read is the free-bed dropdown on `/ipd/admit` — a
different screen, for fixture setup.

Playwright asserts the panel's **heading** is visible (`spec-0018-0019.spec.ts:14`). A panel
rendering an empty table under the right heading passes. The "occupancy accurate" half of the
case is asserted nowhere.

### M2-F2 — the doctors-today panel has no case and no assertion *(Medium)*

US2.1's first question is *"when is the doctor available?"*. `FrontDeskModel` computes
booked / done / waiting per doctor. No `LC-FD` row covers it and nothing asserts the counts —
only that the heading renders.

### M2-D1 — an unresolvable bed-day rate silently understates the estimate *(Medium)*

> **FIXED and verified 2026-07-28 — and my "code-read risk" caveat was too cautious.**
> I recorded this as unreproducible because seeded data never reaches it. Engineering
> **reproduced it two ways** against real Postgres: a ward priced from a date *after* a sitting
> patient was admitted (edge 11's go-live case), and a **gap between two rate versions** —
> `valid_to` is nullable and the exclusion constraint forbids *overlap*, not *holes*. So it is a
> real defect that seeded data does not reach, which is a different and stronger statement than
> the one I made.
>
> **The catch stays** — a read-only enquiry screen must not 500 on a pricing gap. What was wrong
> was that the *sum* could not say it was short. `RateResolver.TotalOverDaysAsync` now returns the
> total **and** the days it could not price; the screen reads *"Net payable now (AT LEAST)"* with
> the unpriced dates named. Seven facts in `BedDayEstimateTests`.
>
> The arithmetic went to `RateResolver` rather than `FolioService` because `Hms.Ipd` cannot see
> `Hms.Admin`, and pulling it in would invert a boundary `ModuleBoundaryTests` polices.
>
> **The other four silent catches are correct and were left alone.** Each filters a *picker*,
> where an unpriced item is properly not sellable (edge 11). The front desk was the only one
> inside a **sum** — and that distinction is the whole finding.

`FrontDesk.cshtml.cs:126`: `catch (RateResolutionException) { }`. If any accrued bed-day fails
rate resolution, that day contributes **zero** and the enquiry desk quotes a number that is too
low, with nothing on screen to say so. For a screen whose only job is to quote, a silently-low
quote is the worst available failure mode. Nothing asserts this path in either direction.

### M20-D2 — the ER could not register an unconscious patient *(High, defect — found by LC-REG-20)*

Filed under M1, found by the case this sweep opened. **`/registration/new` returned HTTP 500 for
every unknown-identity registration**, on every build up to this one.

Model binding turns a blank form field into `null`, so `FullName`'s `= ""` initialiser does not
survive an empty post, and `FullName.Trim()` threw `NullReferenceException`. The name guard means
the **only** post that ever reaches that line with a null name is the unconscious-emergency case —
so the failure was invisible to every other path.

**Why this is the sweep's most important finding.** `RegistrationTests.Unknown_emergency_registers_
without_identity` passes, and always has: it constructs the command with a real `""`. The rule was
correct at the service the entire time and the screen was broken the entire time. Nothing that
tested the rule could have found it.

This is the concrete vindication of the ruling recorded above under M1 — that a row naming a
performer (`jashim`) is a claim about **that operator's path**, and a service-level fact does not
discharge it. Engineering flipped LC-REG-09/10 to `xunit` and then argued the flip was too
generous; QA agreed and opened LC-REG-19/20 for the screen path; LC-REG-20 immediately found a
High defect in the emergency-admission path of a hospital product.

Had the rows been left flipped, this would have shipped.

*Verified by QA on the final build: an unknown-identity registration returns `/registration?q=ALT-…`,
and a blank name **without** the box is still refused with a plain message rather than a 500.*

### M2-F3 — `frontdesk-check.py` bypasses the shared harness *(Low, harness)*

It carries its own `Session` and `check` instead of `_harness`'s, so its assertions carry **no
`case()` ids** (traceability is by filename only) and its two logins are not counted in the
suite's "roles exercised: 12/12" statistic. `_harness.py`'s own docstring says the per-script
copies were removed "in nine of thirteen cases" — this is one of the survivors.

> **FIXED and verified 2026-07-28 — and it was hiding something worse than untraceability.**
> Migrated to `_harness`: five `case()` ids, logins counted, exits through `report()`, `BASE_URL`
> honoured.
>
> **It had no `guard()` at all.** `_harness.guard()` is the environment interlock that refuses to
> mutate anything that is not demonstrably localhost without an explicit `--env` and a typed
> confirmation — precisely because rule 4 means a mutating production run leaves rows that can
> never be removed. `frontdesk-check.py` registers a patient, admits them, takes ৳700, and
> absconds the admission. Pointed at a deployment it would have done all four with **no
> interlock**; only its hardcoded `localhost` default stood in the way, and `BASE_URL` was the one
> thing the migration was going to make configurable.
>
> This is the strongest argument in the sweep for M2-F3 not being a tidiness finding. A script
> outside the shared harness is outside every safety rail the harness carries, not just its
> traceability.

---

## M3 — Appointment & Queue Management

| Axis | Verdict |
|---|---|
| UI smoke | **OK** — `/appointments` in `ROUTES` |
| e2e | **THIN** — issue is covered (`golden-thread` 2); advance, cancel and re-issue are not |
| Business logic | **GAP** — one High defect, and an [M] acceptance criterion enforced nowhere |

### M3-D1 — one cancellation bricks a doctor's queue for the rest of the day *(High, defect)*

`AppointmentsService.IssueSerialAsync` allocates `max(SerialNo) + 1` over rows
**`WHERE state != Cancelled`**, but the unique index is
`(DoctorId, OnDate, SerialNo)` with **no state filter** (`ApptDbContext.cs:57`). So after the
day's highest serial is cancelled, the allocator proposes a number a cancelled row still holds,
collides, and retries the *identical* computation five times before giving up.

Verified live against `Dr. Nusrat Jahan` on a clean queue:

```
before:       Dr. Nusrat Jahan — next serial 1
after issue:  Dr. Nusrat Jahan — next serial 2
cancel serial 1
after cancel: Dr. Nusrat Jahan — next serial 1
issue again:  "Could not allocate a serial — the queue is moving fast, try again."
final:        Dr. Nusrat Jahan — next serial 1      ← stuck, permanently
```

**Why it matters.** Cancel is a one-click button on the queue board. After the first cancellation
of the day's top serial, **no further patient can be queued for that doctor until midnight**. The
message blames concurrency and tells the operator to try again, which can never succeed — so the
counter stops with no idea why. In an outpatient-heavy hospital running ~40 serials a doctor-day,
this is a counter-stopping bug reachable in two clicks.

The retry loop is sound for real concurrency (a competing insert raises the max, so recomputation
finds a new number). It is defeated only by cancelled rows, which the max query excludes but the
index does not.

> **FIXED and verified 2026-07-28.** `IssueSerialAsync` now allocates over **all** rows for the
> doctor-day, matching the unfiltered unique index. Re-running the original repro: issue → cancel
> → re-issue now **succeeds with serial 2**, where it previously returned "the queue is moving
> fast" forever. Five new facts in `AppointmentQueueTests`, including the genuine-concurrency case
> (two counters issuing at once still get two distinct serials, so the retry loop still does its
> real job) and the stale-advance state guard that closes M3-G1.
>
> The engineer also corrected an operator-facing message the brief had not mentioned: the no-show
> toast read *"the serial is freed"*, which was untrue before the fix and would have outlived it.

### M3-D2 — the queue label now contradicts the allocator *(Medium, regression found in verification)*

Found by re-probing M3-D1 rather than by reading the diff. `Appointments/Index.cshtml`'s doctor
dropdown rendered `next serial @(d.TodayCount + 1)`, and `TodayCount` — built in
`IndexModel.LoadAsync`'s `byDoctor` projection — counts
**non-cancelled** appointments. The fixed allocator takes `max(SerialNo) + 1` over **all** rows.
The two disagree as soon as any serial for that doctor-day is cancelled:

```
before:        Dr. Nusrat Jahan — next serial 1
after issue:   Dr. Nusrat Jahan — next serial 2
after cancel:  Dr. Nusrat Jahan — next serial 1     ← the label
after reissue: Dr. Nusrat Jahan — next serial 2     ← what the patient actually got
```

The page's own docstring promises *"the serial constraint is surfaced as 'next free is N' rather
than as an error after the fact (ADR-0015 #1)"*. Before the fix the label and the allocator were
wrong together; now the allocator is right and the label is wrong alone — the receptionist reads a
number aloud to the patient in front of them and the SMS then sends a different one.

Complicating the fix: `TodayCount` serves two meanings that have now diverged. The card also
renders `@d.TodayCount today` beside `Capacity @d.MaxSerials`, and *that* reading should keep
excluding cancelled — a cancelled appointment is not a patient seen today.

**Handed back to the same engineer.** This is the loop working: the defect was fixed correctly and
the fix moved a lie from one place to another, which only a re-probe of the original scenario
would show.

> **FIXED and verified 2026-07-28.** `DoctorCard` now carries `TodayCount` **and** `NextSerial` as
> two separate fields, and the engineer went further than asked: it extracted
> `AppointmentsService.NextSerialFor` / `NextSerialAfter` so the dropdown and the allocator share
> **one arithmetic** rather than merely agreeing today. Re-probe:
>
> ```
> after cancel:  Dr. Nusrat Jahan — next serial 2   ← label
> after reissue: Dr. Nusrat Jahan — next serial 3   actual serial issued: 2
> ```
>
> The label now promises what the patient receives.

### M3-R1 — US3.1's capacity AC is enforced nowhere *(Medium, unmet requirement)*

PRD §5 M3 lists *"[M] Doctor master schedule (days, hours, **max serials**, fee)"* and US3.1's
**AC** reads *"Serial number auto-assigned; booking slip printable; **capacity limit enforced with
waitlist option**."* `DoctorSchedule.MaxSerials` (default 40) is carried into `DoctorCard` and
**displayed**, and `IssueSerialAsync` never consults it. Serial 41, 60, 200 all issue.

Nor does the server check that the doctor **has a session that day**: `IssueSerialAsync` takes
`doctorId` on trust with no lookup against `Schedules` and no FK. The dropdown constrains the
browser; the handler does not — which is the same shape as the gap spec 0030 closed for
`appointments.create`.

> **Routed to the PM, not built — correct under hard rule 2.** Raised as **P25** in
> `docs/architecture/09-questions-for-pm.md`, asking the three decisions a waitlist actually
> needs: what happens at serial 41 (refuse / overbook / waitlist), what a waitlist *is* here (a
> position with no number auto-promoted on cancellation, or a callback list the desk works by
> phone), and whether a supervisor may override the cap. Recommended default: **refuse past
> `MaxSerials`** with a supervisor override through the existing approval engine, and validate the
> doctor has a schedule row for that weekday — that much is enforcement of a rule the PRD already
> states and needs no new product surface. The waitlist stays unbuilt until the PM answers, on the
> grounds that a queue whose rules the operator cannot see is worse than no queue.
>
> P25 also carries the **M3-F1** observation below, so the PM sees the module's whole shortfall
> rather than one symptom of it.

### M3-F1 — the module was scoped "lite" under a freeze that has since lifted *(Medium, coverage)*

`Appointments/Index.cshtml.cs:18` describes the module as *"§9A.2 module 2 (deliberately lite)"*.
That was correct under the MVP freeze. The PM lifted the freeze on 2026-07-27 and Waves 1–3 gave
Pharmacy, IPD, EMR, OT and Radiology full specs — **M3 was never revisited**, and it is absent
from every wave in `11-build-plan-phase2.md` (R3 rides on its queue, but adds nothing to it).
Against §5 M3 the module is missing: postpone/transfer, cancel-with-reason, the printable booking
slip, department/doctor calendar views, doctor-arrival SMS, and no-show tracking.

This is a **scope** observation for the PM, not a defect — recorded here so that M3's thin test
surface is not mistaken for adequate coverage of the module §5 describes.

### M3 test gaps

| ID | Gap | Severity |
|---|---|---|
| M3-G1 | `AdvanceAsync`'s state-guarded UPDATE (a stale second click loses safely, edge 28) is unasserted; LC-QUE-02 is `ui smoke` only | Medium |
| M3-G2 | Cancel → re-issue (LC-QUE-05) unasserted — the path that hides M3-D1 | High |
| M3-G3 | No assertion that a serial cannot be issued for a doctor with no session (LC-QUE-03) or beyond `MaxSerials` (LC-QUE-04) | Medium |

---

## M4 — OPD & Emergency Billing

| Axis | Verdict |
|---|---|
| UI smoke | **OK** — eight routes in `ROUTES`, print sheets in `documents.spec` |
| e2e | **OK** — `golden-thread`, `discount-and-dues`, `money-and-controls` walk invoice → pay → due → day-close |
| Business logic | **THIN** — the forward path is well proven; **the reversal path is not tested at all** |

### M4-D1 — the money invariant does not hold after a refund *(High)*

LC-BIL-04 declares **`Σ receipts + due = net`**. It holds for every invoice state except the one
that matters most. Measured on the live database after `pharmacy-thread.py`:

```
 id | invoice_no         | state    | net | receipts | due | Σreceipts+due
  1 | INV-2026-27-000001 | paid     | 550 |      550 |   0 |  550  ✓
  2 | INV-2026-27-000002 | paid     | 700 |      700 |   0 |  700  ✓
  3 | INV-2026-27-000003 | billed   | 700 |        0 | 700 |  700  ✓
  4 | INV-2026-27-000004 | refunded |   4 |        0 |   0 |    0  ✗
  5 | INV-2026-27-000005 | paid     |   6 |        6 |   0 |    6  ✓
```

`BillingService.RefundAsync` adds the negative receipt and sets `State = Refunded`, but **never
touches `bill.due.balance`**. `CollectAsync` and `CancelInvoiceAsync` both maintain the due row;
refund is the one path that does not.

Whether the fix is *restore the due* (the patient owes it again) or *define the invariant over
non-reversed invoices only* is a design decision, not a QA call — but today the invariant is
stated without qualification and is false in production data. **Nothing asserts it either way.**

### M4-D2 — the two reversal paths have no test in `tests/` *(High)*

`RefundAsync` and `CancelInvoiceAsync` are **referenced by no test in the entire `tests/` tree**
(verified by grep). These are the two operations hard rule 4 exists to govern. Untested rules
they carry:

- a refund must not exceed what was actually paid (`amount > netPaid`)
- a refund needs a reason; a cancellation needs a reason
- a refund is a **negative** receipt pointing at the original, never a delete
- an invoice with money on it cannot be cancelled — it must be refunded
- both need an open counter session

`money-and-controls.py` covers **cancellation** end to end (LC-BIL-10) and `pharmacy-thread.py`
step 6 exercises a refund for its **restock** effect. Neither asserts the money rules above.

### M4-D3 — the lifecycle document credits refund coverage that does not exist *(High, documentation)*

LC-BIL-09 — *"Refund as a negative receipt, approval-gated | no hard delete"* — is marked
**`xunit` MoneySpineTests**. `MoneySpineTests` contains six facts: invoice identity, receipts+due,
parallel collection, over-payment, duplicate session, and DELETE-denied. **There is no refund
test in it.** LC-BIL-12 likewise cites `xunit RateTests`; the class is `ApprovalAndRateTests`.

`eng/check-lifecycle-traceability.sh` is documented as failing when *"the document cites a script
that does not exist"* — it evidently does not extend that check to xUnit class names, so this
drifted silently. A false green on the refund row is worse than a gap: a gap gets triaged.

### M4-F1 — a partial refund marks the whole invoice `Refunded` *(Medium)*

`RefundAsync` sets `invoice.State = InvoiceState.Refunded` unconditionally, whatever the amount.
Refund ৳1 of a ৳1000 paid invoice and the invoice reads as refunded. `CollectAsync` guards on the
due balance and not on state, so a "refunded" invoice can still take money.

### M4-D4 — the day-close statement counts cancelled and refunded invoices *(High)*

`DayCloseService.CloseAsync:52` selects `bill.Invoices.Where(i => i.CounterSessionId == sessionId)`
with **no state filter**, then reports `Gross`, `Discount` and `Net` as sums over it. A cancelled
invoice contributes its full `Net` to a **printed financial document**, and — because cancellation
creates no receipt — nothing offsets it anywhere on the statement.

Refunds fare slightly better: line 56 computes a separate `refunds` figure, so the statement at
least shows them, but `Net` still includes the reversed invoice.

This is the same root cause as **M22-D1** (the MD dashboard). One fix serves both: define which
invoice states count toward income once, and apply it in the dashboard and the day-close summary
together. *(Found by reading; not separately measured — the dashboard half was measured.)*

> **M4-D1 / D2 / D3 / D4 and M22-D1 all FIXED and verified 2026-07-28**, as one coherent change.
>
> A new `src/Modules/Billing/Hms.Billing/InvoiceValue.cs` is the single definition of what an
> invoice is worth after a reversal, and both the MD dashboard and the day-close statement read
> it. The invariant is restated as `Σ receipts + due = Realised(state, net, refunded)` — LC-BIL-04
> stated it unqualified, which was true only until something was reversed.
>
> **The design decision, and why it is the right one.** The other candidate fix was to add the
> refunded amount back onto `bill.due.balance`, which also makes the arithmetic close. Engineering
> rejected it: a due is a **receivable**, so restoring it puts the patient back on
> `/billing/dues` and into the MD's "Outstanding due" tile for money the hospital just decided to
> give back — the hospital would chase a refund it granted. A refund reduces what the invoice
> *earned*; it does not re-charge the patient.
>
> **The two consumers deliberately read different members.** The dashboard uses `Realised` (it has
> no refunds line and must stay comparable with the collected tile). The day-close statement uses
> `IsReversed` to drop reversed invoices whole and leaves its existing separate `Refunds` line to
> carry money handed back — because the statement prints "Gross billed / Less: discount / Net
> billed" as a visible subtraction, so netting a partial refund into `Net` would break the printed
> arithmetic and double-count against the line below it.
>
> *QA verification, on a full-suite database:*
>
> | Check | Result |
> |---|---|
> | Spine invariant across all 30 invoices (`billed`/`paid`/`cancelled`/`refunded`) | **0 violations** |
> | Dashboard "Income today" | **৳71,143** = realised; the naive sum would read ৳71,447 |
> | Dashboard invoice count | 28 live, not 30 |
> | New xUnit facts | 10, incl. `The_day_close_statement_leaves_out_the_invoices_that_were_reversed` and `Reversed_invoices_do_not_reach_the_income_figure` |
>
> *One correction to my own work:* my first spine query reported a violation on invoice 16. That
> was the query missing folio-parented advances (an advance is a receipt on the **folio**, not the
> invoice), not a product defect. Corrected before drawing any conclusion.

### M4-F2 — a refund inherits the tender of the **largest** receipt *(Medium)*

`RefundAsync:467` picks `receipts.Where(Amount > 0).OrderByDescending(Amount).First()` and copies
its `Tender`. For a patient who paid ৳500 cash + ৳1000 card, a ৳200 cash refund is recorded as
**card**. `DayCloseService:48` computes `expectedCash = OpeningFloat + Σ receipts where
Tender = "cash"`, so the returned cash never reduces expected cash: the drawer comes up short and
the operator is handed a variance with no explanation. The "first row wins" family the last audit
swept (commit `7680940`) did not reach this one.

> **FIXED and verified 2026-07-28.** `tender` is now a **required parameter** of `RefundAsync`,
> chosen by the operator on the refund screen at the moment money leaves the drawer — not inferred
> from what the patient paid weeks earlier. It is normalised and validated against a new `Tenders`
> set (`cash`/`card`/`bkash`/`nagad`/`corporate`), which also closes a latent hazard nobody had
> named: day-close groups receipts by the tender string **exactly**, so a stray `"Cash"` was a
> different drawer and a silent cash variance. The `RefundOfReceipt` link now prefers the largest
> receipt **in the same tender**. Asserted by
> `A_cash_refund_is_booked_as_cash_however_the_patient_paid` and
> `A_refund_in_a_tender_nobody_recognises_is_refused`.
>
> **M4-F1 fixed too, and it mattered more than I had ranked it.** I flagged before verification
> that `InvoiceValue.Realised` returns 0 for any `Refunded` invoice, so leaving partial refunds
> marked `Refunded` would have turned a ৳4 overstatement into a ৳999 *under*statement — a worse
> defect introduced by the fix for the first one. Engineering found the same interaction
> independently and made the state conditional: `Refunded` only once everything taken has gone
> back and nothing is owed. Asserted by
> `An_invoice_is_only_refunded_once_everything_taken_has_gone_back` and
> `A_partial_refund_leaves_the_invoice_live_and_worth_what_is_left`.

---

## M5 — Prescription & EMR

| Axis | Verdict |
|---|---|
| UI smoke | **OK** — seven routes, print sheet in `spec-0024.spec.ts` |
| e2e | **OK** — `emr-thread` walks vitals → note → sign → supersede → orders → charts |
| Business logic | **OK** — the strongest module in the sweep so far |

`EmrTests` carries 12 facts that map almost one-to-one onto the service's guards: draft resumed
not duplicated, single parent, immutability after signing, double-sign refused, empty note
refused, supersede carrying drugs forward, supersede only from signed, no double-correction,
vitals validation, single-shot dose with a reason for a miss, one handover per admission,
template application. This is what the other modules' business-logic axis should look like.

### M4-F3 — `CollectAsync` still takes an unvalidated tender string *(Medium, open)*

Raised by engineering while fixing M4-F2, and **deliberately left open**. `RefundAsync` now
normalises and validates its tender against `Tenders`; `CollectAsync` does not. Day-close groups
receipts by the tender string **exactly** (`tenderTotals.GetValueOrDefault("cash")`), so a
receipt written as `"Cash"` lands in a different bucket and silently disappears from expected
cash — the operator counts a drawer that is over by that amount with nothing to explain it.

The blast radius is **wider** than the refund path this sweep fixed: every collection at every
counter goes through `CollectAsync`, while refunds are rare and approval-gated. It is unfixed
only because it was found at the end of a handoff and changing the collection signature deserves
its own pass rather than being smuggled into a refund fix.

### M4-F4 — a second bug was hiding behind M4-F1 *(closed with it)*

Engineering found that because a ৳1 refund set `State = Refunded`, the refund screen's
double-reversal guard *appeared* to work — by locking the operator out of the remaining ৳999.
Fixing M4-F1 removed the false state **and** the false sense that the guard was doing its job.
Worth recording because it is the shape of defect this sweep exists to find: a control that looks
correct because a different bug is masking what it does.

### M4-F5 — the fix reached further than the screens *(favourable)*

`bill.v_dashboard_day` sums `day_close_summary`, so it inherited the M4-D4 correction for free.
Had the day-close figure been patched at the page instead of in `DayCloseService`, the M15
accounting view would still have been reporting cancelled invoices as billed.

### M5-F1 — the gap register under-credits this module *(Low, documentation)*

Two rows are marked **`gap`** that are in fact asserted:

| Row | Marked | Actually |
|---|---|---|
| LC-EMR-06 — draft saved and resumed | `gap` | `EmrTests.Draft_is_resumed_not_duplicated` |
| LC-EMR-07 — template reuse prefills the note | `gap` | `EmrTests.A_template_applies_its_drug_lines` |

Only the *favourite* half of LC-EMR-07 is genuinely uncovered (`AddFavouriteAsync` has no test).

Together with M4-D3 this is the sweep's most important documentation finding: **the gap register
drifts in both directions.** It claims coverage that does not exist on the refund path, and
claims gaps that were closed in EMR. Its 85% headline is therefore not a measurement of the
product — which is exactly why this sweep reads the code rather than the register.

### M5 test gaps

| ID | Gap | Severity |
|---|---|---|
| M5-G1 | `AddFavouriteAsync` has no test (the favourite half of LC-EMR-07) | Low |
| M5-G2 | LC-NUR-06 — a missed dose being *visible as missed* on the chart, not merely recordable | Medium |

---

## M6–M11 — IPD, OT, Diagnostics, LIS, Radiology, Pharmacy

These six were built under Phase-2 specs (0016, 0017, 0024–0026) with dedicated threads, and the
sweep found them in good order. Guards versus assertions:

| Module | Service guards | xUnit facts | Thread | Verdict |
|---|---|---|---|---|
| M6 IPD | 44 (`IpdService` 27 + `FolioService` 17) | 11 + 4 terminal-exit + 2 settlement-reopen + 4 lifecycle-gap + 4 concurrency | `ipd-thread`, `edge-cases` | **OK** |
| M7 OT | 15 | 10 | `ot-thread` | **OK** |
| M8 Diagnostics | 5 | 2 | `golden-thread`, `money-and-controls` | **OK** |
| M9 LIS | 5 | 6 | `golden-thread` | **OK** |
| M10 Radiology | 3 | 6 | `radiology-thread` | **OK** |
| M11 Pharmacy | 24 (`StockService` 16 + `PurchaseService` 8) | 7 | `pharmacy-thread`, `pharmacy-full` | **OK** |

The business-logic axis is genuinely covered here: FEFO across batches, expired stock invisible to
sale, concurrent sales serializing with stock never negative, theatre and surgeon double-booking
refused, completion idempotent, bed-days idempotent by constraint, settlement applying advances
and locking the folio, sample rejection spawning a recollection with the same tests.

**This is the sweep's honest limit:** for these six I compared each service's guard count against
the test inventory and read the tests that the gap register disputes (below). I did not re-derive
every one of the 96 guards. Where I did look closely — the register's disputed rows — the tests
were stronger than the register claimed, not weaker.

---

## M20 — SMS / Notification

| Axis | Verdict |
|---|---|
| UI smoke | **OK** — `/notifications/tray` in `ROUTES` |
| e2e | **GAP** — no script exercises the module; LC-XCUT-08 is an open gap |
| Business logic | THIN — `tests/Hms.Integration.Tests/SmsQueueTests.cs` (8 tests) now asserts the rules below; written against this section's own gap list *(row corrected 2026-08-03, spec 0039 — the table said "no test referenced `SmsQueue`" after the tests existed)* |

The rules the module states explicitly, now asserted by `SmsQueueTests`:

| Rule | Where |
|---|---|
| An SMS commits with the business fact that caused it — a rolled-back registration leaves none (G19) | `SmsQueue.cs:13` |
| A patient with no phone is a recorded **skip**, not a failure (edge 24) | `Queue`, `SmsState.SkippedNoPhone` |
| Resend re-queues **unchanged** — the operator resends, never rewrites (§5 M20 [M]) | `Resend` |
| Simulation vs live posture (`HMS_SMS_MODE`) | `SmsOptions.From` |
| Segment counting | `SegmentsFor` |

### M20-D1 — the billable-segment count is understated for long messages *(Medium)*

`SegmentsFor` divides by 160 (GSM-7) or 70 (UCS-2). Those are the **single-message** limits; a
*concatenated* SMS spends 6–7 bytes per part on the UDH header, so real segments hold **153**
(GSM-7) and **67** (UCS-2) characters. A 320-character Latin message is 3 segments in the world
and 2 in this code; a 210-character Bangla message is 4 and 3.

The tray renders this figure as **"N billable segment(s)"**, so the hospital reads a low estimate
of what it is spending.

**How close the seeded product already is.** Measured on `notif.sms` after a full suite run, every
template is Latin and 93–122 characters, so all count as 1 segment under either arithmetic — the
defect is latent, not currently firing:

```
registration  115 chars   appointment   93 chars
registration  122 chars   report_ready  99 chars
```

Two ordinary changes trip it. The hospital name is configurable and prefixes every message, so a
name ~31 characters longer than "Altushi General Hospital" pushes registration past 153 and the
count silently stays 1 where the gateway bills 2. And PRD §2 records that *"Bangla appears only in
SMS templates if a customer demands it"* — the moment one does, every message takes the UCS-2 path
where `ceil(n/70)` and the real `ceil(n/67)` diverge.

**Correction to my own arithmetic, from engineering.** I first gave the UCS-2 divergence bands as
"68–70, 135–140, 202–210". **68–70 is not a divergence:** a 70-character body is a *single*
message, and a single message carries no UDH header, so it costs one segment under both the naive
and the correct rule. The real bands are **135–140 and 202–210** (UCS-2) and **307–320** (GSM-7).
My own independent validation table below had `Bangla 68 → 1/1` and `Bangla 70 → 1/1` sitting in
front of me and I did not notice they contradicted the claim. Worth recording: the check caught
it, the author did not.

Two lesser inaccuracies in the same function: `c > 127` treats GSM-7 extension characters as
Unicode, and the GSM-7 escaped set (`{ } [ ] ~ ^ \ |`) bills as two characters but counts as one.

> **FIXED and verified 2026-07-28.** `SegmentsFor` now carries the GSM 03.38 basic and extension
> alphabets, counts septets (escaped characters costing two), picks the encoding from the alphabet
> rather than from `c > 127`, and applies the single-message limit (160/70) or the concatenated
> one (153/67) as appropriate.
>
> **Validated independently by QA** — I reimplemented the arithmetic separately rather than trust
> the unit tests shipped with it:
>
> | Body | old | new | |
> |---|---|---|---|
> | the six seeded templates (93–121 chars) | 1 | 1 | no regression; matches the stored values |
> | 320 Latin characters | 2 | **3** | the concatenation undercount, fixed |
> | 135 / 140 Bangla | 2 | **3** | the UCS-2 divergence band |
> | 210 Bangla | 3 | **4** | |
> | 100 × `é` | 2 | **1** | `é` is GSM-7; the old `c > 127` test billed it as Unicode |
>
> That last row is a case my own finding had not spelled out, and it errs the *other* way: the old
> code **over**-counted accented Latin. The correction is not simply "the number goes up" — it is
> that the number becomes right, and the fix was checked in both directions.

### M20-F1 — ~~the tray offers a button its own permission cannot press~~ *(QA error, corrected)*

**I got this one wrong, and the correction is worth more than the original finding.**

I reported that `/notifications/tray` (`[Authorize(Perm.NotificationsRead)]`) renders a **Resend**
button posting to `/admin/sms?handler=Resend` (`[Authorize(Perm.AdminMastersManage)]`), so a
`notifications.read`-only grant would produce a visible button that 403s. I had found the form and
the two policies and did not check for a guard around the form.

There is one, and it predates this sweep — `Tray.cshtml`:

```cshtml
@if (Model.Can("admin.masters.manage") && m.Recipient is not null)
{
    <form method="post" action="/admin/sms?handler=Resend" …>
}
```

The button is hidden without the grant. **The behaviour was already correct.**

**What was genuinely missing** is anything that *keeps* it correct: no test joined the view's guard
to the policy on the handler it posts to, and §12 is editable data at `/admin/users` — spec 0030 F1
showed those grants drift in practice. That is now `ViewGuardPermissionTests`.

And chasing it turned up a real latent trap the original finding would never have reached:
`HmsPageModel.Can(p)` tests the raw **claim**, while `Perm.*` constants are **policy** names
carrying the `perm:` prefix that `PermissionPolicy.TryParse` strips. So
`Can(Perm.AdminMastersManage)` **compiles and is silently always false** — a guard that looks
correct in review and hides the button from everybody. `No_view_guard_is_passed_a_policy_name_
instead_of_a_permission` now forbids it outright.

---

## M21 — Administration, Security & Audit

| Axis | Verdict |
|---|---|
| UI smoke | **OK** — eight routes in `ROUTES`, `authz.spec.ts` |
| e2e | **OK** — `role-journeys` (12 roles × 64 routes), `grant-drift` (deployment vs code matrix) |
| Business logic | **OK** — approvals, delegation, rate versioning, import, audit-commit-together all asserted |

`ApprovalAndRateTests` (7) and `AuditWriterTests` (2) cover the module's core: auto-approve under
threshold, pending above it decided once, delegation windows, overlapping rate versions refused by
constraint, price change as a new version with history resolving unchanged, package ≻ corporate ≻
standard scope precedence, and audit committing or rolling back with its change. This is the
second-strongest module after M5.

---

## M22 — Management Dashboards & MIS

| Axis | Verdict |
|---|---|
| UI smoke | **OK** — `/dashboard`, MD-only (LC-XCUT-01) |
| e2e | **THIN** — `golden-thread` 10 asserts one absolute figure on a fresh ledger |
| Business logic | **GAP** — one defect, and the only assertion cannot detect it |

### M22-D1 — cancelled and refunded invoices still count as income *(High)*

`Dashboard.cshtml.cs:53` selects invoices by `CreatedAt` alone — **no state filter** — and
`IncomeToday = todays.Sum(i => i.Net)`. A cancelled or refunded invoice keeps contributing its
full `Net` to the MD's income tile.

Measured live:

```
SQL, today:   billed 1×700  ·  paid 3×1256  ·  refunded 1×4     total 1,960
Dashboard:    Income today      ৳ 1,960   5 invoice(s) · 2 patient(s)
              Collected today   ৳ 1,256
              Outstanding due   ৳   700
```

The refunded invoice is in the income figure. `CollectedToday` sums receipts and therefore *does*
net out the refund, so the two tiles disagree by exactly the reversed amount — and the gap reads
like an unpaid due rather than a reversal. `InvoicesToday` and `PatientsToday` count it too.

At ৳4 this is noise. On a refunded ৳50,000 surgery it is a materially wrong number on the only
screen §9A.1 built for the owner's "is money leaking?" question.

**Why no test caught it:** the sole assertion, `golden-thread.py:206`, checks the dashboard reads
exactly ৳550 on a **fresh** ledger where nothing has been cancelled or refunded. The defect is
invisible by construction to the only test that looks.

This and **M4-D1** are one theme: *a reversal is recorded on the receipt side but not propagated
to the due, the invoice state's meaning, or the aggregates that read them.*

---

## Cross-cutting finding: the additive-migration gate guarded 2 of 14 contexts *(High, process)*

Found by the M1 engineer while adding a migration, and worth more attention than its origin
suggests. `03-data-model.md` §12 makes migrations **additive-only**, and
`eng/check-additive-migrations.sh` is the CI gate that enforces it. In `.github/workflows/ci.yml`
that gate was scripted for **`KernelDbContext` and `AuthDbContext` only** — two of the fourteen
`DbContext`s in the solution.

The other twelve — `reg`, `bill`, `diag`, `lis`, `appt`, `adm`, `notif`, `pharm`, `ipd`, `emr`,
`ot`, `radiology` — could have carried a `DROP COLUMN` or `DROP TABLE` into a release and CI would
have reported green. That includes every schema holding money and clinical history.

A gate that runs on one seventh of the surface reads, on the CI dashboard, exactly like a gate
that runs on all of it.

`RegDbContext` is now scripted and checked (it was the context this sweep's own migration
touched). **Eleven remain unguarded** — a bigger call than one handoff should make unilaterally,
and the reason this is recorded here rather than quietly fixed. Recommended: script all fourteen
in a loop rather than enumerating them, so a fifteenth context cannot be forgotten.

---

## Cross-cutting finding: the gap register is unreliable in both directions

The 26-row register in `patient-lifecycle.md` was triaged by hand against script names. Checking
it against the tests themselves:

**Marked `gap`, actually asserted:**

| Row | Marked | Actually asserted by |
|---|---|---|
| LC-EMR-06 — draft saved and resumed | `gap` | `EmrTests.Draft_is_resumed_not_duplicated` |
| LC-EMR-07 — template reuse prefills the note | `gap` | `EmrTests.A_template_applies_its_drug_lines` (the *favourite* half is genuinely open) |
| LC-LAB-07 — rejected sample spawns a recollection | `gap` | `LisAndDayCloseTests.Sample_chain_collect_receive_reject_spawns_child_with_same_tests` — new barcode, `RecollectionOf` chain, same tests carried |
| LC-OT-08 — case cancelled | `gap` ("neither asserted") | `OtTests.A_cancelled_case_frees_its_slot` — the slot half is asserted; only "no completion charge posted" is open |
| LC-OT-09 — case postponed | `gap` | `OtTests.Cancelling_and_postponing_both_need_a_reason` exercises postpone; only "original slot released" is open |

**Marked covered, not asserted:**

| Row | Marked | Actually |
|---|---|---|
| LC-BIL-09 — refund as a negative receipt, approval-gated | `xunit` MoneySpineTests | **no refund test exists in `MoneySpineTests`, or anywhere in `tests/`** |
| LC-BIL-12 — effective-dated rate resolves by service date | `xunit` RateTests | class is `ApprovalAndRateTests`; no `RateTests` exists |
| LC-FD-05 — free beds shown, occupancy accurate | `auto` frontdesk-check | the script never reads the beds panel |

### Why it drifted — the mechanism

`eng/check-lifecycle-traceability.sh:34` is the whole of the coverage check:

```sh
for s in $(grep -oE '(auto) [a-z0-9-]+' "$DOC" | awk '{print $2}' | sort -u); do
  [ -f "eng/verify/$s.py" ] || missing_scripts="$missing_scripts $s"
```

It validates **`auto` citations only**, and only that a **file exists**. It therefore cannot catch:

- a **`xunit` citation naming a class that does not exist** → LC-BIL-12's `RateTests`;
- a **`xunit` citation naming a class that exists but lacks the test** → LC-BIL-09's refund;
- an **`auto` citation to a real script that never asserts the case** → LC-FD-05's beds panel;
- a row marked **`gap` that is in fact asserted** — nothing looks in that direction at all.

The durable fix is to make the citation checkable in both directions: validate `xunit <Class>`
against `tests/**/*.cs`, and require every `auto <script>` row to appear as a matching
`case("LC-…")` call inside that script. `_harness.case()` already exists for exactly this
purpose — which is why **M2-F3** (`frontdesk-check.py` bypassing the harness) is not a cosmetic
finding: the scripts that skip `case()` are precisely the ones whose coverage claims cannot be
verified, and LC-FD-05's false green came from one of them.

**Consequence for the 85% headline:** it is not a measurement of the product. It counts rows in a
hand-maintained document, and those rows are wrong in both directions. Deriving the register from
the tests (as the traceability script already does for scripts) is the durable fix.



---

## Resolutions — 2026-07-28 (engineering)

### QA-H1 — fixed

`eng/verify/lifecycle-suite.py` now executes `--tier all` as **t0 → t2 → t1** (`ALL_ORDER`), so
t2 meets the fresh ledger it documents before the twelve mutating t1 scripts spend it. t2's
internal order is untouched — `golden-thread.py` still runs before `discount-and-dues.py`,
which bills the patient the former registers. An explicit `--tier t0|t1|t2` behaves exactly as
before. `docs/qa/README.md` states the order in both places it describes a run.

`golden-thread.py`'s absolute assertions were **not** weakened. They are the reason t2 exists.

*Verified by engineering:* `--tier t0` green against localhost:5199; ordering asserted by reading
`ALL_ORDER` and `TIERS["t2"]` back out of the module.

*Verified by QA (2026-07-28, the run engineering left to us):* `hms` dropped and recreated, app
restarted on the new build, then `python3 eng/verify/lifecycle-suite.py --tier all` →
**SUITE GREEN — 14 scripts, 0 failed · roles exercised 12/12 · ward census 13 free beds,
unchanged.** The same command on the same database was red before the fix.

### M1 — Patient Registration & ID

**M1-D1 (High) — an infant aged in months could not be registered — fixed end to end.**
`ParseAge` parsed `8 months` and `OnPostAsync` then dropped it: months reached neither the
command, nor the service guard, nor the row, and `ck_identity` would have refused the insert
even if it had. Carried through instead of papered over:

| Layer | Change |
|---|---|
| `RegisterPatientCommand` | `short? AgeMonths` added — registration stays **one** transaction (G19) |
| `RegistrationService` | guard accepts DOB **or** years **or** months **or** unknown-identity; `AgeAsOf` stamped for a months-only age exactly as for a years-only one |
| `RegDbContext` | `ck_identity` widened to `… or age_months is not null` |
| migration | `20260728104406_WidenIdentityCheckForAgeMonths` — additive (widening a CHECK); `check-additive-migrations.sh` passes on the idempotent script |
| `New.cshtml.cs` | passes months through; its unreachable `p.AgeMonths = months` and second `SaveChangesAsync` are gone |

**Years do not clear months and months do not clear years.** Only a DOB clears both. They are two
components of one age — an operator who records "1 year 6 months" means both — and `Ui.AgeDisplay`
already reads them as a pair. Nulling years on a months entry would silently turn 18 months into
6 months.

**Gaps closed**

| Gap | Closed by |
|---|---|
| M1-G1 `ParseAge` untested | `tests/Hms.Web.Tests/RegistrationInputTests.cs` — 14-row table over §7 U13's six shapes plus empty/whitespace/garbage, asserting the whole tuple including `Estimated` |
| M1-G2 `NormalizePhone` untested; messy-phone search unasserted | same file (storage side **and** `PatientSearch.PhoneDigitsOf`, the search side); LC-REG-05 in `lifecycle-thread.py` step 1 now asserts all seven typed forms over HTTP through `_harness.case()` |
| M1-G3 "DOB wins" unasserted | `RegistrationTests.Dob_wins_over_both_age_columns`, `.Age_in_months_registers_and_persists`, `.Years_and_months_together_are_both_kept` |
| M1-G4 guardian / blood group / patient type | `RegistrationTests.Guardian_blood_group_and_patient_type_persist`, `.Patient_type_defaults_to_general` |

**M1-F1 (new, Low) — `patient.patient_type` is written and read by nothing.** LC-REG-14 expects
"type recorded, **drives later pricing**". Grep across `src/` finds two writers (the registration
page; `PharmacySale` walk-ins) and **no reader**. `RateResolver`'s `corporate:<id>` scope comes
from the referrer chosen on the billing screen, not from the patient's type. The field is
therefore recorded and inert: the row is now cited as "type recorded" only, and whether a
corporate patient should price differently by default is a **PM question**, not a test gap.

**M1-F2 (new, Low) — `Guardian` is captured and never displayed.** Written by the service, read by
no page and no print template. Nothing but a row assertion can prove it landed, which is why
LC-REG-12 is closed at the xunit layer.

**M1-F3 — two phone normalisers, one rule.** `NewModel.NormalizePhone` (what is stored) and
`PatientSearch.PhoneDigitsOf` (what a search matches) both implement the `880 → 0` rule
independently. They agree today and a test now pins that they agree, but the duplication is a
drift risk worth one shared helper.

### QA ruling on the engineer's push-back — LC-REG-09 / LC-REG-10

Engineering flipped both rows from `gap` to `xunit RegistrationTests` and then challenged its own
flip: both tests assert the **service** invariant, and neither drives the ≤ 60-second screen over
HTTP, so *"can a receptionist actually complete this form with the identity-unknown box ticked"*
stays unproven.

**The challenge is right, and the flip is also right.** Two different things:

- By the document's own legend — `xunit` = asserted by a test project under `tests/` — the rule
  *is* asserted. Leaving the rows as "nothing asserts this" was false, and the flip corrects it.
- But every `LC-REG` row carries a **performer** (`jashim`), and a case performed by a named
  operator is a case about that operator's path. A service-level fact does not discharge it.

So: rows stay flipped, and the screen path is recorded as **new** cases rather than by reopening
old ids (ids here are stable and append-only). Added to handoff 3:

| New case | What |
|---|---|
| LC-REG-19 | A receptionist completes registration with **no phone** over HTTP |
| LC-REG-20 | A receptionist completes an **unknown-identity** registration over HTTP — the ER path |

This is the more useful outcome than either reading alone: the invariant is pinned cheaply in
xUnit, and the operator's path is pinned where operators actually fail.

**On the fifth test project — keep it.** `tests/Hms.Web.Tests` is 48 assertions in ~50 ms with no
Docker, covering the parsers behind the product's front door, and it is wired into `ci.yml` (a
project CI does not name does not run — the workflow enumerates projects rather than running the
solution). Hiding page-model parser tests inside a project whose name promises NetArchTest rules
would save one `.csproj` and cost the next reader the ability to find them.

**LC-REG-16 (merge) and patient deactivation — the QA engineer's classification is correct.**
`reg.patient_merge` has a `DbSet` and no writer; `patient.merged_into` and `patient.active` are
read by `PatientSearch.Searchable`, `/registration`, `HistoryGenerator` and
`FindDuplicatesAsync`, and written nowhere in `src/`. Every `.Active = …` assignment in the
codebase belongs to a different entity — pharmacy product, user, referrer, theatre, EMR
template. Unbuilt PRD §5 M1 [S] sub-features, not test gaps. No writer was missed.

---

# Sweep summary

## What the three axes actually found

The sweep was organised around three questions per module. They did not pay off equally, and the
imbalance is the most useful thing this document records.

| Axis | Findings | What it turned out to be good for |
|---|---|---|
| **UI smoke** | 1 (M2's panels asserted to exist, not to be right) | Already strong — `ROUTES` covers every protected route by owning role, and the shell/console/icon assertions catch real breakage. Little left to find. |
| **e2e** | 3 | Strong for the paths the threads walk. Its blind spot is the path nobody thought to walk — M20 has no script at all. |
| **Business logic** | **12 of the 16 findings**, including every High | The axis that mattered. Rules are enforced in services; tests were written per spec, so a rule no spec happened to feature was enforced and unasserted. |

**The pattern behind the High findings.** Seven of them are the same shape: *a state the product
can reach that the code downstream never considered.* A cancelled serial the allocator ignored. A
reversed invoice the income figures counted. A partially refunded invoice treated as fully
reversed. In each case the forward path was well tested and the reversal was not — and reversals
are precisely what hard rule 4 exists to make safe, because a hospital cannot delete money.

That is worth stating plainly for the next sweep: **test the exits, not just the happy path.**
Every one of these defects was reachable in two clicks from a screen an operator uses daily.

## What was fixed, and what was left open

Nothing was closed by assertion. Every fix below was re-probed by QA against the original failing
scenario, not accepted from a diff.

| Left open | Why |
|---|---|
| **M4-F3** `CollectAsync` unvalidated tender | Found at the end of a handoff; wider blast radius than the refund path, so it deserves its own pass rather than being smuggled into one |
| **M3-R1** capacity AC | Product scope — routed to the PM as **P25** under hard rule 2, with a recommended default |
| **LC-XCUT-11** load/concurrency | Open by decision, pending ADR-0024 — unchanged by this sweep |
| **M2-D1** silent rate catch | Recorded as a **code-read risk**, not a proven defect: the path could not be reached with seeded data and QA declined to write it up as reproduced |
| **LC-REG-16** merge, patient deactivation | Reclassified: unbuilt PRD [S] sub-features, not test gaps |
| Spec integrity on **0006–0010** | Pre-existing missing `plan.md` archives, outside this sweep — fixing them would mean making retroactive claims that cannot be verified |

## The documentation finding, and why it outranks the defects

`patient-lifecycle.md`'s register was wrong in **both directions** — five rows marked `gap` that
xUnit already asserted, and five citations naming a script or class that did not assert (or did
not exist). The 85% headline measured the document, not the product.

One mechanism explains all ten: `check-lifecycle-traceability.sh` verified only that a cited
**`auto` script file existed**. It never resolved `xunit` class names, never checked that a cited
script asserts the case it is credited with, and nothing looked for a false gap at all.

- **Corrected:** all ten rows.
- **Guarded:** the script now resolves every `xunit` citation against `tests/`, and the guard was
  **negative-tested** — reintroducing the original `RateTests` citation makes it fail.
- **Still unguarded, deliberately:** that a cited script *asserts what the row claims*. The tool
  for it exists (`_harness.case()`), but two scripts still bypass the harness, so enabling it as a
  hard failure today would fail ~40 legitimately-covered rows and teach the "red is normal" habit
  QA-H1 was about. The staged path is written up in this sweep's notes.

A false green on the refund row is worse than a gap. A gap gets triaged; a false green gets
believed — and LC-BIL-09 claimed the most sensitive money operation in the product was covered by
a test that did not exist.

---

# Final regression — 2026-07-28

Run on a freshly seeded database, in the documented order (**reset → threads → one Playwright
run**). That order matters: my first Playwright attempt failed on a test that hardcodes
`PatientId=1` as "Rahim Uddin", because a probe of mine had run between the reset and the threads
and taken `ALT-000001`. Diagnosed to my own process, not filed as a defect.

| Layer | Result |
|---|---|
| `dotnet build hms-erp.slnx -c Debug` | clean, **0 warnings** |
| `dotnet test hms-erp.slnx -c Debug` | **306 passed, 0 failed** (was 156) — kernel 22 · architecture 29 · integration 150 · web 104 · print 1 |
| `lifecycle-suite.py --tier all` | **SUITE GREEN — 14 scripts**, roles 12/12, ward census unchanged |
| `eng/verify/ui` Playwright | **245 passed, 0 failed** |
| `check-lifecycle-traceability.sh --stats` | OK — 175 cases, 162 covered, 13 gaps |

## Two harness defects found by the final regression itself

**`money-and-controls.py` seeded an invalid phone.** It generated a **ten**-digit number
(`0199{STAMP}0`); a Bangladeshi mobile is eleven. `NormalizePhone` correctly declines to reformat
anything that is not eleven digits, so those patients were stored undashed — a number no operator
could type. Fixed to eleven digits.

**`spec-0020.spec.ts` picked row 0 blind.** It asserted that the *first* `01…` type-ahead hit has
a dashed stored phone, conflating "a dashed number is findable by its digits" (the product claim)
with "every patient in the corpus has a well-formed phone" (a fact about test data). It now takes
the first candidate that *is* dashed, and says so if none exists.

**Both surfaced because of QA-H1.** Reordering `--tier all` to `t0 → t2 → t1` made t1's patients
the newest, so the type-ahead returned a `money-and-controls` patient first for the first time.
The fix broke nothing; it changed run ordering and exposed a latent fragility that had been one
`ORDER BY` away from firing since spec 0031. Worth recording as the cost of the fix, and as a
reminder that "first row wins" (commit `7680940`'s theme) reaches into test code too.

---

# Addendum — concurrency audit (2026-07-28, after spec 0032 closed)

Raised by a follow-up question, not by the sweep. Recorded here rather than reopening a `Done`
spec.

## The model: contention is PostgreSQL's problem, and that is the right call

`src/` contains **no** `lock`, `Interlocked`, `Concurrent*`, `SemaphoreSlim`, `Mutex`,
`BackgroundService`, `async void`, `.Result` or `.Wait()`. There is nothing in-process to race on
because there is almost no in-process state:

- Domain services are `AddSingleton` and **stateless** — the `DbContext` arrives as a *parameter*,
  never a field. That is what makes the singleton registration correct rather than a captive
  dependency.
- `HmsTx` is a singleton holding only a connection string; it opens a fresh `NpgsqlConnection` and
  a fresh `TxScope` per call, and `TxScope` owns its own context list.
- Contention is resolved in the database: **17 `SELECT … FOR UPDATE` sites**, unique-constraint
  retry for serials and number series, `IsConcurrencyToken` on billing/auth rows, and
  `pg_advisory_lock(422026)` so two instances cannot migrate at once.

On 2 vCPU / 3 GB this is a better answer than in-process synchronisation. **No change recommended.**

## CONC-1 — folio and stock are locked in opposite orders by two live paths *(Medium-High, open)*

| Path | Order |
|---|---|
| `IpdBilling.IssueIndentAsync` (ward indent — `nasrin` → `parvin`) | `EnsurePostableAsync` → **folio lock**, then `AllocateFefoAsync` → **stock batch lock** |
| `OtBilling` consumables (`shaheen`) | `AllocateFefoAsync` → **stock batch lock**, then `EnsurePostableAsync` → **folio lock** |

Two transactions on the **same folio and the same batch** acquire the two locks in opposite order —
a textbook ABBA deadlock. It is reachable: a patient in theatre consuming from batch X onto folio F
while a nurse issues a ward indent for the same patient containing the same product. Ward and OT
draw on the same outlet.

**What the operator sees.** PostgreSQL detects it and aborts one side with SQLSTATE `40P01`. That
is a `PostgresException`, and the handlers catch only `OtException`/`OtBillingException`/
`PharmacyException`/`IpdException`/`BillingException` — so it escapes as an **unhandled 500** to a
non-technical operator (§7). `grep` for `40P01`/`deadlock` across `src/` returns nothing.

Nothing is corrupted: the transaction rolls back whole, so G19 and rule 4 hold. The cost is a
counter stopping with an error nobody can act on, rarely and unreproducibly.

**Why no test catches it.** `ConcurrencyTests` covers *two operators posting to one folio* and
`PharmacyStockTests` covers *concurrent sales of the last units* — each contends on **one**
resource. Nothing exercises two resources acquired in two orders. This is the concrete, cheap end
of **LC-XCUT-11**.

**Fix:** pick a global order — folio before stock is the natural one, since the folio is the
coarser resource and `IssueIndentAsync` already does it — and make `OtBilling` conform.
`IpdService.TransferAsync` already sets the precedent by locking two beds `.OrderBy(id)` with the
reason in a comment. Then assert it with two transactions racing in opposite orders.

## CONC-2 — `EntitlementProvider._current` is the only mutable singleton field *(Low, latent)*

```csharp
public sealed class EntitlementProvider          // registered AddSingleton
{
    private EntitlementFile? _current;           // written by Load(), read by Current
}
```

Safe **today**: `Load()` is called once at startup, before any request. The class comment says
*"Admin upload of a fresh file lands in S5"* — the moment that ships, a request thread writes
`_current` while others read it, with no barrier. Reference assignment is atomic so no torn read
is possible, but a reader is not guaranteed to observe the new value promptly.

**Fix, when that feature lands, not before:** `volatile` or `Interlocked.Exchange`, or swap an
immutable provider instance.

## CONC-3 — the pool-versus-lock interaction is still unmeasured

`HmsTx` takes a pooled connection for the whole business action, and 17 sites hold row locks
inside it. At §14 volumes the plausible failure is not corruption but **connection-pool exhaustion
or a lock convoy** on 2 vCPU. That is LC-XCUT-11, open by decision under **ADR-0024** — unchanged
by this audit, and CONC-1 is the part of it worth closing without waiting for the ADR.

## CONC-4 — pooling is used correctly, but the two ceilings are inverted *(Medium-High, open)*

**Yes, it pools, and the pattern is right.** `HmsTx.RunAsync` does `new NpgsqlConnection(cs)` +
`OpenAsync` per business action. Npgsql pools by connection string, so that is a **pool checkout**,
not a TCP connect — the idiomatic ADO.NET pattern. Nothing needs changing there.

**What is wrong is that nobody sized it.** No `Pooling`, `Maximum Pool Size`, `Minimum Pool Size`
or `Timeout` appears in any config, `.env`, or code (checked across `*.json`, `*.cs`, `*.yml`).
So Npgsql 10.0.3 defaults apply — **Max Pool Size = 100 per process**. Meanwhile:

| Limit | Value |
|---|---|
| Npgsql max pool size (default, per app process) | **100** |
| Deployed `max_connections` (`deploy/compose.yml`) | **40** |
| less `superuser_reserved_connections` (default) | 3 |
| **usable by `hms_app`** (a plain LOGIN role) | **37**, shared with the backup container's `pg_dump` |

The client ceiling is nearly three times the server ceiling, so **the pool can never do its job**.
A correctly-sized pool applies backpressure: the 38th caller *waits* up to `Timeout` for a
connection to come back. With the ceilings inverted the pool cheerfully opens a 38th connection and
PostgreSQL rejects it — `53300 too many clients already`, a `PostgresException`, caught nowhere
(same gap as CONC-1's `40P01`) and therefore a **500 to the operator** instead of a short wait.

**Two things make it likelier than the raw numbers suggest.**

1. §14's N1 target is **40 concurrent operators**. `HmsTx` holds one connection for the *whole*
   business action, so concurrent actions ≈ concurrent connections — landing exactly on a 37-usable
   ceiling with no headroom for the backup loop, a migration on deploy, or an admin `psql`.
2. Connection hold time is bounded by **lock-wait** time, not query time, because the 17
   `FOR UPDATE` sites are taken *inside* the transaction. CONC-1's deadlocks and CONC-3's convoys
   therefore lengthen exactly the window in which connections are scarce. The three compound.

**Why it has never been seen.** The dev container `hms-dev-db` runs with the PostgreSQL default
**`max_connections=100`**; only `deploy/compose.yml` sets 40. The constrained ceiling exists solely
in the environment nobody load-tests — so every local run, every CI run and every script in
`eng/verify/` is incapable of reproducing it.

**Recommended:** set `Maximum Pool Size` explicitly in the connection string, below the server
budget — roughly 25–30 for the single `app` service, leaving room for backup, migration and
admin sessions — so the failure mode becomes "wait briefly" rather than "500". Then reconcile
`max_connections` against the real operator count. Both numbers are architecture decisions and
belong with **ADR-0024** (LC-XCUT-11), which is the ADR already open on §8 N1 behaviour.
