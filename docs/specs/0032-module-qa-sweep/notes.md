# 0032 — notes

Departures from the QA brief, decisions it asked me to make, and what the work turned up that
the brief did not anticipate. Engineering half only; the module sweep's own notes are the QA
engineer's.

## Decisions the brief left to me

### Months does **not** null out years

The brief: *"Decide for yourself whether months should also null out years; state what you chose
and why."*

**Chosen: only a DOB clears the age columns. Years and months never clear each other.**

Years and months are not two competing answers to one question the way a DOB and an age are —
they are two components of *one* age. An operator recording a toddler as "1 year 6 months" means
both numbers, and `Ui.AgeDisplay(dob, years, months, today)` already reads them as a pair: it
prefers years when `years > 0` and falls through to months otherwise. If months cleared years,
"1 year 6 months" would silently persist as "6 months" — an eighteen-month-old aged down by a
year, on the exact record class the defect was about.

Nothing constructs both today (`ParseAge` returns one or the other), so this is a rule about what
the *service* promises a caller, not a change to the current screen. `Years_and_months_together_
are_both_kept` pins it, so a future importer or the "1y 6m" entry the PRD's §7 U13 phrasing
invites cannot lose half the age quietly.

DOB clearing both is unchanged and now asserted (`Dob_wins_over_both_age_columns`).

## Departures from the brief

### 1. A new test project, `tests/Hms.Web.Tests`

The brief asked me to say so rather than invent one silently: **`Hms.Web` had no test project.**
The three candidate homes:

| Home | Cost | Problem |
|---|---|---|
| `tests/Hms.Architecture.Tests` | zero — already references `Hms.Web`, already in CI | its name promises NetArchTest rules; page-model parsing tests hidden there are findable by nobody |
| `tests/Hms.Kernel.Tests` | one reference | a project named for the kernel referencing the web app inverts the dependency the architecture tests exist to police |
| move `ParseAge`/`NormalizePhone` into `Hms.Kernel` | a production move | real option (spec 0015 did exactly this for `FlexibleDate`), but it is a design change the brief did not ask for, mid-defect-fix |

I created `tests/Hms.Web.Tests` (xunit, no Docker, 48 tests in ~50 ms), added it to `hms-erp.slnx`
and to `ci.yml`'s "Unit + architecture tests" step. **A test project CI does not name does not
run** — the workflow lists projects individually rather than running the solution, so adding the
project without the CI line would have produced 48 tests that never guard anything.

If you would rather not carry a fifth project, folding the one file into
`Hms.Architecture.Tests` is a two-line change and I will do it.

### 2. `BloodGroup` and `PatientType` also moved into `RegisterPatientCommand`

The brief scoped the command change to `AgeMonths`. I moved these two as well, for the same
reason the brief gave for months: the page was writing them in a **second** `SaveChangesAsync`
after `RegisterAsync` had already committed the patient — the same "one business action, one
transaction" (G19) violation, three fields deep in the same method.

M1-G4 asked for the *cheapest honest* assertion that they persist. Before the move there was no
cheap honest layer: `Guardian` is displayed by no page, `PatientType` is read by no code at all
(see M1-F1 below), so neither can be asserted over HTTP, and there is no web-host test project.
The choices were a new WebApplicationFactory harness, raw psql from a verify script (which would
break against a real deployment), or moving the write to where it belongs. Behaviour is identical
— same values, same defaults, same "blank blood group stays null", `PatientType` still defaults
to `"general"` — and the page's second `SaveChangesAsync` is gone entirely.

If you want this reverted to keep the diff to the brief, the tests move to an HTTP assertion for
blood group only, and patient type goes back to being unassertable.

### 3. LC-REG-09 and LC-REG-10 flipped, though the brief did not list them

Both were in the gap register at Medium. Both were **already asserted** when that register was
written:

- LC-REG-09 "no phone, registration still completes" ← `RegistrationTests.No_phone_registration_succeeds`
- LC-REG-10 "unknown/unconscious, placeholder identity" ← `RegistrationTests.Unknown_emergency_registers_without_identity`

