# 0032 — tasks

Appended as the sweep proceeds. Module sections are the QA engineer's; the M1 remediation and
QA-H1 rows below are engineering's.

## QA-H1 — `--tier all` red by construction *(harness)*

- [x] `lifecycle-suite.py`: `--tier all` now runs `ALL_ORDER = ["t0", "t2", "t1"]`, so t2 meets
      the fresh ledger it documents (`eng/verify/lifecycle-suite.py`)
- [x] t2's internal order preserved — `golden-thread.py` before `discount-and-dues.py`
- [x] explicit single-tier runs unchanged
- [x] ordering comment rewritten; `docs/qa/README.md` updated in both places it describes a run
- [x] `golden-thread.py`'s absolute assertions left alone — they are the point of t2
- [x] verified: `--tier t0` green; `ALL_ORDER` and `TIERS["t2"]` read back from the module
- [x] **taken by QA, 2026-07-28:** `hms` dropped and recreated, app restarted on the new build,
      then `python3 eng/verify/lifecycle-suite.py --tier all` → **SUITE GREEN — 14 scripts,
      0 failed · roles exercised 12/12 · ward census 13 free beds, unchanged.** The same command
      on the same database was RED before the fix, on the single check `income lands on the
      dashboard`. QA-H1 is closed end to end.

## M1-D1 — a patient aged in months cannot be registered *(High)*

- [x] `short? AgeMonths` on `RegisterPatientCommand`; registration stays one transaction (G19)
- [x] `RegistrationService` guard: DOB **or** years **or** months **or** unknown-identity
- [x] `AgeAsOf` stamped for a months-only age exactly as for a years-only age
- [x] `Patient.AgeMonths` set inside `RegisterAsync` — no longer dead code
- [x] DOB wins over **both** age columns; months and years do not clear each other (rationale in
      `notes.md` and in the code comment)
- [x] `ck_identity` widened to include `age_months is not null`
      (`20260728104406_WidenIdentityCheckForAgeMonths`)
- [x] `bash eng/check-additive-migrations.sh` passes on `dotnet ef migrations script --idempotent`
      output for `RegDbContext`
- [x] `New.cshtml.cs`: months passed through; unreachable `p.AgeMonths` line and the page's second
      `SaveChangesAsync` removed
- [x] observed **red before, green after** — see `notes.md`

## M1-G1 — `ParseAge` untested *(Medium)*

- [x] `tests/Hms.Web.Tests/RegistrationInputTests.cs` — 14-row theory over §7 U13's six shapes,
      empty/whitespace/null, garbage, and surrounding whitespace; asserts the whole tuple
      including `Estimated`
- [x] day-first (`12/03/1980` = 12 March) asserted separately — a month-first reading would
      misdate a population silently
- [x] new project `tests/Hms.Web.Tests` (Hms.Web had none) + `hms-erp.slnx` + `ci.yml` — see
      `notes.md`, this is the one thing the brief asked to be told about

## M1-G2 — `NormalizePhone` untested, messy-phone search unasserted *(Medium)*

- [x] unit tests for `NormalizePhone` (13 rows) — every form the QA engineer probed live
- [x] unit tests for `PatientSearch.PhoneDigitsOf` — the function actually on the search path
- [x] a test pinning the two normalisers against each other without a database
- [x] `lifecycle-thread.py` step 1: existing plain-digits and tail checks folded into one
      `case("LC-REG-05", …)` table with `+880`, `880`, spaces and misplaced dashes — extended,
      not duplicated
- [x] verified green against localhost:5199 (all 7 forms return the patient)

## M1-G3 — "DOB wins" unasserted *(Low)*

- [x] `RegistrationTests.Dob_wins_over_both_age_columns` — DOB set, both age columns null,
      `AgeAsOf` null
- [x] `RegistrationTests.Age_in_months_registers_and_persists` — the red-then-green test
- [x] `RegistrationTests.Years_and_months_together_are_both_kept`
- [x] all three re-read the row from a fresh connection, not the tracked entity

## M1-G4 — guardian, blood group, patient type never asserted *(Low)*

- [x] `RegistrationTests.Guardian_blood_group_and_patient_type_persist`
- [x] `RegistrationTests.Patient_type_defaults_to_general`
- [x] blood group and patient type moved into `RegisterPatientCommand` to make the write
      assertable in the cheapest layer — a departure, recorded in `notes.md`

## Documentation

