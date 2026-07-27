# 0025 — Tasks

- [x] Traceability matrix written before code (`plan.md`)
- [x] `Hms.Ot` module + `ot` schema: theatre, case (parent XOR + window check), team, consumables
- [x] `OtService`: schedule with theatre-lock + overlap refusal, §11 transitions, cancel/postpone,
      completion record — all state-guarded
- [x] `OtBilling` at the composition root: completion charges (operation + per-role team fees) and
      FEFO consumables, posted to folio or visit in the completing transaction
- [x] Screens: board, schedule, case, register, theatre master
- [x] Seed: two theatres, four operations, three per-role fee services (all priced)
- [x] Nav under "Operation Theatre"; **OT In-charge** role and `shaheen` in the demo cast
- [x] 10 integration tests (`OtTests`) — overlap, surgeon clash, freed slot, ordering, double
      completion, reasons required
- [x] 5 Playwright tests (`spec-0025.spec.ts`)
- [x] `eng/verify/ot-thread.py` — 25 checks; repeat-run safe (own theatres, own date)
- [x] Upgrade gate runs the thread and smokes the routes