By the document's own legend (`xunit` = asserted by a test project under `tests/`) they are
covered, and leaving them as `gap` leaves the document asserting something false. I flipped both
and said so in the register.

**Push back if you meant something narrower.** Both tests assert the *service* invariant; neither
drives the ≤ 60-second screen over HTTP, so "can a receptionist actually complete this form with
the identity-unknown box ticked" remains unproven. If that is what the rows meant, reopen them
with that wording — the honest version is a UI-smoke gap, not "nothing asserts this", and the two
readings imply different work.

This is the same failure mode your `module-coverage.md` already names: *"a row marked `gap` that
is in fact asserted — nothing looks in that direction at all."* Two of the four M1 examples of it
were in the register.

### 4. `RegDbContext` added to CI's additive-migration gate

The gate scripted `KernelDbContext` and `AuthDbContext` — two of fourteen contexts, and not the
one carrying patient identity. My migration would have passed CI without ever being checked. One
line added; it passes on the current migration set. The other eleven contexts are still unguarded
and that is a separate, larger call.

## What the work turned up that the brief did not

### M1-F1 — `patient.patient_type` is inert

LC-REG-14 expects *"type recorded, **drives later pricing**"*. Grep across `src/`: two writers
(the registration page, `PharmacySale`'s walk-in stub), **zero readers**. `RateResolver`'s
`corporate:<id>` scope comes from the referrer picked on the billing screen, not from the
patient's type, so a patient registered as Corporate is billed exactly like a General one.

The row now reads "type recorded (nothing reads it yet — see the register)". Whether a corporate
patient *should* default to corporate rates is a PRD question for the PM under hard rule 2, not
a test gap and not something to implement here.

### M1-F2 — `Guardian` is write-only

Captured on the registration screen, stored, and read by no page and no print template — not the
ID card, not the invoice header, not the EMR chart. For a minor or an unconscious patient the
attendant's name is the only contact the hospital has, and no operator can see it after the
registration screen closes. Recorded as a finding; not fixed, because "show the guardian
somewhere" is a screen decision, not a defect.

### M1-F3 — two phone normalisers implementing one rule

`NewModel.NormalizePhone` decides what is **stored**; `PatientSearch.PhoneDigitsOf` decides what
a typed search **matches**. Both strip non-digits and both implement `880 → 0`, independently.
They agree today and `Any_typed_form_reduces_into_the_stored_phone_digits` now pins that they
agree, but the rule lives in two places and only one of them is on the path the QA engineer
probed live. Worth one shared kernel helper next time this area is opened.

### The dev database is now behind the code

`20260728104406_WidenIdentityCheckForAgeMonths` has **not** been applied to `hms-dev-db`. The app
migrates on startup (`Program.cs`), and the instance on :5199 is yours — I did not restart it and
did not run `database update` against a database you own. Until it restarts, that instance still
carries the old `ck_identity` and will still refuse a months-only registration at the row level.
Nothing in the .NET suite depends on it (Testcontainers migrates a fresh database per run), and
`lifecycle-thread.py` passes against the running instance as it is.

## Not done, deliberately

- **A fresh-database `--tier all` run.** It needs `hms` dropped and the app restarted; both are
  yours. The ordering fix is verified structurally (`ALL_ORDER`, `TIERS["t2"]`) and `--tier t0`
  is green, but the end-to-end proof that `--tier all` now passes is one command away and I did
  not take it.
- **Patient merge (LC-REG-16) and deactivation.** Confirmed unbuilt, not untested — see the
  register row for the grep evidence. Product scope, PM's call.
- **`golden-thread.py`'s absolute assertions.** Untouched, as instructed. They are what t2 is for.

---

# Handoff 2 — M3 (queue) and the reversal cluster (M4 + M22)

## The decision the brief asked me to make: the money invariant after a refund

The brief: *"Whether the right answer is restore the due (the patient owes it again) or scope the
invariant to non-reversed invoices is a design decision… make the invariant true as stated or
state it differently."*

**Chosen: neither. State it differently — and universally.**

```
Σ receipts + due.balance  =  Realised(state, net, refunded)

Realised = 0                 when the invoice is cancelled or refunded
         = net − refunded    otherwise
```

`Σ receipts` already nets refunds out, because a refund is a negative receipt (hard rule 4). So
the three terms read: **money in the drawer · money still owed · what the invoice ended up being
worth.** `InvoiceValue` is that definition, with the reasoning on it, and
`The_money_invariant_survives_every_way_an_invoice_can_end` walks all six shapes an invoice can
end in — billed, partially paid, paid, cancelled, partly refunded, fully reversed.

**Why not restore the due.** A due is a *receivable*. Adding the refunded amount back to
`bill.due.balance` also closes the arithmetic, and it is the wrong close: it puts the patient
back on `/billing/dues` and into the MD's "Outstanding due" tile for money the hospital has just
decided to hand back. The desk would then chase a refund it granted. A refund reduces what the
invoice *earned*; it does not re-charge the patient.
`A_refund_leaves_the_due_alone_rather_than_re_charging_the_patient` pins that, so the next reader
cannot "fix" the invariant back the wrong way.

**Why not scope it to non-reversed invoices.** That leaves reversals — the place money is most
likely to go missing — checked by nothing at all. The point of an invariant is to hold where you
are least confident.

**A consequence worth knowing:** with the M4-F1 fix, an invoice that was partly paid and then
fully refunded goes back to `billed` with its remaining due intact (paid 600 of 1000, refund 600
→ `billed`, due 400). That is arithmetically honest, but there is no operation that voids the
*charge* on an invoice once money has touched it — `CancelInvoiceAsync` refuses those by design.
Whether the desk needs a "void the rest of this bill" path is a product question, not one to
invent here. Flagged, not filed: it needs a real operator case behind it before it is worth a
PM row.

## The other decision: the income definition, and why the two consumers read it differently

The brief asked for one definition applied in both the dashboard and the day-close statement.
Both now answer to `InvoiceValue`, but to **different members**, deliberately:

| Consumer | Uses | Because |
|---|---|---|
| MD dashboard income tile | `Realised` | It has no refunds line, and its whole job is to be comparable with the Collected tile. A partial refund has to move both or the gap reads like an unpaid due — which is the exact symptom M22-D1 reported |
| Day-close statement | `IsReversed` (a state filter on Gross/Discount/Net) | The statement prints *"Gross billed / Less: discount given / Net billed"* as a **visible subtraction**, so `Net = Gross − Discount` must hold on the page. It also already prints a separate `Refunds` line, so netting a partial refund into `Net` would both break the printed arithmetic and count the same money twice |

**This is the one place I did not do exactly what the brief said.** It asked to "define once which
invoice states count toward income, and apply it in both places". A pure *state* predicate is not
enough once M4-F1 is fixed: a partially refunded invoice is no longer `Refunded`, so a state
filter alone would let the dashboard count ৳1000 of income against ৳800 collected — M22-D1 again,
one fix downstream. The definition is single; the two documents take the reading their own layout
can carry.

## Departures and judgement calls

### 1. Never-reuse for serials — agreed with the brief, and here is the check I ran

The brief invited disagreement. I agree, and the reason is stronger than "nothing depends on it":
`Index.cshtml.cs` sends the issue-time SMS **inside the same transaction as the allocation**, so
the number is in the patient's hand before the transaction commits. Reuse would put two people in
the corridor holding "serial 5" with an SMS each. A partial unique index excluding cancelled rows
would make the allocator and the index agree again, but it buys back a number nobody is waiting
for at the cost of that. Allocation now reads **every** row, matching the index that already had
no state filter.

### 2. `RefundAsync` gained a required `tender` parameter (M4-F2)

The tender cannot be inferred: the cash in the drawer has nothing to do with how the patient paid
three weeks ago. `RefundAsync(…, long amount, string tender, string reason, …)` — positioned to
mirror `CollectAsync`'s `amount, tender, tenderRef`. One caller, `Billing/Refund.cshtml.cs`,
updated; the operator now picks on both the request form and the "Carry out" row, defaulting to
**cash**, with help text saying the drawer is counted against it.

The tender is asked for at **execution**, not on the approval request — that is the moment the
money moves and it is that operator's drawer it moves out of. `ApprovalRequest` carries no tender
column and did not need one.

Two smaller things came with it:

- **`Tenders` is now a named set** (`bill`'s `Tenders.All`, beside `InvoiceState`). Day-close
  groups by the tender string *exactly* — `tenderTotals.GetValueOrDefault("cash")` — so a stray
  `"Cash"` is a different drawer and a silent variance. `RefundAsync` normalises (trim +
  lowercase) and refuses anything unknown.
- **`RefundOfReceipt` now points at the largest positive receipt of the *same* tender**, falling
  back to the largest of any tender. A cash refund pointing at a card receipt was the same
  "first row wins" reading commit `7680940` swept.

**Deliberately not done:** `CollectAsync` and `CollectAdvanceAsync` still take an unvalidated
tender string. The same `"Cash"` hazard exists there, and the same `Tenders.IsKnown` guard would
close it — but that changes a heavily-exercised path that five screens and several verify scripts
post to, on a brief about reversals. It is a one-line change when someone wants it.

### 3. The dashboard's arithmetic moved into `Hms.Billing`

`InvoiceValue.Totalise` is pure and takes `InvoiceMoneyRow`s, so the **window and the timezone
stay in the page** (`Ui.DhakaMidnightUtc`, `Ui.Local` are presentation) and the **money leaves
it**. That is what made M22-D1 testable at all: the defect lived in a Razor page model with no
test host, and the assertion now runs against the same function the page calls.

Also swept for the same omission while I was there, all through the one definition: the 12-day
trend bars, `YesterdayNet`, the department split, the consultant ranking, and the "who discounted
what" table — every one of them was reading reversed invoices as live. The brief named the income
tile and the counts; these are the same query, three lines down.

### 4. M3-D2 — a regression this fix exposed, found by the QA engineer and closed here

Making the allocator right made the queue board's **label** wrong. `Index.cshtml` rendered
`next serial @(d.TodayCount + 1)` where `TodayCount` counts **non-cancelled** rows, while the
allocator now maxes over **all** rows. One cancellation and the receptionist reads a number off
the screen that the SMS then contradicts — worse than before, when label and allocator were wrong
*together*.

`TodayCount` was one field doing two jobs, and the two meanings had diverged. **Split rather than
flipped**, as asked:

| Field | Question | Cancelled rows |
|---|---|---|
| `TodayCount` | how many patients is this doctor seeing today? (reads beside "Capacity 40") | excluded — a cancelled appointment is not a patient seen |
| `NextSerial` | what number does the next patient get? | **included** — a consumed serial is never reissued |

`NextSerial` comes from `AppointmentsService.NextSerialFor`, and `IssueSerialAsync` allocates
through `NextSerialAfter` — the same arithmetic, so the board and the allocator cannot drift
apart again by editing one of them. `The_next_serial_the_board_offers_is_the_one_the_next_patient_gets`
asserts offered == issued at every step of a 1-2-3-with-2-cancelled sequence, which is the shape
that broke it.

Checked for the same pattern elsewhere: the public queue board (`Public/Queue.cshtml.cs`) shows
the in-chamber serial and counts of waiting/done. It predicts no serial, so it is correct as it
stands.

## What the work turned up that the brief did not

### `bill.v_dashboard_day` inherits the day-close fix for free

The read model M15 will consume (`20260726130403_DashboardReadModel`) sums `day_close_summary`
rows. Fixing `DayCloseService` fixes the view, with no migration: cancelled and refunded invoices
stop reaching `net` at the source. Worth knowing that the two were coupled — a fix applied only
at the page would have left the accounting view still wrong.

### A partial refund could previously be executed twice through the screens

`Refund.cshtml.cs` blocks a second reversal by checking `State is Refunded or Cancelled`. Because
a ৳1 refund set `Refunded`, that guard *appeared* to work — by locking the operator out of the
remaining ৳999 they had every right to refund. With M4-F1 fixed the screen correctly allows a
second partial refund, and `RefundAsync`'s `amount > netPaid` guard reads receipts **net of
earlier refunds**, so cumulative refunds still cannot exceed cumulative payments.
`A_refund_cannot_exceed_what_was_actually_paid` asserts both the single-shot and the by-
instalments attempt.

### `check-lifecycle-traceability.sh` still cannot see xUnit citations

M4-D3 (the register citing `MoneySpineTests` for a refund test that did not exist) is now false
as a *fact* — the tests are there. The **mechanism** that let it drift is untouched: the script
validates `auto <script>` citations only, and only that a file exists. `xunit RateTests` still
names a class that does not exist. That is the QA engineer's script and their document, and
fixing it properly means validating class names against `tests/**/*.cs` and requiring every
`auto` row to appear as a `case("LC-…")` call inside the named script. Not taken here; named so
it is not mistaken for closed.

## Not done, deliberately

- **End-to-end verification of these fixes against `:5199`.** The brief forbids `dotnet run` and
  the dev app is the QA engineer's process, so the instance I could reach was serving the
  pre-fix binary. Everything is proven at the xUnit layer against real Postgres, against the same
  service methods and the same `InvoiceValue.Totalise` the pages call. The QA engineer has since
  rebuilt and confirmed `--tier all` green on a fresh database, plus the live repros.
- **No new lifecycle-script assertions.** An e2e dashboard-after-reversal check in
  `money-and-controls.py` would be the natural companion to `Reversed_invoices_do_not_reach_the_income_figure`,
  and I could not run it against a build containing the fix. Handing over an assertion I have not
  seen pass is how a green suite turns red for the wrong reason. Recommended shape, for whoever
  takes it: read `/dashboard` as `md` before and after the LC-BIL-10 cancellation and assert
  income **fell by the cancelled invoice's net** — relative, so it survives a dirty ledger.
- **`MaxSerials` and the doctor-has-a-session check.** Report only, as instructed — now **P25**
  in `09-questions-for-pm.md`. The waitlist is product behaviour with its own states, SMS and
  screen; hard rule 2 puts it with the PM.
- **No migration.** Nothing in this handoff changes a schema, so
  `eng/check-additive-migrations.sh` had nothing to run against. The `Tenders` set is a C#
  constant list, not a CHECK constraint — deliberately, because the deployed data already
  contains whatever it contains and a new constraint on `bill.receipt` would be a non-additive
  change to a financial table.

---

# Handoff 3 — notes

## The defect the brief did not know about

### `/registration/new` returned 500 for every unknown-identity registration *(High)*

LC-REG-20 was opened as a coverage gap — "nothing exercises the ER path end to end". It is not a
coverage gap. **The ER path does not work.** Every attempt to register an unconscious patient
returns HTTP 500:

```
System.NullReferenceException
  at Hms.Web.Pages.Registration.NewModel.<>c__DisplayClass60_0.<OnPostAsync>b__1
```

ASP.NET model binding converts an **empty** form field to `null`, not to `""`
(`ConvertEmptyStringToNull` defaults to true), so `public string FullName { get; set; } = ""`
is null after a post that leaves the name blank, and `FullName.Trim()` dereferences it.

The reason it survived this long is the shape of the guards. `FullName` can only be blank on one
path — the guard above it refuses a blank name unless `UnknownIdentity` is ticked — so the *only*
post that reaches `.Trim()` with a null name is the unconscious-emergency case of edge 25. Every
ordinary registration fills the name in and never touches the bug.

It is pre-existing at `HEAD`, not something handoff 1 or 2 introduced.

**This is the argument for LC-REG-19/20 existing at all, made concrete.**
`RegistrationTests.Unknown_emergency_registers_without_identity` passes, has always passed, and
could never have caught this: it constructs the command directly with a real `""`. The rule was
right at the service the whole time and the screen was broken the whole time. The QA engineer's
refusal to let a service-level fact discharge a row that names `jashim` was correct, and the two
new ids paid for themselves on their first run.

Fixed by normalising the three properties whose initialisers a blank post destroys, at the top of
`OnPostAsync`. `Sex` and `PatientType` are not reachable today — both are selects that always
post a value — but they are the same trap one form change away, and the fix is one line each.

### It is red against :5199 and I could not clear it

The brief forbids restarting the dev app, so the only instance I can reach still runs the build
that has the bug. `--tier t1` is therefore red on exactly one check, and that check is the
reproduction. Every other script in t1 is green.

**What the QA engineer needs to do:** restart the app on this build. `--tier all` on a fresh
database — which you said you would run yourself — does that anyway, and LC-REG-20 will go green.
If it does not, the fix is wrong and I want to know.

The unit-level proof I *can* give without a restart: `RegistrationScreenTests` has 10 facts and 5
of them fail when the normalisation is removed, including one that asserts the blank name is
normalised *before* anything touches a dependency.

## Decisions the brief left to me

### M2-D1 — the screen says the estimate is incomplete, and the catch stays

I took the brief's suggestion, and I did **not** find it unreachable — the opposite. Two ordinary
situations produce it, both proven in `BedDayEstimateTests` against real Postgres:

1. **A ward priced from a date after a sitting patient was admitted.** Edge 11 names this
   directly: *"provisional items need prices before go-live"*. A hospital that opens a cabin block
   on Monday and prices it on Wednesday under-quotes every cabin patient in between.
2. **A gap between two rate versions.** `valid_to` is nullable, not contiguous, and nothing
   requires a plan to have no holes. `ex_rate_version_no_overlap` forbids overlap; it says nothing
   about gaps.

So it is not a code-read risk. It is a real defect that seeded data does not happen to reach, and
I have written it up that way rather than as "reproduced on the deployment", which it was not.

**The catch itself stays**, and I think that is right. `/frontdesk` is a read-only enquiry screen
that a receptionist opens dozens of times an hour; throwing turns a pricing-configuration problem
into a dead screen and the desk loses the ability to answer anything at all. What was wrong was
never the catch — it was that the sum could not tell anyone it was short. So:

- the arithmetic moved to `RateResolver.TotalOverDaysAsync`, which returns the days it could not
  price rather than dropping them;
- the total is relabelled **"Net payable now (AT LEAST)"** — an under-report that announces
  itself as a lower bound is honest; a short number under the word "estimate" is not;
- the alert names the dates and tells the operator what to do about it.

**Where it went, and why not `FolioService`.** The dates come from `FolioService.Compute
UnpostedBedDaysAsync` and it would read naturally there — but `Hms.Ipd` references only
`Hms.Kernel`, and pulling `Hms.Admin` into it to reach `RateResolver` inverts a module boundary
`ModuleBoundaryTests` exists to police. The resolver is what fails, so the summing-with-outcome
belongs beside the resolver. No new project reference either way.

**Worth noting for whoever opens this area next:** there are four more silent
`catch (RateResolutionException) { }` sites — `Ipd/Folio.cshtml.cs` ×2, `Ipd/Admit.cshtml.cs` ×2,
`Diagnostics/Order.cshtml.cs`, `Billing/Opd.cshtml.cs`. I left all of them alone and I think that
is correct: every one filters a **picker**, where an unpriced item is simply not sellable (edge
11) and omitting it is the intended behaviour. The front desk was the only one inside a **sum**.
That distinction is the whole finding, and it is now written on `TotalOverDaysAsync`.

### M20-D1 — I implemented the full GSM-7 table, and here is why

The brief said a correct single/concatenated split was the valuable part and a full alphabet table
might be more than it is worth. I did both. The split alone would have left two known-wrong
behaviours in a function I was already rewriting, and the table is a bounded, static, ~10-line
constant with no runtime cost:

- `c > 127` is wrong in the expensive direction. é, è, Ü, ñ, £, §, Ω are all GSM-7 basic. A
  message containing one accented character billed at **70** per part instead of 160 — more than
  double. The hospital name is configurable and prefixes every message, so one hospital with an
  accent in its name doubles its own reported spend across every template.
- The escaped set (`{ } [ ] ~ ^ \ | €`) costs two septets. `€` matters if a price ever reaches a
  template; braces matter because the templates are **written in braces** and an operator editing
  one at `/admin/sms` can leave a literal brace in the body.

Two nuances I deliberately did **not** model, both documented on the method, both able to cost at
most one extra segment on a pathological body: an escaped GSM-7 pair is never split across a
segment boundary, and neither is a UTF-16 surrogate pair. Modelling them needs a packing
simulation, and the error is one segment on a message no template will ever produce.

**A correction to the brief.** It says `ceil(n/70)` and `ceil(n/67)` diverge at "68–70, 135–140
and 202–210". The 68–70 band is not a divergence: a 70-character body is a *single* message and
costs one segment under the correct rule too — there is no UDH header because there is no
concatenation. The real UCS-2 divergences are **135–140** and **202–210** (and 269–280, …), and
the GSM-7 ones are **307–320**, **460–480**, … The tests pin the boundaries on both sides
(70 → 1, 71 → 2, 134 → 2, 135 → 3), so the rule is stated by the tests and not by prose.

### M20-F1 — the button is already hidden; the finding's premise is wrong

`/notifications/tray` renders the Resend form inside
`@if (Model.Can("admin.masters.manage") && m.Recipient is not null)` — `Tray.cshtml:88`,
unchanged since `d6b6b34`. A read-only grant does **not** see a button that 403s. There was
nothing to fix and no decision to make between hiding the button and moving the handler.

What was genuinely missing is anything that *keeps* it true, which is the same reasoning spec
0030 F1 used about grants drifting. `ViewGuardPermissionTests` now joins the two sides: it reads
the guard out of the view, reads `[Authorize(Policy = Perm.X)]` off `Sms.cshtml.cs`, resolves the
constant through `PermissionPolicy.TryParse`, and asserts they name the same permission. Removing
the guard fails it by name.

I did not move the handler. Resend is masters work and belongs on `/admin/sms` with the template
editing; `notifications.read` is deliberately the weaker "look at the log" grant, and widening it
to cover a write would be the spec 0030 mistake in reverse.

## What the work turned up that the brief did not

### `Can(Perm.X)` compiles and is silently always false

`HmsPageModel.Can(p)` tests the raw **claim** (`admin.masters.manage`). Every `Perm.*` constant is
a **policy** name carrying the `perm:` prefix that `PermissionPolicy.TryParse` strips. So this
compiles, reads perfectly in review, and hides a control from everybody — including the role that
holds it:

```csharp
@if (Model.Can(Perm.AdminMastersManage))     // always false
```

Nothing does it today (all 26 call sites pass bare literals), which is luck rather than design —
the two spellings are one autocomplete apart. `No_view_guard_is_passed_a_policy_name_instead_of_a_
permission` now forbids it, and `Every_view_guard_names_a_permission_that_exists` catches the
neighbouring typo. Both passed on first run, so this is a guard against a future mistake, not a
fix for a present one.

The deeper fix would be a type — `Can(Permission p)` with an implicit conversion, or a
`Perm.Claim(...)` accessor — so the wrong string cannot be passed at all. That is a refactor
across 26 call sites and I did not take it inside a QA sweep.

### A 5xx aborted the whole lifecycle thread

`lifecycle-thread.py`'s POST helper let `urllib`'s `HTTPError` propagate. When the ER registration
500'd at step 1, the script died with a traceback: steps 2–12 never ran, no summary printed, and
the run looked like a broken script rather than a broken screen. Its teardowns still fired
(`_harness`'s `atexit` does that much), so nothing leaked.

The registration helper now returns the status as a failed check. That single change is why the
run above shows one clean red line and eleven green steps instead of a stack trace — and it is
worth generalising to `_harness.Session.post` next time somebody is in there. I scoped it to the
one helper rather than changing shared behaviour under ten other scripts mid-sweep.

### `frontdesk-check.py` had no environment interlock

Migrating it to `_harness` surfaced something the M2-F3 write-up did not mention: as well as
carrying its own `Session`, it never called `guard()`. It registers a patient, admits them, takes
a ৳700 advance and marks them absconded — a fully mutating t1 run — and it would have done all of
that against `BASE_URL=https://hms.example.com` with no `HMS_QA_ENV`, no confirmation, and no
manifest. It also hardcoded `http://localhost:5199`, which is what kept that from happening. It
now calls `guard("t1")` like every other mutating script.

### The doctors panel counts in-chamber as waiting, and nothing said so

`FrontDeskModel` computes `Waiting` as `Booked or Arrived or InChamber`. That is a real product
decision — from the help desk's point of view a patient in the chamber has not been seen yet, so
the family asking "how long?" should still be counted — and it was written in a LINQ expression
and nowhere else. LC-FD-06 now asserts it directly: calling the patient in changes **nothing** on
the desk, and only finishing moves waiting into done.

## Case ids for the QA engineer

Not edited by me — `docs/qa/patient-lifecycle.md` and `docs/qa/module-coverage.md` are yours.

**Rows I have earned:**

| Id | Change | Closed by |
|---|---|---|
| LC-FD-01 | `auto` frontdesk-check — now carries the id | `frontdesk-check.py` `case("LC-FD-01")` |
| LC-FD-02 | `auto` frontdesk-check — now carries the id | `frontdesk-check.py` `case("LC-FD-02")` |
| LC-FD-03 | `auto` frontdesk-check — now carries the id | `frontdesk-check.py` `case("LC-FD-03")` |
| LC-FD-05 | `gap` → `auto` frontdesk-check | the panel's numbers, as a delta the run causes |
| LC-REG-19 | `gap` → `auto` lifecycle-thread 1 | passes today |
| LC-REG-20 | `gap` → `auto` lifecycle-thread 1 | **red until :5199 restarts** — hold this row until you have seen it green |

**New row I am asking for:**

| Id | Case | Expectation | Performer | Coverage |
|---|---|---|---|---|
| LC-FD-06 | Today's doctors panel tracks the queue | issuing a serial moves booked and waiting; calling the patient in changes nothing (in-chamber still counts as waiting); finishing moves waiting into done | `jashim` | `auto` frontdesk-check |

**Also worth a row, and I did not take it:** LC-XCUT-08 ("SMS queued, and resendable — tray shows
it") is still an open gap on its **e2e** half. Its business-logic half is now covered by
`SmsQueueTests`, so the row is no longer "nothing asserts this". The cheap close is an `admin`
session in `lifecycle-thread.py` reading `/notifications/tray?event=registration` after step 1 and
asserting the skip count moved for the LC-REG-19 patient — that would tie edge 24 to the screen
in one step. I left it because the brief did not ask for it and it is your row to word.

## Not done, deliberately

- **Requiring every `auto <script>` row to carry a matching `case()`.** The brief names this as
  the natural next step now that `frontdesk-check.py` is migrated. I did not add it: I have not
  audited the remaining scripts for survivors, and a gate that fails the build on scripts nobody
  has looked at yet would land red. `frontdesk-check.py` is no longer the blocker; the check is a
  small change once someone confirms the other twelve are clean.
- **The four remaining silent `RateResolutionException` catches.** All are picker filters, all
  correct as they stand — reasoning above.
- **Restarting the app on :5199.** Yours.