- [x] `docs/qa/patient-lifecycle.md`: LC-REG-05 → `auto` lifecycle-thread 1; LC-REG-09/10/11/12/14
      → `xunit` RegistrationTests; **LC-REG-18** appended for the months path; no id renumbered
- [x] gap register: three closed rows removed, LC-REG-16 annotated as unbuilt product with the
      grep evidence, closure paragraph added
- [x] coverage summary: 170 cases / 150 covered (88%) / 20 gaps (12%), taken from
      `check-lifecycle-traceability.sh --stats`
- [x] `docs/qa/module-coverage.md`: M1 resolution + QA-H1 resolution appended
- [x] `ci.yml`: `tests/Hms.Web.Tests` added; `RegDbContext` added to the additive-migration gate

## Verification

- [x] `dotnet build hms-erp.slnx -c Debug` — 0 warnings, 0 errors
- [x] `dotnet test hms-erp.slnx -c Debug` — **209 passed** (was 156): kernel 22, architecture 26,
      integration 112 (was 107), print 1, **web 48 (new)**
- [x] `dotnet restore hms-erp.slnx --locked-mode` — the new project's lock file is in place
- [x] `bash eng/check-lifecycle-traceability.sh --stats` — OK
- [x] `python3 eng/verify/lifecycle-thread.py` — PASSED, exit 0
- [x] `python3 eng/verify/lifecycle-suite.py --tier t0` — GREEN
- [x] nothing committed, pushed or deployed

---

# Handoff 2 — M3 queue + the reversal cluster (M4 + M22)

## M3-D1 — one cancellation bricks a doctor's queue *(High)*

- [x] `IssueSerialAsync` allocates over **all** rows for the doctor-day; the `State != Cancelled`
      filter is gone, so the max query and the unique index `(doctor_id, on_date, serial_no)`
      finally agree
- [x] never-reuse chosen over a partial index — reasoning on the method and in `notes.md`
- [x] `Index.cshtml.cs`: the comment claiming cancel "frees it for reissue" corrected; the no-show
      toast no longer promises "the serial is freed"
- [x] **observed red before, green after** — `Could not allocate a serial — the queue is moving
      fast, try again.`, the exact message the QA repro produced
- [x] `AppointmentQueueTests` (new, real Postgres): cancel→reissue (LC-QUE-05), no-show→reissue,
      twelve serials through a cancel-every-other-one morning staying distinct and gapless
- [x] the retry loop still does its real job — two counters issuing at once get 1 and 2
      (LC-QUE-07), not an error
- [x] `AdvanceAsync`'s state guard asserted (M3-G1, edge 28)

## M3-D2 — the queue label contradicted the allocator *(High, regression this fix exposed)*

- [x] `DoctorCard.TodayCount` **split** into `TodayCount` (patients today, cancelled excluded —
      reads beside "Capacity 40") and `NextSerial` (the number the next patient gets, cancelled
      **included**); neither meaning flipped
- [x] `Index.cshtml` dropdown reads `@d.NextSerial` instead of `@(d.TodayCount + 1)`
- [x] both sides now come from one place — `AppointmentsService.NextSerialFor` for the board,
      `NextSerialAfter` for the allocator, same arithmetic
- [x] **observed red before, green after** — old label offered 2 where the allocator issued 3
- [x] `The_next_serial_the_board_offers_is_the_one_the_next_patient_gets` — offered == issued at
      every step of 1-2-3-with-2-cancelled
- [x] checked `Public/Queue.cshtml.cs` for the same pattern — it predicts no serial, correct as is

## M3-R1 — US3.1's capacity AC is enforced nowhere *(report only, as instructed)*

- [x] **not implemented.** `MaxSerials` unenforced and no doctor-has-a-session check, appended as
      **P25** in `docs/architecture/09-questions-for-pm.md`, in that file's row style
- [x] P25 asks the three decisions the waitlist actually needs (refuse/overbook/waitlist · what a
      waitlist *is* · supervisor override) and carries a recommended default so nothing blocks

## M4-D1 — the money invariant after a refund *(High)*

- [x] **design decision made and written where the next reader will find it** —
      `src/Modules/Billing/Hms.Billing/InvoiceValue.cs`, with the rejected alternative and why
