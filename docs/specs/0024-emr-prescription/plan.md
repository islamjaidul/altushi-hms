# 0024 — Plan

## Approved: 2026-07-28

## Traceability matrix (PRD → screen), written before any code

| PRD | Requirement | Screen / surface | Built |
|---|---|---|---|
| M5 [M] | Patient pickup from queue with pre-checkup vitals | `/emr/queue` (doctor), `/emr/vitals` (nurse) | yes |
| M5 [M] | Chief complaint, O/E, diagnosis entry | `/emr/consult/{encounterId}` | yes |
| M5 [M] | Investigation ordering → test orders in M8 | consult screen → `DiagnosticsService.CreateOrderAsync` | yes |
| M5 [M] | Medication with dose/frequency/duration | consult screen, drugs from `pharm.product` | yes |
| M5 [M] | Advice & follow-up date | consult screen (`hms-date`) | yes |
| M5 [M] | Prescription print on doctor's layout | `/emr/prescription/{id}` | yes |
| M5 [M] | Longitudinal EMR view | `/emr/history/{patientId}` | yes |
| M5 [S] | Doctor templates filling all sections | `/emr/templates` + apply on consult | yes |
| M5 [S] | Drug list from pharmacy item master | type-ahead over `pharm.product` | yes |
| M5 [S] | Favourite drugs (US5.1: ≤ 3 keystrokes) | favourites row on consult | yes |
| M5 [C] | Allergy & alert flags | — | **cut** (spec §Out of scope) |
| M5 [C] | ICD-code tagging | — | **cut** (spec §Out of scope) |
| 5A-7 | Medicine chart (MAR) | `/emr/chart/{admissionId}` | yes |
| 5A-7 | Diabetic chart | `/emr/chart/{admissionId}` | yes |
| 5A-7 | Patient receive note on IPD handover | `/emr/chart/{admissionId}` | yes |
| §10 | Prescription immutable after visit close | finalise → supersede-only | yes |
| §12 | Permissions as data | `emr.read`, `emr.note.write`, `emr.vitals.record`, `emr.chart.record` | yes |

## Module

New `Hms.Emr` module + `emr` schema (ADR-0003: one DbContext per schema; module references only
Contracts + Kernel). Cross-schema reads (patient, encounter, results, admissions) are joined in
memory at the composition root, never in SQL across contexts.

### Entities

- `emr.note` — the consultation. `encounter_id` (outdoor) XOR `admission_id` (indoor), doctor,
  complaint, on-examination, diagnosis, advice, `follow_up_on`, `state` (draft|final),
  `supersedes_id`, created/finalised attribution.
- `emr.note_drug` — product (nullable, free text kept for off-formulary), dose, frequency,
  duration, instruction, ordinal.
- `emr.vitals` — encounter XOR admission, systolic/diastolic, pulse, temp ×10 (integer tenths, no
  floats near clinical numbers), weight ×10, SpO₂, recorded by/at.
- `emr.template` — per doctor: name + the four sections + drug lines as JSON.
- `emr.favourite` — doctor × product.
- `emr.mar_dose` (5A-7) — admission, drug text, dose, `scheduled_at`, `administered_at`,
  administered_by, state (scheduled|given|missed|refused) + reason.
- `emr.glucose_reading` (5A-7 diabetic chart) — admission, at, mmol×10, insulin units, route.
- `emr.receive_note` (5A-7) — admission, from, condition, belongings, received by/at.

Constraints mirror the house style: `num_nonnulls(encounter_id, admission_id) = 1` on note and
vitals; state-guarded updates for finalise and for MAR administration (ADR-0015 — affected-rows-0
becomes a comprehensible message, never a silent no-op).

### Service

`EmrService` — draft/finalise/supersede, vitals recording, template apply, MAR scheduling and
administration. Test ordering stays at the composition root (`EmrOrdering` in `Hms.Web`), because
it spans Emr + Diagnostics + Billing exactly as `IpdBilling` spans Ipd + Billing.

## Screens

1. `/emr/queue` — today's open encounters with a vitals/notes column; the doctor's worklist.
2. `/emr/vitals` — nurse entry, patient by type-ahead, one row per visit.
3. `/emr/consult/{encounterId}` — the consultation: vitals panel, four sections, favourites,
   drug type-ahead, test ordering, template apply, save draft / finalise & print.
4. `/emr/prescription/{noteId}` — print layout (reuses the existing print CSS and identity).
5. `/emr/history/{patientId}` — the longitudinal record.
6. `/emr/templates` — the doctor's own templates.
7. `/emr/chart/{admissionId}` — MAR, diabetic chart, receive note (nurse).

Nav entries under a new "Clinical" group; a new **OPD Consultant** role in the seed carries
`emr.read` + `emr.note.write` + `diagnostics.order.create`; Nurse gains `emr.vitals.record` and
`emr.chart.record`.

## Verification

- Service integration tests (finalise guard, supersede chain, MAR state machine, vitals XOR).
- Architecture tests keep passing unchanged (no cross-context joins, no module-to-module refs).
- `eng/verify/emr-thread.py` — nurse vitals → doctor note → tests ordered → charges unbilled at
  the counter → finalise → print → history shows it → supersede. Dirty-database tolerant.
- Playwright `spec-0024.spec.ts` — the screens load, the illegal edit is absent after finalise,
  a template application fills the form.
- Upgrade gate runs the new script over previous-release data.
