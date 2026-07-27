# 0026 — Tasks

- [x] Traceability matrix written before code, with the three cuts named (`plan.md`)
- [x] `Hms.Radiology` module + `radiology` schema: modality, modality_test (one machine per test),
      study (unique per order test, accession number reserved as the DICOM seam)
- [x] `RadiologyService`: idempotent study creation, state-guarded "done", reported marker
- [x] `RadiologyReporting` at the composition root: the worklist read model and the bridge that
      stores a radiology report as the order test's **result** (M9's store, e-sign and delivery)
- [x] Screens: modality worklist, report editor with the exam template, print (provisional/final)
- [x] Seed: five modalities, the three imaging tests mapped; unmapped tests surfaced as a warning
- [x] Nav under "Radiology"; **Radiology Technician** role and `moinul` in the cast; Pathologist
      gains the reporting permission
- [x] 6 integration tests (`RadiologyTests`), 4 Playwright tests (`spec-0026.spec.ts`)
- [x] `eng/verify/radiology-thread.py` — 24 checks, passed on the first run
- [x] Upgrade gate runs the thread and smokes the route
