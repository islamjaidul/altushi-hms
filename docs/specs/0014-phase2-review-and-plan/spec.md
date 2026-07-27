# 0014 — Architect review of the MVP + Phase-2 build plan

- **Status:** Done
- **Date:** 2026-07-27
- **PRD ref:** §5, §5A, §9, §9A.3 (scope change: PM released the fourteen deferred modules)
- **MVP:** post-MVP — Phase-2 kickoff work per `docs/architect_review_prompt.md`

## Problem

The PM has released the fourteen modules PRD §9A.3 deferred. Before any of them is built,
the inheriting architect must (a) review the delivered MVP — is it sound, is it safe to run
a hospital's cash and lab results on, where is the debt that compounds at Phase-2 scale —
and (b) sequence the fourteen modules with their structural prerequisites. Manual UI testing
also surfaced input-control defects (search over pre-truncated pages, a silently ignored
report date range, two date-entry paradigms) whose root cause — no shared input layer —
must be ruled on before fourteen more modules hand-roll their own inputs.

## Requirements

- [M] Architectural review at `docs/architecture/10-mvp-review.md`: rule on the shared input
  layer first; name ADR drift with file references; judge money-spine concurrency safety;
  prove or refute the M6 folio seam; rank the known debt by Phase-2 pain.
- [M] Build plan at `docs/architecture/11-build-plan-phase2.md`: sequencing for all fourteen
  modules + R2/R3/R4, structural work each forces, migration risk, buildable-now vs
  blocked-on-precondition, where the shared input layer lands.
- [M] Verification of the claimed MVP state before judging it (build, tests, harness).
- [S] New ADRs for any decision surfaced; PM questions to `09-questions-for-pm.md`.

## Acceptance criteria

1. Full verification harness green on a fresh database before the review is written
   (81 .NET tests, golden-thread, discount-and-dues, 104 Playwright tests).
2. `10-mvp-review.md` exists and every claim in it carries a file reference or a
   verification-run result — no unverified assertions from the handoff summary.
3. `11-build-plan-phase2.md` exists, sequences all fourteen modules plus R2/R3/R4, and
   states for each whether it can be *validated* or only *built* today.
4. Decisions that change inherited ADRs are recorded as new ADRs, not silent edits.

## Out of scope

Building any of the fourteen modules or the shared input layer — each gets its own spec
(the input layer next, then Pharmacy M11 first among modules unless the plan argues better).

## Risks / open questions

- Review findings may demand structural work before Pharmacy (upgrade-path tests, input
  layer). Default: fix the debt the review ranks top *before* the first new module.
- Some modules can only be built, not validated (no stock, no analyzers, no payroll, no
  transfusion licence) — demo risk goes to the PM, not silently absorbed.
