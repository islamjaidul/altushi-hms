# 0043 — Front-desk friction: a false duplicate, a mislabelled menu, and a cashier retyping what the hospital knows

- **Status:** Done
- **Date:** 2026-08-03
- **PRD ref:** §5 M1, §5 M3, §5 M4, §7 (U7, U15), §3.2
- **MVP:** in scope — defects and usability in shipped modules, no new product scope
- **Found by:** operator testing on the demo VM (`hms.specshipper.com`), 2026-08-03

## Problem

A receptionist registering a walk-in hit three separate frictions in the first two minutes of
the patient journey. Each is small; together they are the difference between a front desk that
trusts the software and one that clicks through its warnings.

1. **A shared family phone is reported as a possible duplicate patient.**
   `RegistrationService.FindDuplicatesAsync` matches on *same phone* **OR** *(phonetic name
   match AND age ±2y)*. In Bangladesh one mobile number routinely serves a whole household, so
   registering a wife on her husband's number raises *"1 possible duplicate — this patient may
   already be registered"* against a record with a **different name and a different sex**.

   The warning is non-blocking, which is right. The damage is behavioural: an operator who sees
   an obviously-wrong duplicate warning several times a day stops reading it and reflexively
   presses **"Not a duplicate — register anyway"** — which is precisely the click that admits a
   *genuine* duplicate later. A warning that is usually wrong is worse than no warning.

2. **The sidebar shows "FRONT DESK" twice, and never shows "Indoor (IPD)".**
   `NavComposer.Compose` groups by `Module`, and `NavGroup.Label` reads `Items[0].GroupLabel`.
   The `Ipd` module's first registry entry is Help Desk, labelled `"Front Desk"`, so the whole
   IPD block inherits that heading and the `"Indoor (IPD)"` label carried by Ward Board, New
   Admission, Ward Indents, Certificates, Nursing Station, Ward Duty and IPD Reports is
   **silently discarded**. Ward functions are filed under the front desk.

3. **The cashier re-selects, by hand, the service the appointment already named.**
   The receptionist records *patient → doctor → serial*. Seconds later the cashier opens
   `/billing/opd`, finds the patient, and then searches the whole hospital catalogue for
   "Doctor Consultation". `OpdModel.LoadAsync` queries services, patient, encounter and
   approvals — and **never opens the appointments context at all**. The screen's own comment
   states the principle it is failing to apply here: *"the operator never re-types what the
   hospital already knows."*

   Underneath sits a data-model gap: **no consultation fee is attached to a doctor.** `Doctor`
   is `{ Id, Name, Active }`; `DoctorSchedule` carries room and capacity. Nothing distinguishes
   a `CON-GEN` doctor from a `CON-SPC` one, so the counter could not pre-fill correctly even if
   it looked.

## Requirements

- [M] **A phone-only match must not be presented as a probable duplicate.** It stays visible —
      a shared number is how a returning patient is found — but under its own heading that says
      what actually matched. A name match continues to read as a duplicate.
- [M] **Every nav group renders under the label its items declare.** No group heading appears
      twice; `"Indoor (IPD)"` exists.
- [M] **A doctor carries the consultation service they charge**, settable by an administrator
      through the product, effective-dated by the existing rate machinery (hard rule 5 — the fee
      is a reference to a service, never a copied number).
- [M] **The OPD counter surfaces today's appointment** for the selected patient and offers that
      doctor's consultation in one press. Nothing enters the cart without an operator action.
- [S] The absence of a serial is visible to the cashier, without blocking the bill.

## Non-goals

- **Making a serial mandatory before billing.** Considered and rejected: emergencies, after-hours
  attendance and follow-ups must bill without an appointment. Optional-by-design stays.
- Removing phone from duplicate matching. It is a legitimate and necessary signal.
- A doctor-fee *schedule* (per-session or per-weekday pricing). One consultation service per
  doctor; anything richer is new scope for the PM.
- Retrofitting the two other cash screens (`/billing/dues`, `/pharmacy/pos`) with appointment
  awareness — neither sells consultations.

## Acceptance criteria

**AC1** — Registering a patient whose phone matches an existing record of a **different name**
renders under a heading that does not assert the patient may already be registered; registering
one whose **name** matches phonetically within the age band still does.
**AC2** — The composed sidebar contains no repeated group label, and an `Ipd`-entitled user with
`ipd.read` sees an `"Indoor (IPD)"` heading. Negative-tested: reverting `NavComposer` fails it.
**AC3** — An administrator can set a doctor's consultation service on `/admin/people` without
SQL, and the OPD counter offers exactly that service at the rate `RateResolver` returns for
today.
**AC4** — A patient with a serial today shows the doctor and serial number at `/billing/opd`;
pressing the suggestion places one consultation line in the cart at the resolved price. A patient
with no serial bills exactly as before.
**AC5** — `dotnet test hms-erp.slnx` green, `lifecycle-suite.py --tier t0` green, and the
existing `Same_phone_flags_duplicate_regardless_of_name` test still passes — the *rule* is
unchanged, only its presentation.

## Notes

Complaint 3 and the "is the serial enforced?" question share a root cause: the appointments
module is written to but never read by the modules downstream of it. This spec connects the one
seam that costs an operator time on every single OPD patient. The wider question — what else
should know about an appointment — is left open.

`shirin` was reported as a wrong doctor login during the same session. That was an error in a
chat answer, not in the product: `shirin` is Shirin Begum, *Department Head* (`hr.*` only) and
the seeded OPD consultant is `chowdhury`. Nothing in the repository claims otherwise, so there
is no code or doc change here — recorded so the question is not re-opened.
