# 0024 — Tasks

- [x] Traceability matrix written before any code (`plan.md`)
- [x] `Hms.Emr` module + `emr` schema: note, note_drug, vitals, template, favourite,
      mar_dose, glucose_reading, receive_note; parent XOR checks; supersede uniqueness
- [x] `EmrService`: draft/save/finalise/supersede, vitals, templates, favourites, MAR, glucose,
      handover — every mutation state-guarded or attributable
- [x] `EmrOrdering` at the composition root: prescription → test order → unbilled charge (US5.4)
- [x] `EmrRecord`: the longitudinal read, joined in memory across five schemas
- [x] Screens: queue, vitals, consult, prescription print, history, templates, nursing charts
- [x] `/api/typeahead/products` — the drug picker is the pharmacy master
- [x] Nav under "Clinical"; **OPD Consultant** role and `chowdhury` in the demo cast;
      Nurse gains vitals + charts
- [x] Entitlement regenerated to include the new modules
- [x] **Defect (found by the thread):** reopening a signed visit silently started a *second*
      prescription. The screen now shows the latest note, and the write path refuses when it is
      signed.
- [x] **Defect (found by the tests):** the state-guarded UPDATE left EF's tracked entity stale,
      so a save later in the same transaction could slip past the immutability check. Both
      finalise and supersede now reload the entity.
- [x] 12 integration tests (`EmrTests`), 6 Playwright tests (`spec-0024.spec.ts`)
- [x] `eng/verify/emr-thread.py` — 30 checks across the whole clinical thread including 5A-7
- [x] Upgrade gate runs the thread and smokes the new routes
