# 0024 — M5 Prescription & EMR (Wave 3)

- **Status:** Done
- **Date:** 2026-07-28
- **PRD ref:** §5 M5, §5A-7, §11, §12, §7
- **MVP:** in scope — Wave 3 of `11-build-plan-phase2.md`

## Problem

The product bills a consultation and never records it. A patient's visit leaves an invoice, a
test order and a receipt behind, but nothing that says why they came, what was found, or what
they were told to take. The doctor — persona P4, the first non-operator user the product has —
has no screen at all, and the longitudinal history that makes the rest of the record worth
keeping does not exist.

Two consequences bite today. Tests ordered by the doctor are re-typed at the counter from a
paper chit (US5.4 exists precisely because that re-typing loses orders), and a nurse's
pre-checkup vitals live on the same chit.

## Requirements

- [M] Pre-checkup **vitals** recorded by a nurse against today's visit: BP, pulse, temperature,
  weight, SpO₂ — visible to the doctor before consultation (US5.3).
- [M] **Consultation note**: chief complaint, on-examination, diagnosis, advice, follow-up date.
- [M] **Medication entry** with dose, frequency and duration, drawn from the pharmacy item
  master so a prescribed brand is a real brand (§5 M5 [S] drug list, pulled forward because
  free-text drugs would make the pharmacy link fiction).
- [M] **Investigation ordering from the prescription** creating real test orders (M8), appearing
  unbilled at every billing counter under the patient's UHID — no re-typing (US5.4 AC).
- [M] **Prescription print** on the doctor's own layout.
- [M] **Longitudinal EMR view**: previous visits, prescriptions, verified lab results, admissions.
- [M] A prescription is **immutable once the visit closes** (§10 entity table); corrections are a
  new note that supersedes, never an edit.
- [S] **Doctor templates** applying complaints/diagnosis/drugs/advice in one action, all editable,
  and **favourite drugs** (US5.1 AC: ≤ 3 keystrokes to add one).
- [S] **5A-7 nursing charts** against an admission: medication administration record (MAR),
  diabetic chart, and the patient receive note on IPD handover.

## Acceptance criteria

1. A nurse records vitals for a patient on today's visit; the doctor's screen shows them without
   being asked to fetch anything.
2. A doctor writes a note with complaint, diagnosis, two drugs and advice, and prints it; the
   print carries the doctor's name, registration line and the hospital identity.
3. Ordering two tests on that note creates a test order whose charges appear **unbilled** on the
   OPD billing screen for that patient, and the order is visible in the diagnostics flow.
4. Applying a template fills all four sections in one action and every field stays editable.
5. The EMR view for a patient with history shows previous notes, their prescriptions, verified
   results and admissions, newest first.
6. A finalised note cannot be edited; the screen offers a superseding note instead (U7: the
   illegal action is absent, not refused).
7. A MAR dose can be scheduled and marked administered against an admission, with who and when;
   a diabetic chart row records glucose and insulin; a receive note records the handover.
8. Every write is attributable and audited; no clinical hard deletes.

## Out of scope

- **[C] Allergy & alert flags** and **[C] ICD-code tagging** — explicitly cut from this spec, per
  the Definition of Done's rule that anything cut is cut in the matrix. Both are additive later.
- Analyzer/PACS integration (I10) — M9/M10 work, and no devices exist.
- Doctor-side scheduling or chamber management (M3 owns serials).
- Nursing charts beyond MAR/diabetic/receive note: observation charts, intake-output, and the
  rest of the ward paperwork are not in 5A-7 and are not invented here.

## Risks / open questions

- **A doctor-facing screen is a new persona.** §7's UX principles were written for 30–55 year old
  counter operators; a consultant resisting typing (P4) is the same constraint sharpened. The
  three-minute target (US5.1) is the acceptance bar, not a nice-to-have.
- **Immutability versus reality.** A doctor who mistypes a dose must be able to fix it. The
  spec's answer is supersede-not-edit, which keeps the record honest but adds a step. Raised to
  the PM as **P22** with that default.
- No prescription can be written before the visit is billed, because the encounter is born at the
  counter. That matches how these hospitals work today; it is stated so it is a decision, not an
  accident.