- [x] invariant restated **universally**: `Σ receipts + due = Realised(state, net, refunded)`
- [x] the due is **not** restored on refund — a receivable the hospital would then chase
- [x] `The_money_invariant_survives_every_way_an_invoice_can_end` — all six end states
- [x] `A_refund_leaves_the_due_alone_rather_than_re_charging_the_patient` pins the decision
- [x] **observed red before, green after** — `Expected: 0 / Actual: 800` on the partial-refund row

## M4-D2 — the two reversal paths had no test *(High)*

- [x] in `MoneySpineTests`, where the lifecycle document already claims they live
- [x] a refund is a negative receipt pointing at the original, never a delete (LC-BIL-09)
- [x] a refund cannot exceed what was actually paid — single-shot **and** by instalments
- [x] an invoice nobody has paid is cancelled, not refunded
- [x] an invoice with money on it cannot be cancelled
- [x] both reversals need a reason
- [x] a refund needs an open counter session
- [x] a cancelled invoice keeps its number and its row, and cannot be cancelled twice (LC-BIL-10)

## M4-D4 + M22-D1 — reversed invoices counted as income *(High, one fix)*

- [x] one definition, `InvoiceValue`, consumed by both — reasoning for why each reads a
      *different member* is on the class and in `notes.md`
- [x] `DayCloseService`: Gross/Discount/Net over non-reversed invoices only; `invoiceIds` still
      spans **all** session invoices so `dueCollected`'s "other days' invoices" test stays right
- [x] `Dashboard.cshtml.cs`: income, discount, invoice count, patient count, the 12-day trend,
      `YesterdayNet`, the department split, the consultant ranking and the discount table all
      read the same definition
- [x] `bill.v_dashboard_day` inherits the fix with no migration — it sums `day_close_summary`
- [x] **observed red before, green after** — day-close `Expected: 700 / Actual: 1600`;
      dashboard `Expected: 1500 / Actual: 3150`
- [x] `The_day_close_statement_leaves_out_the_invoices_that_were_reversed`, and asserts the
      printed subtraction `Net = Gross − Discount` still holds
- [x] `Reversed_invoices_do_not_reach_the_income_figure` — **involves actual reversals**, so it
      can see what `golden-thread.py:206`'s fresh-ledger ৳550 cannot; also asserts the two tiles
      agree (income = collected + outstanding)
- [x] `golden-thread.py`'s ৳550 assertion untouched and still passes — a fresh ledger has no
      reversals, so realised == net

## M4-F1 — a partial refund marked the whole invoice `Refunded` *(Medium)*

- [x] `Refunded` now means **finished**: everything taken has gone back *and* nothing is owed
- [x] otherwise the state follows the due/collection rule — `paid` / `partially_paid` / `billed`
- [x] `A_partial_refund_leaves_the_invoice_live_and_worth_what_is_left`
- [x] `An_invoice_is_only_refunded_once_everything_taken_has_gone_back` — paid 600 of 1000 then
      refunded 600 goes back to `billed` with its 400 due intact
- [x] **observed red before, green after** — `Expected: "paid" / Actual: "refunded"`

## M4-F2 — a refund inherited the tender of the largest receipt *(Medium)*

- [x] `tender` is now a **required parameter** of `RefundAsync`, positioned to mirror
      `CollectAsync`; the one caller is updated
- [x] `Tenders` named set added beside `InvoiceState`; refund normalises (trim + lowercase) and
      refuses anything unknown — day-close matches the tender string exactly
- [x] `RefundOfReceipt` points at the largest positive receipt of the **same tender**, falling
      back to the largest of any
- [x] `Billing/Refund.cshtml`: a tender select on the request form and on the "Carry out" row,
      hidden for a cancellation (which moves no money), defaulting to cash
- [x] the tender is asked at **execution**, not on the approval request — that is when the money
      moves, and whose drawer it moves from
- [x] `A_cash_refund_is_booked_as_cash_however_the_patient_paid` — ৳500 cash + ৳1000 card, ৳200
      cash back: expected cash lands on float + 500 − 200, and the refund points at the cash
      receipt not the bigger card one
- [x] **observed red before, green after** — `Expected: "cash" / Actual: "card"`
- [x] `A_refund_in_a_tender_nobody_recognises_is_refused`, and `"  CASH "` normalises

## Verification

- [x] `dotnet build hms-erp.slnx -c Debug` — **0 warnings, 0 errors**
- [x] `dotnet test hms-erp.slnx -c Debug` — **230 passed** (was 209): kernel 22, architecture 26,
      integration **133** (was 112), print 1, web 48
