# 0042 — Nursing Station cross-module hardening

- **Status:** Done (build + local verification; deploy rides the next ERP image — `notes.md` §5)
- **Date:** 2026-08-03
- **PRD ref:** §5 M6 [M] (consultation/visit entry; folio universality), §5 M5 [C] (allergy & alert flags), §11, §12
- **MVP:** in scope (defect closure + implementation of existing §5 text — no new scope)

## Problem

A cross-module audit of the R5 Nursing Station (spec 0041) against the flows it must serve —
doctor rounds → charges, ward → pathology, ward → pharmacy, folio → accounts, patient
information → every department — found that the station's *reads* comply, but six defects sit on
the seams:

1. **The doctor's round produces no record and no charge** (§5 M6 [M]). Signing an indoor
   prescription is financially inert; the only path is a cashier manually posting `IPD-VISIT`
   with an *optional* doctor. The doctor who wrote the round and the doctor who would be paid
   are two unlinked facts.
2. **The diagnostics counter bills admitted patients outdoors.** It has no folio branch and no
   R4 blocked-patient guard, so an inpatient's test becomes a separate outdoor invoice —
   breaking "every chargeable event posts to the folio" [M].
3. **Open clinical work orphans at every terminal exit.** Every reader of doses and tasks is
   gated to live admissions, so at discharge/death/abscond every open item becomes invisible
   and un-closable, and a discharged patient's MAR cannot even be read. *This supersedes the
   claim in 0041 `notes.md` §6 that a trailing dose "is left for a nurse to close" — there was
   no screen on which she could.*
4. **One cross-tenant write:** recording a dose outcome carries no branch predicate.
5. **Ward writes accept any admission id in any state** — a task, dose, or prescription can be
   created against a discharged, dead, or non-existent admission.
6. **The money-carrying indoor seams (indoor lab order, indent issue, service post) had zero
   test coverage**, and death/abscond had no unit coverage at all.

Two opted-in additions from the same audit: an **allergy surface** (field absent everywhere;
0041 added four clinical screens, one of which prescribes, with no allergy visibility) and the
**prescription→pharmacy product link** (indoor drug lines are free text; indents are typed from
scratch).

## Requirements

- [M] Signing an indoor prescription records the consultant visit (doctor, admission, day) and
  posts one folio visit charge at the rate-plan price — at most once per doctor per day per
  admission, however many notes are signed. A blocked folio records the visit without a charge.
- [M] A test ordered for an admitted patient at the diagnostics counter posts to the folio (no
  separate invoice) and respects the R4 hold.
- [M] Open doses and tasks on a closed admission remain readable and closable-with-reason; the
  clinical-clearance step and death/abscond flows surface them. No machine writes a clinical
  outcome.
- [M] Dose outcomes are branch-scoped; ward writes require a live admission (close-out actions
  require only an existing one).
- [S] Clinical screens show a patient banner: UHID, age/sex, blood group, allergy flag,
  admission diagnosis. Registration captures allergies.
- [S] Indoor prescription lines can carry a pharmacy product; a ward indent can be prefilled
  from a signed prescription.
- [M] The previously-untested money seams get integration tests that fail without the fixes.

## Acceptance criteria

1. Sign an indoor note → folio shows a consultant-visit line with the signing doctor; signing a
   second note the same day adds nothing; a different doctor the same day adds one more.
2. An admitted patient at `/diagnostics/order` produces folio charge lines and a folio-parented
   order, not an invoice; a bill-blocked patient is refused with a sentence.
3. Discharge a patient holding an open task and scheduled doses: the station drops them, the
   chart still opens read-only, both items are closable with reasons, and the clearance screen
   listed them beforehand. Death/abscond warn when open items remain.
4. Recording a dose outcome with a foreign branch id changes nothing and says "already
   recorded". Creating a task/dose/prescription on a discharged admission is refused with a
   sentence.
5. An allergy entered at registration is visible on the indoor prescription and charts screens.
6. An indent prefilled from a signed prescription issues and charges exactly like a hand-typed
   one; product-less lines are named for manual entry.
7. All guards and suites green; migrations additive.

## Out of scope (recorded in the QA gap register, not built)

Nurse-side service posting (oxygen from tasks) · clinical discharge checklist beyond warnings ·
`TestOrderPaid` outbox consumer / any push notifications to pharmacy or pathology (poll-only
stands) · ward/bed visibility for the pharmacy porter, LIS phlebotomist, and OT (OtCase carries
no AdmissionId) · receive-note prompting from the admit flow · duty EmployeeId validation moving
into the service · `MarDose.ProductId` (dose-vs-issue reconciliation) · doctor-facing test
ordering on `/emr/indoor`.

## Risks / open questions

- Visit price is the seeded `IPD-VISIT` service through the effective-dated rate plan; per-doctor
  fee differentiation is an M17 concern for when M17 is built (the visit row already carries the
  doctor).
- Read-only terminal charts expose a closed record to `emr.chart.record` holders; that is the
  records-retention intent, and every close-out action still demands a reason and is audited.
