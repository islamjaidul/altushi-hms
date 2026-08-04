# 0045 — The unconscious patient never gets his name back

- **Status:** Done
- **Date:** 2026-08-03
- **PRD ref:** §5 M1 (US1.4, US1.1), §7, §12, edge 25
- **MVP:** in scope — completes a shipped `[M]` user story, no new product scope
- **Found by:** driving the emergency admission path through the UI, 2026-08-03

## Problem

**US1.4:** *"As an Emergency Desk Operator, I want an 'unknown patient' quick registration (e.g.,
unconscious patient), so that care is never blocked; **details completed later**."*

The first half is built and works well. An unidentified casualty is registered with no name and
no age, a UHID is issued immediately, the card prints so a sample tube can be labelled, and the
patient can be admitted to a bed and investigated without anyone knowing who he is. That is
exactly right.

**The second half does not exist.** There is no edit screen, and `RegistrationService` has no
update path at all — `RegisterAsync` is the only write, and it only ever inserts. Confirmed by
driving it: `GET /registration/{id}/edit` returns **404**, and `src/Hms.Web/Pages/Registration/`
contains only `New`, `Index` and `Card`.

So:

- A patient registered as `UNKNOWN-…` stays `UNKNOWN-…` **for life**. His brother arrives an
  hour later with a name, an age and a phone number, and there is nowhere to put them.
- The registration screen tells the operator, in as many words, *"The system issues a UHID
  immediately; details are filled in later."* The product makes a promise it cannot keep.
- More broadly, **no patient detail can ever be corrected.** A misheard name, a transposed
  phone number, a wrong sex or age is permanent. Sex and age are not cosmetic — they select the
  reference band a lab result is flagged against (§5 M9), so a wrong sex silently mis-flags
  every future haemoglobin.
- The only way to give the man his name is to register him a second time, which creates a second
  UHID and splits his history — precisely what §5 M1 exists to prevent ("One patient = one
  permanent UHID for life"), and the duplicate the `[S]` merge feature would then be needed to
  repair.

This is **half of a written `[M]` user story**, not new scope.

## Requirements

- [M] **A registered patient's details can be completed or corrected** through the product:
      name, sex, age or date of birth, phone, guardian, area, address, blood group, allergies.
- [M] **Completing an unknown patient's identity clears the unknown flag** and gives them a real
      name, on the same record — same UHID, same history, same open admission.
- [M] **Every change is attributable and append-only** — who changed what, from what, to what,
      and when (hard rule 4). Nothing about a correction may erase what was there before.
- [M] **The UHID is never editable.** It is the identity the rest of the product refers to.
- [M] **The screen is reachable** from where an operator already is — the patient card and the
      patient directory.
- [S] Completing an identity re-runs duplicate detection, so renaming `UNKNOWN-14` to a name the
      hospital already holds is *surfaced* rather than silently creating a functional duplicate.

## Non-goals

- **Patient merge and deactivation** (`[S]`, US1.3). Still unbuilt, still deferred — spec 0039
  WP5.2 already recorded `MergedInto`/`Active` as written nowhere. This spec reduces how often
  merge is *needed*; it does not implement it.
- Editing anything outside the patient's own identity — encounters, invoices, admissions and
  results are corrected by their own reversal paths, never here.
- Changing the UHID series, format, or issuance.
- A general-purpose admin data editor.

## Acceptance criteria

**AC1** — A patient registered with "identity unknown" can be given a name, sex, age and phone
afterwards; the record keeps its original UHID and its open admission.
**AC2** — After completion, the patient is findable by the new name, and the search returns the
**same** patient id — no second record.
**AC3** — The change writes an audit event naming the actor and the fields that changed, with
their previous values.
**AC4** — The UHID cannot be changed by any posted field.
**AC5** — Validation matches registration: a blank name is refused unless the record is still
flagged unknown; an unparseable age is refused with the same message the registration screen uses.
**AC6** — `dotnet test` green; negative-tested — removing the audit write, or the unknown-flag
clear, fails a test.

## Notes

Found while answering "what happens when a patient arrives at emergency needing admission and
tests". The rest of that path held up well: unknown registration, admission with
`Source = Emergency`, a folio that opens and posts its admission fee, and bedside investigations
that reach the lab with nobody paying first (the indoor rule). Two other observations from the
same run are recorded in `notes.md` rather than fixed here — one is a deliberate design seam and
one is a question for the PM, not an engineering decision.
