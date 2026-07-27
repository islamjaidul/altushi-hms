# 0026 — Plan

## Approved: 2026-07-28

## Traceability matrix (PRD → screen), before code

| PRD | Requirement | Screen / surface | Built |
|---|---|---|---|
| M10 [M] | Imaging worklist per modality, fed from M8 | `/radiology/worklist` | yes |
| M10 [M] | Study-done marking by technician (+ film usage) | `/radiology/worklist` | yes |
| M10 [M] | Template-based report editor per exam | `/radiology/report/{studyId}` | yes |
| M10 [M] | Final approval with e-signature | same screen → `LisService.VerifyAsync` | yes |
| M10 [M] | Amendment audit as in M9 | existing `/lis/amend` (same result store) | yes |
| M10 [M] | Delivery integration + SMS trigger | existing `/diagnostics/delivery` | yes |
| M10 [S] | Radiology report print | `/radiology/report/{id}` print view | yes |
| M10 [S] | DICOM modality worklist feed | — | **cut** (no device; `accession_no` seam kept) |
| M10 [S] | PACS integration / viewer | — | **cut** (no archive) |
| M10 [C] | Comparison with prior studies | — | **cut** (patient record covers it) |
| §12 | Permissions as data | `radiology.worklist.read`, `radiology.study.perform`, `radiology.report.write` | yes |

## The central decision: one result store

A radiology report **is** a result. It is entered against the same `diag.order_test`, stored in
`lis.result`, verified by the same `LisService.VerifyAsync` (same e-sign hash), amended by the
same approval-gated path, and delivered by the same flow. M10 adds the *radiology-shaped surface*
and the study's own facts — it does not add a second place where a patient's report can live.

That is why this module is small: most of what M10 needs, M9 already guarantees.

## Module

New `Hms.Radiology` module, `radiology` schema.

- `radiology.modality` — branch, name, code, active.
- `radiology.modality_test` — which `adm.test_catalog` rows a modality performs (many-to-one).
- `radiology.study` — one per `diag.order_test`: modality, state
  (`awaiting` → `done` → `reported`), `accession_no` (the DICOM seam, generated now, unused),
  performed by/at, film size and count, technician note. Unique on `order_test_id`.

`RadiologyService` — study creation (idempotent), study-done marking (state-guarded), and the
worklist read model. Report writing calls `LisService` from the composition root
(`RadiologyReporting`), because it spans Radiology + Lis + Diagnostics.

## Screens

1. `/radiology/worklist` — modality tabs; today's paid imaging orders; mark done with film usage.
2. `/radiology/report/{studyId}` — template parameters + findings + impression; save draft, sign.
3. `/radiology/print/{studyId}` — the report on letterhead; watermarked **provisional** until signed.

Nav under "Radiology". Roles: **Radiology Technician** (worklist + perform), and the existing
**Pathologist** role gains `radiology.report.write` (a reporting consultant signs both).

## Verification

- Integration tests: study creation idempotent; done-marking guarded; unsigned cannot be final.
- `eng/verify/radiology-thread.py`: order + pay an imaging test → it appears on the right
  modality's worklist and no other → mark done → report with template → unsigned prints
  provisional → sign → prints final → deliverable in `/diagnostics/delivery`.
- Playwright: worklist and report screens; the provisional/final print distinction.
- Upgrade gate runs the thread and smokes the routes.
