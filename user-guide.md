# HMS ERP — Operator User Guide

**For the computer operators of the hospital.** One section per role — find your role in
[Part 4](#part-4--role-guides) and keep that section at your desk. Parts 1–3 are for
everyone and take ten minutes to read.

> Written 2026-07-29 against the deployed build (spec 0033). English mirrors the screens
> exactly: when this guide says press **Save & print card**, that is the text on the button.

---

## Part 1 — About this system

This software runs the hospital's daily work: patient registration, doctor serials, OPD and
emergency billing, doctor's prescriptions, indoor admissions (IPD), operations (OT), lab
tests (LIS), X-ray/ultrasound reporting, the pharmacy, SMS to patients, and the owner's
dashboard. It runs in a normal web browser — there is nothing to install at the counter.

**Modules working today (14):** Patient Registration & ID · Front Desk / Help Desk ·
Appointment & Queue · OPD & Emergency Billing · Prescription & EMR · IPD Management ·
OT Management · Investigation / Test Orders · LIS (Laboratory) · Radiology & Imaging ·
Pharmacy · SMS / Notification · Administration, Security & Audit · Management Dashboard.

**Not built yet (8):** Inventory (general store) · Blood Bank · Canteen ·
**Accounts & Finance** · HR & Payroll · Consultant Payment · Corporate / Panel Billing ·
Marketing & Referral. What "no Accounts module" means for daily money handling is
explained plainly in [Part 3](#part-3--how-the-money-works).

### Demo logins (practice accounts)

The demo hospital is **Altushi General Hospital**. Every practice account uses the password
**`Demo#1234`**.

| Username | Name | Role |
|---|---|---|
| `jashim` | Jashim Uddin | Receptionist |
| `rasel` | Rasel Ahmed | Billing Operator |
| `shahid` | Shahid Alam | Billing Supervisor |
| `parvin` | Parvin Akter | Pharmacist |
| `ripon` | Ripon Das | Lab Technologist |
| `farhana` | Dr. Farhana Rahman | Pathologist |
| `moinul` | Moinul Haque | Radiology Technician |
| `chowdhury` | Dr. A. K. Chowdhury | OPD Consultant |
| `nasrin` | Nasrin Sultana | Nurse |
| `shaheen` | Shaheen Akhter | OT In-charge |
| `admin` | System Admin | Admin |
| `md` | Dr. Chairman | MD |

> ⚠️ **Before the first real patient (go-live):** these demo accounts are deactivated and
> every remaining account gets its own private password (deploy RUNBOOK §9). If `Demo#1234`
> still signs in on a live hospital, stop and tell the administrator. Never share passwords —
> every receipt, discount and report permanently carries the name of the account that made it.

---

## Part 2 — Getting started (every role)

### Signing in and out

1. Open the browser. Go to the hospital's HMS address and the sign-in page appears (`/login`).
2. Type your username and password, press **Enter**.
3. After sign-in you land on your **home screen**: your name, and up to **6 large buttons** —
   your daily jobs, one click each, with a one-line description under every button.
4. Sign out with the button at the top right when you leave the seat.

Things to know:

- **Wrong password 5 times locks the account for 5 minutes.** The message tells you this;
  just wait and try again.
- **After 15 minutes of no activity you are signed out** automatically. Unsaved typing is
  lost — save your work before walking away.
- If you open a page and see **"no access"** (`/denied`), your role does not include that
  screen. That is a permission decision, not a fault. Ask the Admin if you believe your role
  should have it. Role changes take effect within about 5 minutes — no need to sign out.

### How every screen behaves

- **The left menu shows only your screens.** If a menu item this guide mentions is not in
  your menu, your role does not have it.
- **Search, don't type.** Every patient, test, medicine or doctor box is a search box: type
  2–3 letters (or digits of a phone number, or scan the patient's card) and pick from the
  list. You never type a full name of anything that already exists.
- **The grey strip at the top shows keyboard shortcuts** for the current screen (for
  example *F2 New patient · F3 Item search · F10 Payment*). Billing, lab entry and pharmacy
  work fully by keyboard: **Tab** moves, **Enter** advances.
- **After every action a message appears** at the edge of the screen — green means done,
  red explains what to fix. Read it; it often tells you the next step.
- **Printing** is the **Print** button on the screen itself — what you see is what prints.
  Money receipts offer A4 (file copy) and thermal (counter roll) sizes.
- **Nothing is ever deleted.** Wrong entries are corrected by a reversal, an amendment or a
  new version — always with a reason, always with your name. This protects you: the record
  shows exactly what you did and why.

---

## Part 3 — How the money works

*This part answers the owner's question directly: when a patient does a pathology test, sees
a doctor, buys medicine, and stays in a ward or cabin — do the bills reach the accounts
automatically?*

### The short answer

**Every charge lands on the patient's bill by itself or by one deliberate entry, and every
taka automatically reaches the counter's day-close statement, the income reports and the
MD dashboard. There is no separate Accounts module yet** (it is on the roadmap). Today the
accounts desk works from the **printed Day-Close Statement** each counter produces at
shift end, plus the **Collection & Income Reports** screen. Nobody re-enters any figure —
the statement and reports are built from the same receipts the counters record.

### Outdoor patients (OPD, tests, pharmacy)

Each counter keeps **one running bill per patient per day**. Charges collect on it and the
**Save** at the counter turns them into one invoice. Note: the OPD counter, the diagnostics
counter and the pharmacy each produce **their own invoice** — an outdoor patient who visits
all three gets three receipts that day, not one combined bill.

| Charge | Automatic or typed? |
|---|---|
| Doctor's consultation fee | **Typed** — the billing operator adds it to the bill (e.g. Consultation ৳700) |
| Tests the doctor orders on the prescription screen | **Automatic** — they appear at the billing counter by themselves, under "charges raised elsewhere"; the operator never re-types them |
| Tests ordered directly at the diagnostics counter | **Automatic** — selecting the test bills it in the same save |
| Medicines at the pharmacy | **Typed** — the pharmacist builds the sale; prices always come from the batch, never typed |

**The one rule everyone must know: the lab starts only when the bill is fully paid.**
A part-paid test order prints no sample labels and appears on no lab worklist. The moment
the balance is collected (at **Due Collection**), the order releases and labels print. The
same applies to X-ray/ultrasound. There is deliberately no way to hand over a report while
money is owed — the delivery screen only offers **Collect due first**.

### Indoor patients (ward / cabin) — the folio

An admitted patient has a **folio**: one running account for the whole stay. This is where
everything converges automatically:

| Charge on the folio | Automatic or typed? |
|---|---|
| Admission fee and package price | **Automatic** at admission |
| Bed / cabin rent, per day | **Automatic** — the system counts the days itself |
| Lab tests and imaging ordered for the inpatient | **Automatic** at order (no payment gate — the money is already on the folio) |
| Operation charge + surgeon, anaesthetist and assistant fees | **Automatic** the moment the OT case is marked **Complete** — an operation cannot be completed without being billed |
| Medicines the ward requests (indent) | Nurse **types the request**; the pharmacy issue prices and posts it **automatically**, batch by batch |
| Doctor's ward visit, oxygen, nursing, other services | **Typed** — the ward picks the service and quantity; the price is always the system's, never the operator's |
| Service charge % (on packages) | **Automatic** at settlement |

At discharge the folio is settled **once**: all charges, minus advances already deposited,
equals one settlement invoice. If the advance was larger than the bill, the screen says
exactly how much to return from the drawer. After settlement the folio locks — a late
charge needs a supervisor's approval to enter.

One caution: a purchase the patient's family makes at the **pharmacy counter** during the
stay is a separate outdoor sale — it does **not** move onto the folio (ward medicine must
go through an indent). The discharge screen lists every unpaid outdoor bill the patient has
so the desk can collect them, but they stay separate invoices.

### Where the money story ends each day

1. Every taka in or out passes through an operator's **counter session** (Part 4 explains
   opening one).
2. At shift end the operator counts the drawer and runs **Counter Day-Close**. Any
   difference between counted and expected cash is **recorded against the operator's name**
   — it never blocks closing.
3. The close produces the printable **Day-Close Statement**: gross, discounts, net, dues
   created and collected, refunds, tender totals, float, counted, variance. **This paper is
   the hand-off to the accounts desk.**
4. The **Collection & Income Reports** screen and the **MD Dashboard** are built from the
   same records — collection by counter and by operator, department income, referrer
   business, discount register, due ageing. Cancelled and refunded invoices are excluded
   from income automatically.

When the Accounts & Finance module is built, it will read these same day-close records —
the figures will not change, they will simply also post to ledgers. Until then: **the
day-close statement is the accounts interface, and it is produced automatically.**

---

## Part 4 — Role guides

Find your role. Each guide lists your menu, your shift, and when to call a supervisor.

---

### 4.1 Receptionist — the front desk

*Demo login `jashim`. You register patients, print ID cards, run the doctor serials, admit
patients to beds, and answer "is a cabin free?" and "how much so far?" questions.*

**Your menu:** New Patient · Patient Directory · Help Desk · Serials / Queue · Ward Board ·
New Admission · Admissions & Census · IPD Reports.

**Registering a patient — under a minute (`/registration/new`)**

1. Press **F2** anywhere, or open **New Patient**.
2. Type the full name. **Tab.**
3. Sex, then **age or date of birth in the same box** — `45`, `45y`, `8 months` and
   `12/03/1980` all work.
4. Phone (any written form is fine — the system tidies it), then any of: guardian, area,
   blood group.
5. **Save & print card** (or **Alt+S**). The ID card prints; a welcome SMS queues by itself.

- **Possible duplicate?** The screen lists matching patients instead of saving. If it is
  truly a new person, press the red **register anyway**. If it is the same person, open the
  existing record — never create a second card for one patient.
- **Unconscious, identity unknown (emergency):** tick **identity unknown** and save with no
  name and no age. Fill the details in later. Nothing blocks care.
- **Lost card:** Patient Directory → open the patient → print the card again. A reprint is
  stamped **re-issue** and counted — that is normal and expected.

**Finding a patient (`/registration`)** — one box searches name, ID and phone together;
scanning a card lands here too. The list also shows each patient's unpaid balance.

**Doctor serials (`/appointments`)**

1. Open **Serials / Queue**. Each doctor card shows room, today's count vs capacity, and
   **"next free is N"** — quote that number to the patient.
2. Pick the patient, pick the doctor, press **Issue serial**. The patient receives the
   serial by SMS immediately.
3. Through the day, advance the queue: **Called in** when the patient enters the chamber,
   then **Consultation finished**. Use **Cancel serial** or **No-show** with their buttons.

A serial number, once issued, is never given to anyone else that day — the patient was
already told it by SMS. Cancelling does not free the number.

**Beds and admissions**

- **Ward Board (`/ipd/board`)** — every bed as a coloured tile: free, occupied, reserved,
  cleaning, out of service. When housekeeping finishes, press **Bed is free again**.
- **New Admission (`/ipd/admit`)** — patient → consultant → provisional diagnosis → pick a
  free bed (its daily rate is shown) → optional package → **Admit** (or tick **Reserve
  only**). Admission fee and package post to the folio by themselves. The cash advance is
  taken afterwards by the billing counter on the folio screen.
- **Admissions & Census (`/ipd/admissions`)** — tabs for In-house, Reservations, Block
  list and Discharged. Confirm or cancel reservations here. A patient with a bad debt can
  be **blocked** (reason required, supervisor approves) — a blocked patient cannot be
  admitted or billed anywhere until released.

**Answering enquiries — Help Desk (`/frontdesk`)** — today's doctors (booked / waiting /
done, room numbers), free beds by class, and — after finding a patient — their current
admission with a **live bill estimate** (charges so far + bed days accrued − advance).
This screen is read-only; you can quote from it but never change anything on it. If it says
the estimate is "AT LEAST", a price is missing — tell the Admin.

**Call the supervisor when:** a patient must be admitted despite a due-block (release needs
approval), or two records exist for one patient (merging is not built yet — leave a note in
the record and inform the Admin).

---

### 4.2 Billing Operator — the money counter

*Demo login `rasel`. You bill OPD and tests, collect payments and dues, handle refunds,
settle discharges, and close your counter every shift. Money moves only through you.*

**Your menu:** Patient Directory · Help Desk · OPD Invoice · Due Collection · Refund &
Cancel · Counter Session · Counter Day-Close · Collection Reports · Diagnostic Order ·
Report Delivery · Ward Board · Admissions & Census · Certificates · IPD Reports.

**Start of shift — open your counter (`/billing/session`)**

1. Open **Counter Session**. Pick your counter (Front Desk 1, Diagnostics, Emergency, or
   Pharmacy). A counter someone else holds shows "in use by …" and cannot be picked.
2. Count the change money physically in the drawer and type it as the **opening float**.
3. Confirm. You land on the OPD Invoice screen, ready to bill.

No money screen works without an open session — each one will say **"Open your counter
first"** with a link. On the **Emergency Counter**, every bill automatically becomes an ER
bill; you never pick "emergency" anywhere.

**Billing OPD (`/billing/opd`)**

1. Find the patient (**F3** jumps to item search, **F10** to payment — the whole screen
   works by keyboard).
2. Add services to the bill — consultation (e.g. ৳700), dressing, ECG… Anything the doctor
   already ordered from the chamber is **waiting under the cart automatically** — never
   re-type it.
3. Discount, if any, **needs a reason**. Up to ৳200 applies immediately; above that the
   bill **holds** until the Billing Supervisor approves in their inbox.
4. Take payment — one tender, or **Split** for cash + card/bKash/Nagad with a reference.
   You cannot take more than the payable; change is your drawer's business.
5. **Save** → the money receipt prints (A4 or thermal).

The screen refuses a **due-blocked** patient, and warns if the patient is **currently
admitted** — an admitted patient's charges belong on the folio, or the discharge desk will
never see them.

**Billing tests (`/diagnostics/order`)**

Same shape: patient → tests (price, sample and turnaround shown) → **referrer** (defaults
to *Self / walk-in* — pick the referring doctor if there is one) → discount → payment →
**Save**. The slip shows a **promised delivery time** computed from the slowest test —
that is the promise the patient carries home.

**Full payment is what starts the lab.** Fully paid → barcode sample labels print now.
Part-paid → the order waits, invisible to the lab, until the balance is collected.

**Collecting dues (`/billing/dues`)** — search by name, ID or invoice number; type the
amount and tender; **Collect**. If the payment completes a held test order, the toast tells
you the lab has been released and labels can print — do print them.

**Refund & Cancel (`/billing/refund`)** — the screen decides which applies:

- Nothing paid yet → **Cancel** (reason required).
- Money taken → **Refund**: amount up to what was actually paid, a reason, and **how the
  money goes back** (cash/card/…) — say what actually leaves the drawer. Above your limit
  it waits for approval; once approved it appears in your list with an **Execute** button —
  money moves only when you press it, at your open counter.

An invoice can never be reversed twice, and a refunded pharmacy sale returns the stock to
its exact batches by itself.

**Handing over reports (`/diagnostics/delivery`)** — verified reports ready for pickup. If
anything is owed, the only button is **Collect due first**. Otherwise type who is
collecting and press **Deliver** — the exact report version handed out is logged.

**Discharges — settlement (steps 3–4 of `/ipd/discharge/...`)**

When the ward sends a patient for discharge, your part is money:

1. **Prepare settlement draft** — the system catches up bed days, adds any service charge,
   and freezes the folio.
2. Check the figure. **Confirm settlement** — discount (with reason, approval rules as
   usual), advances apply by themselves. If the advance exceeds the bill, the screen says
   **"return ৳X excess advance from the drawer"** — do exactly that.
3. A late charge after the draft? **Reopen draft**, let the ward post it, prepare again.
4. The desk releasing the patient sees every other unpaid bill in the hospital listed with
   **Collect…** links — collect them now; releasing a patient who still owes needs a typed
   reason that is kept forever.

You also issue **Certificates (`/ipd/certificates`)** — discharge, death and birth
certificates against an admission. Once issued the wording is frozen; **Reprint** is
counted and audited.

**End of shift — Day-Close (`/billing/day-close`)**

1. Count the physical drawer.
2. Open **Counter Day-Close**. The screen already knows the expected cash (float + cash
   taken) and the card/mobile totals.
3. Type the **counted cash** → **Close**. Short or over, it still closes — the variance is
   recorded with your name. Never "adjust" a count to make it match; the honest figure is
   the safe figure.
4. The **Day-Close Statement** appears — **print it and hand it to the accounts desk.**
   That paper is the day's accounting.

**Call the supervisor when:** a discount or refund is above your limit (it routes itself —
call so they look at their inbox), the drawer variance is large, or yesterday's session was
left open (see [Troubleshooting](#troubleshooting)).

---

### 4.3 Billing Supervisor — approvals and oversight

*Demo login `shahid`. You decide what the counters may not decide alone. You do not hold a
drawer — you cannot open a counter session, deliberately.*

**Your menu:** Patient Directory · Help Desk · OPD Invoice · Due Collection · Refund &
Cancel · Counter Day-Close · Collection Reports · Ward Board · Admissions & Census ·
IPD Reports · **Approvals Inbox**.

**The Approvals Inbox (`/admin/approvals`)** is your main screen. Every held action queues
here: discounts above ৳200, refunds, invoice reopens, folio late-posts, patient blocks and
releases, stock write-offs, purchase orders. Each row shows what, for whom, how much, the
reason, who asked and when. **Approve** or **Reject**, optionally with a note. Requests
you leave longer than 10 minutes escalate to the MD. If two supervisors click at once, one
wins and the other is told plainly — nothing is decided twice.

Approving moves no money by itself — the counter that asked still executes at its open
session. Your decision and your name are on the record either way.

**Also yours:** the **Collection Reports** screen (by counter, by operator, discount
register with every reason, due ageing) and each counter's day-close variance — a pattern
of small shortages against one name is exactly what that report exists to show.

---

### 4.4 Pharmacist — the medicine counter and store

*Demo login `parvin`. You sell medicines, issue ward requests, receive purchases, and watch
expiry. The system picks the batch; you never choose which strip leaves first.*

**Your menu:** Patient Directory · Due Collection · Refund & Cancel · Counter Session ·
Counter Day-Close · Collection Reports · Pharmacy Sale · Stock & Expiry · Purchase Orders ·
Products & Companies · Indoor Issue Queue · Outlet Transfers · Suppliers & Ledger ·
Pharmacy Reports · Pharmacy Dashboard.

**Start of shift:** open the **Pharmacy Counter** at **Counter Session**, with your float.

> ℹ️ After opening the counter you currently land on a "no access" page (it targets the OPD
> billing screen, which pharmacists don't have). This is a known quirk — just click
> **Pharmacy Sale** in the menu and carry on.

**Selling (`/pharmacy/pos`)**

1. Search by **brand or generic** — 3 letters is enough. The shelf shows stock on hand,
   price, and a near-expiry marker. **Expired or finished items simply do not appear.**
2. Add lines, set quantities.
3. Walk-in cash sale needs no patient. But: leaving any balance unpaid (**credit**), or
   giving a **discount** (reason required), needs a named patient; a **staff sale** must
   name the staff member.
4. Take payment (split tenders fine) → **Save** → receipt prints.

The system allocates stock **earliest-expiry-first** at save; the receipt may show two
batch lines for one medicine — that is correct. If the buyer is an **admitted patient**,
the screen warns you: ward medicine must go through a **ward indent** so it lands on the
folio — a counter sale here will not reach their discharge bill.

**Ward requests (`/pharmacy/indents`)** — the nurses' requisitions queue here, each line
showing requested vs available. Pick the outlet, press **Issue**. The system takes
earliest-expiry stock, prices each batch, and posts it to the patient's folio by itself. A
discharge-time return restocks exactly the batches that left.

**Receiving stock (`/pharmacy/purchase`)** — raise a purchase order (supplier, outlet,
lines); it moves Requested → Approved → Ordered → Received with the **Advance** button
(approval is the supervisor's). Receiving is per line: **batch number off the carton**
(mandatory — it is the recall identity), **expiry**, quantity, cost, MRP. The supplier's
payable rises by itself — see **Suppliers & Ledger**, where you also record payments.

**Stock & Expiry (`/pharmacy/stock`)** — tabs for All / Near expiry / Expired /
Quarantined. Expired or damaged stock: **Quarantine** (reason) → **Request write-off**
(supervisor approves) → **Dispose**. Return to supplier with its button. **Stock count**
lives here too: **Start count** → type counted quantities → post; a variance becomes an
approval request, never a silent adjustment.

**End of shift:** count the drawer → **Counter Day-Close** → print the statement for
accounts, same as any counter. Watch **Pharmacy Dashboard** daily: near-expiry, short
items (reorder list writes itself), supplier payable.

**Call the supervisor when:** a write-off, purchase order, or above-limit discount waits in
the approvals inbox.

---

### 4.5 Lab Technologist — samples and results

*Demo login `ripon`. You collect samples, receive them in the lab, and enter results. You
cannot verify — that separation is deliberate and protects you.*

**Your menu:** Patient Directory · Work Board · Result Entry.

**The Work Board (`/lis/board`)** is your day, six columns left to right:
**Awaiting collection → Collected → Received at lab → Result entered → Verified →
Delivered.** Scan a tube's barcode to advance its card; clicking the button is the
fallback. Only **paid** orders appear at all — if a patient is standing in front of you and
their order is not on the board, it is unpaid ("Held — due ৳X"); send them to the billing
counter, not to the lab bench.

1. **Collect** — the button names the sample ("Collect EDTA blood →"). Label the tube with
   its printed barcode.
2. **Receive at lab** when the tube physically arrives.
3. **Bad sample?** Press **Reject & re-collect**, pick the reason (haemolysed,
   insufficient, clotted, wrong container, label unreadable). A fresh collection with a
   **new label** is created automatically — rejection is never a dead end and never an
   argument.
4. Each card shows elapsed time against the **promised delivery time** — a red **Late**
   pill means the front desk's promise to the patient is at risk. Work those first.

**Result Entry (`/lis/results`)** — pick the order from the worklist; type each value and
press **Enter** to jump to the next. The normal range and the High/Low flag are applied by
the system for that patient's age and sex — you never look ranges up. Descriptive tests get
a text box. **Saving is not releasing** — nothing reaches a patient until the Pathologist
verifies. A value already saved cannot be overwritten here; corrections go through the
Pathologist's amendment, with both versions kept.

---

### 4.6 Pathologist — verification and amendments

*Demo login `farhana`. Your signature releases every report; nothing reaches a patient
without it.*

**Your menu:** Patient Directory · Work Board · Result Entry · Verification Queue ·
Amend a Report · Modality Worklist.

**Verification Queue (`/lis/verify`)**

1. Open an order: every value with its flag, the range used, who entered it and when.
2. Pick the **reporting consultant** whose signature block will print (only consultants
   registered for those departments are offered).
3. **Verify** — this e-signs the report and sends the patient's **"report ready" SMS** in
   the same action. The report is now printable at delivery.

A report not yet verified prints only with a **provisional watermark** and no signature —
that is how a draft in someone's hand stays recognisable forever.

**Amending a released report (`/lis/amend`)** — pick the released test, correct the
values, give a reason (it prints on the corrected report). The correction may need
approval; until decided, the released report stands. The new print says **v2 —
supersedes v1**, and v1 remains readable forever. There is no other way to change a
released report — by design.

**Radiology reporting:** you also write and sign imaging reports — see the flow in
[4.7](#47-radiology-technician--imaging); your part is opening the study from the
worklist, filling the template (Findings, Impression), and **Sign**.

---

### 4.7 Radiology Technician — imaging

*Demo login `moinul`. You run the machines and mark studies done; a consultant writes and
signs the report.*

**Your menu:** Patient Directory · Modality Worklist · Machines & Mapping.

1. **Modality Worklist (`/radiology/worklist`)** — one tab per machine (X-ray, USG, ECG…).
   Only **paid** orders appear — same rule as the lab; an absent study means an unpaid
   bill, not a lost order.
2. Perform the study, then press **Done** — record film size, film count, any note.
3. The study moves to the consultant, who writes the report from a template and signs it.
   An unsigned report prints only as **provisional**.

**Machines & Mapping (`/radiology/modalities`)** — add a machine, and map **every imaging
test to exactly one machine**. The worklist banner counts unmapped tests: **an unmapped
test appears on no worklist at all**, so clear that count whenever the Admin adds a new
test.

---

### 4.8 OPD Consultant — the chamber

*Demo login `chowdhury`. Your queue is the paid list; your prescription is three minutes
with templates; the tests you order bill themselves.*

**Your menu:** Consultation Queue · Patient Record · My Templates · Patient Directory ·
Diagnostic Order · Report Delivery · Work Board · Ward Board · Admissions & Census ·
IPD Reports · OT Board · Operation Register.

**The Consultation Queue (`/emr/queue`)** shows today's patients **who have paid** — a
patient appears the moment the counter bills the visit. Each row carries the vitals the
nurse already took. Press **Consult**.

**The consultation screen (`/emr/consult/...`)** — everything on one page:

1. Patient header, today's vitals, the last five visits and recent lab results — no
   hunting.
2. Write (or let a **template** write): complaint, examination, diagnosis, advice,
   follow-up date.
3. **Drug rows** — pick from the pharmacy's real products (your **favourites** surface
   first), dose, frequency, duration, instruction.
4. **Order tests** right here — **they are billed automatically and wait at the counter;
   the patient just walks there and pays.** Nothing for you to write on paper.
5. **Save** keeps a draft. **Finalise** signs it and opens the printable prescription on
   your pad layout.

**A signed prescription cannot be edited** — medico-legally, ever. The correction path:
open the prescription → **write a correction** — a new note that names the old one; both
stay on record, linked.

**My Templates (`/emr/templates`)** — your own templates and favourite drugs, visible only
to you. Ten minutes invested here is the "3-minute prescription" the hospital was promised.

**Patient Record (`/emr/history`)** — any patient's visits, prescriptions, verified
results and admissions in one read-only place.

---

### 4.9 Nurse / Ward In-charge — the ward

*Demo login `nasrin`. You take vitals, post ward services to the folio, request medicines,
and keep the charts. You never touch a price — the system prices everything.*

**Your menu:** Pre-checkup Vitals · Nursing Charts · Consultation Queue · Patient Record ·
Patient Directory · Help Desk · Ward Board · Admissions & Census · Ward Indents ·
IPD Reports.

**OPD side — Pre-checkup Vitals (`/emr/vitals`)**: one row per waiting patient; enter BP,
pulse, temperature, weight, SpO₂ → save. The doctor sees them on the consult screen.

**Ward side — the folio (`/ipd/folio/...`)**, opened from the Ward Board or the census:

- **Post a service** — pick the service (oxygen, nursing day, consultant visit — with the
  visiting doctor's name), quantity, done. The folio shows every line **with the name of
  whoever posted it**.
- **Raise medicine indent** — list the drugs and quantities the patient needs; the request
  goes to the pharmacy's issue queue, and the issued medicine prices itself onto the folio.
  Track every request in **Ward Indents (`/ipd/indents`)**.
- **Investigation indent** — order lab tests/imaging for the inpatient: one action posts
  the charge **and** raises the samples for the lab.
- A **locked folio** (after settlement) takes no posting without a supervisor-approved
  late-post — the screen walks you through requesting it.

**Nursing Charts (`/emr/charts`)** — per admitted patient:

- **Medication chart (MAR):** schedule a dose (drug, dose, route, time — blank time means
  now), then record each administration with its outcome; a missed dose takes a reason.
- **Diabetic chart:** glucose reading, timing, insulin units and route.
- **Receive note** at shift handover: received from, condition, belongings.

Every chart row carries your name and cannot be edited afterwards — corrections are new
entries. Chart entries are clinical records; they do not create charges.

---

### 4.10 OT In-charge — the theatre

*Demo login `shaheen`. You schedule operations, run the case through its states, and the
completion bills itself — every fee, in the same click.*

**Your menu:** OT Board · Schedule an Operation · Operation Register · Theatres ·
Patient Directory · Help Desk · Ward Board · Admissions & Census · IPD Reports ·
Pharmacy Reports · Pharmacy Dashboard.

**Scheduling (`/ot/schedule`)**

1. Pick the patient — the list marks each as **(indoor)** (charges will go to the folio)
   or **(day case)** (charges go to the day's counter bill).
2. Theatre, operation from the price list, date, start time, expected duration.
3. The team: **surgeon** (required), anaesthetist and assistant (optional) — these names
   decide whose fees post at completion.
4. **A time clash is refused outright** — same theatre or same surgeon cannot be booked
   twice. Pick another slot; there is no override.

**Running the case (`/ot/case/...`)** — press the buttons in order as reality happens:
**Patient sent for → Start → Complete** (findings, procedure performed, anaesthesia type).
**Complete posts every charge in the same action** — operation fee, surgeon, anaesthetist,
assistant, theatre charge; the message tells you the amount posted. An operation cannot be
completed without being billed. **Postpone** and **Cancel** both require a reason; a
cancelled case posts nothing and frees its slot. Consumables used in theatre are issued
from the store on this screen and post to the bill as you go — a running "billed so far"
figure keeps you honest.

**Operation Register (`/ot/register`)** — the chronological legal register: every case,
team, state and amount. Read-only, printable. **Theatres (`/ot/theatres`)** — add a
theatre or retire one (old cases keep its name forever).

---

### 4.11 Admin — masters, users, prices

*Demo login `admin`. You are the one technical seat: accounts, prices, catalogues,
templates, audit. Deliberately, you cannot bill, register, or open patient files.*

**Your menu:** Approvals Inbox · Users & Roles · Price List & Catalog · Bulk Import ·
Doctors & Referrers · Report Templates · SMS Templates · Audit Viewer · SMS Tray.

**Users & Roles (`/admin/users`)** — create accounts (username, name, password ≥ 8
characters, **role is mandatory** — the role is what decides their screens).
**Deactivate, never delete** — receipts keep the person's name forever; deactivation also
ends their live session. **Reset password** signs the holder out; the reset is audited,
the password is not. The **role × permission matrix** is live: a change reaches the
person's menus and permissions within about 5 minutes, mid-session.

**Price List & Catalog (`/admin/masters`)** — services and tests with their prices.
**A price change is a new version from a chosen date** (blank = tomorrow). **A past date
is refused** — an old invoice must always reprint at its old price; that rule is absolute.
Every item shows its price history with authors. An item priced ৳0 is **provisional** and
does not appear at any counter — the "unpriced items" count on this screen is your go-live
checklist; keep it at zero.

**Bulk Import (`/admin/import`)** — paste or upload the price-list CSV (`code, name, dept,
sample_types, tat_minutes, price, valid_from`; a starter template is downloadable). Valid
rows commit as one audited batch; bad rows come back to you individually — a 900-line list
with four typos loads 896 items.

**Doctors & Referrers (`/admin/people`)** — three masters: **doctors** (room, serial
capacity — adding one enables serials immediately), **referrers** (code, kind, commission %
— captured on every diagnostic order), **reporting consultants** (degrees, BMDC number,
departments — these are the signature blocks on lab reports).

**Report Templates (`/admin/templates`)** — each test's parameters, units and normal
ranges (general and male/female). Editing a template **never changes stored results** —
every result keeps the range it was judged by.

**SMS Templates (`/admin/sms`)** — the exact words patients receive per event
(registration, serial, report ready) with on/off switches. An unknown `{placeholder}` is
refused so a patient never receives literal braces. **Resend** re-queues the exact words
already logged. The **SMS Tray (`/notifications/tray`)** shows every message sent, skipped
(no phone) or simulated — until an SMS gateway is procured, messages are stamped
*simulated* and the tray is the proof of what would have gone.

**Audit Viewer (`/admin/audit`)** — who changed what, when, before/after. Searchable.
Read-only at the database level — even you cannot edit history.

**Approvals Inbox** — you can decide held requests, same rules as the Billing Supervisor.

---

### 4.12 MD — the owner's screen

*Demo login `md`. One screen answers "is money leaking?" in ten seconds. You are the only
role that sees it.*

**Your menu:** MD Dashboard · Approvals Inbox · Audit Viewer · Help Desk · Ward Board ·
Admissions & Census · IPD Reports · Pharmacy Reports · Pharmacy Dashboard.

**The MD Dashboard (`/dashboard`)** shows today, live: income · collected · outstanding
due · discounts · patients · invoices · counter variance · open counters · pending
approvals · lab tests in progress. Below: a 12-day income trend (quiet days shown, not
hidden), department split, **counter variances by operator name**, the **discount register**
(each discount with who gave it, who approved it, and the reason), a **consultant ranking**
by patients and income, and a one-line digest of yesterday. Cancelled and refunded
invoices are already excluded from income — the figure is realised money, not paper.

**Your levers:** approvals that escalate past the supervisor (10 minutes unanswered)
arrive in your **Approvals Inbox**; the **Audit Viewer** answers any "who did this?"; the
reports screens drill into anything that looks wrong on the dashboard.

---

## Part 5 — Reference

### Approval matrix (who can allow what)

| Request | Allowed without approval | First approver | If unanswered 10 min |
|---|---|---|---|
| Discount | Billing Operator, up to ৳200 | Billing Supervisor | MD |
| Refund | — | Billing Supervisor | MD |
| Reopen a settled invoice/folio draft | — | Billing Supervisor | — |
| Post to a locked folio | — | Billing Supervisor | — |
| Patient due-block / release | — | Billing Supervisor | MD |
| Stock write-off | — | Billing Supervisor | MD |
| Purchase order | — | Billing Supervisor | MD |
| Carry-close of yesterday's session | — | Billing Supervisor *(no screen yet — see Troubleshooting)* | — |

Decisions happen in the **Approvals Inbox** (`/admin/approvals`) — Billing Supervisor,
Admin, and MD hold it.

### What prints where

| Document | Screen |
|---|---|
| Patient ID card (re-issue stamped) | Patient Directory → patient → card |
| Money receipt / invoice (A4 or thermal) | appears after every Save; reopen from the invoice link |
| Test order slip + barcode sample labels | after saving a Diagnostic Order |
| Re-collection label | prints automatically on **Reject & re-collect** |
| Lab report (watermarked *provisional* until verified) | Work Board → View report, or Report Delivery |
| Radiology report (*provisional* until signed) | Modality Worklist → print |
| Prescription | after **Finalise** on the consult screen |
| Day-Close Statement | appears after **Close**; also from recent-closes list |
| Collection & income reports | Collection Reports → Print |
| Discharge / death / birth certificate | Certificates (issue once, **Reprint** counted) |
| Discharge gate pass | Discharge screen, step 5 |
| Operation register | Operation Register → Print |
| Pharmacy reports | Pharmacy Reports → Print |

### The two public screens (no login — safe for waiting areas)

- **`/public/queue`** — the lobby TV: per doctor, the serial now in the chamber (patient
  name masked), waiting and done counts. Refreshes itself. Never shows money or diagnoses.
- **`/public/report-status`** — patients type the **LB-…** order number from their receipt
  and see: *In progress / Ready for delivery / Delivered*. Wrong or unknown numbers all get
  the same answer, so nobody can fish for other people's orders.

### Troubleshooting

| What you see | What it means / what to do |
|---|---|
| A menu item this guide mentions is missing | Your role doesn't include it. Ask the Admin — role changes apply within ~5 minutes. |
| "Account locked after repeated failures" | 5 wrong passwords. Wait 5 minutes and try again. |
| Signed out by itself | 15 idle minutes. Sign in again; save work before stepping away. |
| "Open your counter before billing/collecting/selling" | Open **Counter Session** first (Billing Operator / Pharmacist). |
| "This counter already has an open session" (from yesterday) | Someone forgot day-close. A supervised carry-close is required — **this has no screen yet**; the Admin/vendor must intervene. Until then that counter cannot open. Prevent it: close every session, every shift. |
| Pharmacist: "no access" page right after opening the counter | Known quirk. Click **Pharmacy Sale** in the menu and continue. |
| IPD menu items appear under a heading that says "Front Desk" | Known label quirk — the items themselves are correct. |
| A report prints with a *provisional* watermark | It is not verified/signed yet. Never hand a provisional print to a patient as final. |
| A test/service/medicine can't be found at the counter | It is unpriced (provisional), expired, or out of stock — the shelf hides what can't be sold. Tests: tell the Admin (price list). Medicine: check Stock & Expiry. |
| An imaging order is missing from every machine worklist | Unpaid (collect the due first) — or the test is unmapped: Admin/technician must map it under **Machines & Mapping**. |
| "Possible duplicate" while registering | Same person probably exists. Open the existing record; press **register anyway** only for a genuinely different person. |
| Help Desk estimate says "AT LEAST" | A bed-day has no price for some date — the figure is a floor, not a quote. Tell the Admin to fix the tariff. |
| The drawer doesn't match at day-close | Close anyway with the honest count — variance is recorded, not punished by the software. Tell your supervisor. |

### Known limits (so nobody searches for what isn't there)

- **No Accounts ledger module yet** — the printed Day-Close Statement and the reports are
  the accounting hand-off (see Part 3). Consultant payout calculation, corporate/panel
  billing, HR & payroll, general store inventory, blood bank and canteen are also not
  built yet. The data they will need (doctor attribution on every charge, OT team fees,
  referrer commission accruals) is already being recorded.
- **One outdoor patient = one invoice per counter per day**, not one combined bill.
- **SMS is simulated** until a gateway is procured — the SMS Tray shows every message that
  would have been sent.
- **Patient record merging is not built** — avoid creating duplicates at registration.

---

*Questions or corrections to this guide: it lives at `user-guide.md` in the project
repository (spec 0033). The guide describes behaviour as of 2026-07-29; when a quirk noted
above is fixed, update the matching line.*