- [x] `dotnet restore hms-erp.slnx --locked-mode` — clean after adding the `Hms.Appointments`
      reference to `tests/Hms.Integration.Tests`
- [x] `bash eng/check-lifecycle-traceability.sh --stats` — OK, 172 cases / 154 covered / 18 gaps
- [x] `eng/check-additive-migrations.sh` — **no migration in this handoff**, nothing to check
- [x] red-before-green observed for **every** defect fixed, not only the three required
- [x] QA engineer independently confirmed: 229 tests green at that point, `--tier all` GREEN on a
      fresh database (14 scripts, 12/12 roles), and the M3-D1 / M4-D1 / M22-D1 live repros
- [x] nothing committed, pushed or deployed

---

# Handoff 3 — M2 Front Desk, M20 SMS, and the registration screen path

## M2-F1 — LC-FD-05 was a false green *(Medium)*

- [x] `frontdesk-check.py` now reads the **Beds free now** panel itself — `beds_panel()` parses
      `{CLASS: (free, total)}` out of the card it is named after, not the dropdown on `/ipd/admit`
- [x] the assertion is a **delta the run causes**: admitting into a class takes exactly one bed
      off that class's *Free*, leaves *Total* alone, moves no other class, and releasing the bed
      puts it back — immune to whatever else is in the ward
- [x] the bed's class is read off the same `/ipd/admit` option the admission consumes, so the
      assertion names the class it actually took (the ward NAME carries brackets of its own —
      "General Ward (Female)" — so the class comes from the last parenthesised group)
- [x] invariants asserted too: the panel renders ≥ 1 class row, and every row reads
      `0 <= free <= total` with the ward not empty
- [x] **proven not to be a false green in a new place** — see `notes.md`: against the live page,
      an empty `<tbody>` under the correct heading parses to `{}` and fails; all-zero numbers
      fail; a panel that never moves fails

## M2-F2 — the doctors-today panel had no case and no assertion *(Medium)*

- [x] **new case id requested: `LC-FD-06`** — wording in `notes.md`, for the QA engineer to add
- [x] `frontdesk-check.py` issues a serial and asserts the panel moves: booked +1, waiting +1,
      done unchanged
- [x] walks the operator's real path (booked → in chamber → done), which also pins that
      **in-chamber still counts as waiting** — a rule in `FrontDeskModel` nothing stated
- [x] completing asserts done +1, waiting −1, booked unchanged
- [x] the serial is found by **patient**, not by position in the queue

## M2-D1 — a silent catch on the money path *(Medium — characterised as a code-read risk)*

- [x] `RateResolver.TotalOverDaysAsync` added beside the resolver that throws: it prices a run of
      days and **names** the ones it could not price. `Hms.Ipd` cannot see `Hms.Admin`, so the
      arithmetic belongs next to `RateResolver`, not `FolioService`
- [x] `FrontDesk.cshtml.cs`: the `catch (RateResolutionException) { }` inside the accrual sum is
      gone; `UnpricedBedDays` / `EstimateIncomplete` exposed
- [x] `FrontDesk.cshtml`: the total is relabelled **"Net payable now (AT LEAST)"**, the accrued
      row carries an "N day(s) unpriced" pill, and an `alert bad` names the dates and tells the
      operator to quote a minimum and get the ward priced
- [x] the screen still never throws — a read-only enquiry page must not 500 on a rate gap
- [x] `BedDayEstimateTests` (7 facts, real Postgres) — **it is reachable**: a rate plan starting
      after a sitting patient was admitted (edge 11 go-live), and a gap between two versions
- [x] **observed red before, green after** — 3 of the 7 fail when the day is dropped silently
- [x] `frontdesk-check.py` asserts the happy path does **not** warn, so it cannot cry wolf

## M2-F3 — `frontdesk-check.py` bypassed the shared harness *(Low, harness)*

- [x] its private `Session` and `check` deleted; it now imports `Session, case, check, fixture,
      guard, on_exit, record, release_bed, report, tag` from `_harness`
- [x] every assertion carries a `case()` id — LC-FD-01/02/03/05/06
- [x] `guard("t1")` added — it mutated a deployment with no environment interlock at all
- [x] its two logins now count toward "roles exercised"; it exits through `report()`
- [x] `BASE_URL` is honoured (it hardcoded `http://localhost:5199`)

## M20 — the module had no tests at all

