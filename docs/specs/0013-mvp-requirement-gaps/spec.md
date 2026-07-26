# 0013 — Close the MVP requirement gaps (PRD [M] items with no screen)

- **Status:** In Progress
- **Date:** 2026-07-26
- **PRD ref:** §5 (M1, M3, M4, M8, M9, M20, M21, M22), §5A, §9A.2, §11, §12
- **MVP:** in scope — this finishes §9A.2's stated depth; it adds no module

## Problem

Spec 0012 delivered a screen for every route in the nav registry and was accepted on that
basis. But the nav registry was never derived from the PRD — so "every menu works" was never
the same claim as "every MVP requirement has a UI". Nobody has ever checked the second one.

An audit of §5, §5A and §9A.2 against the built screens finds **~25 `[M]` or `Must` requirements
inside the eight MVP modules with no user interface**, several of them load-bearing for the
demo's own success criteria: bulk price-list import is §9A.1's "single strongest lock-in
mechanism" (fear F1) and has a service but no screen; split multi-tender payment is `Must`
(5A-4); the Request Center pattern is `Must` (5A-21); reporting-consultant verification is
`Must` (R1).

This spec closes those gaps and — more importantly — establishes the **traceability matrix**
that should have existed from the start, so coverage is a checked fact rather than an
assumption.

## Requirements

- [M] A traceability matrix mapping every §5/§5A `[M]`/`Must` requirement of the eight MVP
      modules to the screen that satisfies it, or to an explicit deferral with a reason.
- [M] Every unsatisfied `[M]`/`Must` item in that matrix gains a working screen wired to the
      existing services, in the design grammar established by 0012.
- [M] No new module (hard rule 2). Where a gap needs a master that does not exist
      (referrer, reporting consultant), the master is added inside the owning MVP module.
- [S] The matrix becomes a checked artifact — a test or script that fails when a row regresses.

## Acceptance criteria

1. The matrix in `plan.md` has no unsatisfied `[M]`/`Must` row without a recorded deferral.
2. Golden thread, discount/dues and the Playwright suite stay green; new screens gain coverage.
3. §9A.4 criterion 4 becomes demonstrable: a price list can be imported through the UI.

## Out of scope

- The 14 modules §9A.3 defers (IPD, OT, pharmacy, radiology, blood bank, canteen, accounts,
  HR, consultant pay, corporate, referral, front desk, EMR, inventory) and sub-modules R2–R4.
  Expanding there is a PM decision under hard rule 2, not a gap.
- Analyzer/DICOM/biometric integration (§9A.3 — the devices do not exist yet).
- The 90-day seed history (spec 0010) and timed §9A.4 tests (spec 0010).

## Risks / open questions

- **Scale.** This is the largest remaining UI body of work; it is sequenced in waves in
  `plan.md` so each wave lands verified rather than everything landing at once.
- **Reference ranges by age/sex** (§5 M9 `[M]`) change the shape of stored results. The
  `ref_used` snapshot already stored with each value protects historical reports (edge 22),
  but the template format itself needs versioning care.

## Supersedes

Spec 0012's implicit coverage claim. 0012 remains accurate about what it set out to do —
every nav route renders — and this spec records that the nav route set was itself incomplete.
