# 0020 — Patient-lifecycle gaps found by end-to-end smoke testing

- **Status:** Done
- **Date:** 2026-07-27
- **PRD ref:** §7 U5/U13 (finding a patient), §5 M6 US6.2 + §11 Admission machine, §5A.2 R4,
  §5 M11 / 5A-9 (indoor issue), §3.2 (BD payment culture), §8 N5
- **MVP:** post-MVP — corrective work inside the modules delivered by specs 0015–0019

## Problem

Walking one patient's whole life through the built product (register → serial → OPD bill →
diagnostics → LIS → pharmacy → admission → folio → discharge → return visit) surfaced three
defects that no existing test covers, because every test so far exercised one module's happy
path with data it created itself.

1. **A patient cannot be found by phone as anyone would type it.** Registration stores the
   number formatted (`01712-345999`, §7 U13), but every search compares the raw string, so
   typing the digits off a prescription slip — `01712345999` — returns **nothing**. Since
   spec 0015 replaced the `<select>` pickers with the type-ahead, this breaks the *only*
   patient picker at OPD billing, diagnostics, appointments, pharmacy POS, IPD admission and
   the help desk. The operator concludes the patient does not exist and registers a duplicate.

2. **A patient can walk out of the gate owing money, silently.** Discharge issues the gate
   pass while the settlement invoice is still unpaid (observed: ৳1,700 outstanding), having
   passed through a state called `financially_settled`. Nothing on the screen shows what is
   owed, nothing forces a decision, and the only trace is a generic audit row. R4's block
   exists for the deliberate hold; nothing catches the *accidental* one.

3. **A counter sale to an admitted patient silently becomes an outdoor due.** The pharmacy POS
   gives no sign that the selected patient is in a bed, so medicine that 5A-9 says belongs on
   the folio becomes a parallel invoice that discharge never sees — compounding defect 2.

## Requirements

- [M] Finding a patient by phone must work for **digits as typed** (`01712345999`,
  `1712345999`, `+8801712345999`) as well as the stored display form — everywhere a patient
  is searched, with the predicate still evaluated in SQL (ADR-0020 rule, no fetch-then-filter).
- [M] Discharge must **show the patient's whole outstanding position** (settlement due plus
  any other unpaid invoice) and must not complete silently while money is owed: releasing a
  patient with a due is a **decision with a stated reason**, attributable and audited.
- [M] A screen that is about to create an **outdoor** charge for a patient who holds an
  **open admission** must say so, and point at the folio path (POS and OPD billing).
- [M] The lifecycle itself becomes a permanent regression script, so the next module cannot
  re-break the seams between modules.

## Acceptance criteria

1. `/api/typeahead/patients?q=01712345999` returns the patient stored as `01712-345999`;
   so do the registration directory, dues and refund searches. Matching is index-backed and
   happens in SQL; it cannot drift from what registration wrote.
2. Discharge shows: settlement invoice number, its balance, and a list of the patient's other
   unpaid invoices with a total. With any balance outstanding, the plain "Discharge" action
   is **absent**; the available action requires a typed reason (§7 U7: illegal actions are
   absent, not warned about).
3. `DischargeAsync` refuses server-side when the settlement invoice has a balance and no
   reason is supplied — a crafted POST cannot bypass the screen. The permitted path writes a
   tier-2 audit row naming the amount and the reason.
4. A patient with an open admission is flagged on the pharmacy POS and the OPD billing screen
   before the invoice is saved, with a link to their folio. The sale is still *possible*
   (an attendant may legitimately buy at the counter) — it is no longer *invisible*.
5. `eng/verify/lifecycle-thread.py` walks the full journey across seven roles and asserts the
   seams; it is dirty-database tolerant and runs in the upgrade gate.
6. Existing verification stays green: .NET suite, the other thread scripts, Playwright, the
   upgrade gate and the CI greps.

## Out of scope

- Routing an admitted patient's counter sale **automatically** to the folio. The attendant
  case is real and the PRD's 5A-11 POS variants (indoor / outdoor / staff) are a Wave-1
  decision; forcing a route would break the counter. Flag now, route when 5A-11's POS-variant
  selector is built.
- Blocking discharge outright on any due. §3.2's payment culture (corporate credit, partial
  payment) makes dues legitimate; the fix makes them *deliberate*, not impossible. If the PM
  wants a hard block or an approval gate, it becomes policy data (**P20**).

## Risks / open questions

- **P20** (new): should discharge-with-due require a supervisor approval rather than an
  attributable reason? Default implemented: reason + tier-2 audit, because a discharge queue
  must not stall (§8 N1); R4's block remains the tool for a deliberate hold.
- Phone matching uses a stored generated column; the migration backfills every existing row
  by definition (generated columns compute on write and on backfill), so old data becomes
  searchable without a data-fix script.