- [x] `SmsQueueTests` (10 facts, real Postgres) — **G19**: a rolled-back registration leaves no
      SMS behind, and a committed one leaves exactly one; **edge 24**: no phone / empty / blank
      is a recorded `skipped_no_phone`, not a failure; **§5 M20 [M]**: resend re-queues unchanged
      and never mutates the original, and a skip resends as a skip; live posture stops at
      `queued` while simulation stamps `sent`
- [x] `tests/Hms.Integration.Tests` gained the `Hms.Notifications` project reference

## M20-D1 — the billable-segment count was understated *(Medium)*

- [x] `SegmentsFor` now splits **single-message** (160 / 70) from **concatenated** (153 / 67)
- [x] full GSM 03.38 basic + escape tables — `c > 127` is gone; é and Ü are one septet, `{ } [ ]
      ~ ^ \ | €` cost two, Bangla forces UCS-2 (rationale for going this far in `notes.md`)
- [x] `SmsSegmentTests` + `SmsOptionsTests` (46 facts, no database)
- [x] **observed red before, green after** — 22 of the 46 fail against the old arithmetic
- [x] the shipped templates are pinned at one segment, so the latency that hid this is now visible

## M20-F1 — the tray's Resend button *(Low)* — **the finding's premise was wrong**

- [x] the button is **already** gated: `Tray.cshtml:88` is
      `@if (Model.Can("admin.masters.manage") && m.Recipient is not null)`. Unchanged since
      `d6b6b34`. Nothing needed fixing; what was missing was anything keeping it right
- [x] `ViewGuardPermissionTests` (3 facts) joins the view guard to the handler's policy by
      resolving `Perm.*` through `PermissionPolicy.TryParse`, so the two cannot part company
- [x] **observed red before, green after** — removing the guard fails the test by name
- [x] two general rules added: every `Can("…")` names a permission that exists, and no `Can()` is
      passed a `perm:`-prefixed policy name (see `notes.md` — a silent-always-false trap)

## LC-REG-19 / LC-REG-20 — the registration screen path

- [x] both added to `lifecycle-thread.py` step 1 with `_harness.case`
- [x] LC-REG-19: no-phone registration over HTTP — the redirect carries a UHID, no validation
      error is shown, the patient is in the directory, and the phone cell holds no digits
- [x] LC-REG-20: unknown-identity over HTTP — and the negative, that the same blank form without
      the box ticked is still refused, so the box is proven to be what unlocks it
- [x] **LC-REG-20 found a live High defect — see `M2/M20` note below and `notes.md`**

## Found by LC-REG-20 — the ER could not register a patient *(High, defect)*

- [x] `/registration/new` returned **HTTP 500** for every unknown-identity registration.
      Model binding converts an empty field to `null`, so `FullName`'s `= ""` initialiser does
      not survive a blank post and `FullName.Trim()` threw `NullReferenceException`
- [x] pre-existing at `HEAD` — not introduced by handoff 1 or 2
- [x] fixed by normalising `FullName` / `Sex` / `PatientType` at the top of `OnPostAsync`
- [x] `RegistrationScreenTests` (10 facts) — **observed red before, green after**, 5 fail
      without the fix
- [x] `lifecycle-thread.py`'s registration POST no longer raises on a 5xx: it records the status
      as a failed check instead of a traceback that aborted every later case in the thread

## Verification

- [x] `dotnet build hms-erp.slnx -c Debug` — **0 warnings, 0 errors**
- [x] `dotnet test hms-erp.slnx -c Debug` — **306 passed** (was 230): kernel 22,
      architecture **29** (was 26), integration **150** (was 133), print 1, web **104** (was 48)
- [x] `dotnet restore hms-erp.slnx --locked-mode` — clean after the `Hms.Notifications` reference
- [x] `bash eng/check-lifecycle-traceability.sh --stats` — OK, 174 cases / 157 covered / 17 gaps
- [x] `eng/check-additive-migrations.sh` — **no migration in this handoff**, nothing to check
- [x] red-before-green observed for every defect fixed and for the two false-green risks
- [ ] `python3 eng/verify/lifecycle-suite.py --tier t1` — **9 of 10 scripts green**;
      `lifecycle-thread.py` red on LC-REG-20 alone, because the instance on :5199 predates the
      fix and I may not restart it. That redness IS the reproduction. See `notes.md`
- [x] `python3 eng/verify/frontdesk-check.py` — green, 6 cases, 0 failed, ward census restored
- [x] nothing committed, pushed or deployed
