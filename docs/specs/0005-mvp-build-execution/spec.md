# 0005 — MVP build execution plan & S1 (walking skeleton + risk spikes)

- **Status:** In Progress
- **Date:** 2026-07-26
- **PRD ref:** §9A (scope being built), §7/§8 (binding UX & NFR inputs), §11 (state machines), §12 (permissions)
- **MVP:** in scope (enables the build; adds no product scope)

## Problem

The architecture package (spec 0003) and the staff-engineer handoff prompt (spec 0004) are complete, but no engineer-level execution plan exists. `docs/staff_engineer_prompt.md` requires, before any code: a mission restatement with implementation risks, an S1 task breakdown that becomes the S1 spec, a solution/repo layout mapped to ADR-0003, and any architecture-vs-.NET conflicts flagged now. Without it, the build cannot start without violating G1 (spec-first).

## Requirements

- [M] Mission restatement + top implementation risks, per the FIRST RESPONSE FORMAT.
- [M] Phase-by-phase plan for S1–S7 mapping each `08-build-plan.md` sprint to its future spec ID, key tests, exit criteria, and the edge cases it owns.
- [M] Dependency-ordered S1 task breakdown with test-first notes — this spec doubles as the S1 spec (staff_engineer_prompt, first-response item 2).
- [M] Solution/repo layout (projects, test projects, compose files) mapped to ADR-0003's module list and the G5–G9 test pyramid.
- [M] Conflicts / verify-in-S1 flags, honouring G4 (no capability asserted unverified).
- [S] Cites architecture docs by number/§ and ADRs by ID; duplicates nothing.

## Acceptance criteria

1. `plan.md` contains all four first-response items plus S1–S7 detail; every §/ADR/edge citation resolves to a real target.
2. The plan contradicts no ADR and adds no scope beyond §9A.2; estimates are marked as estimates.
3. `spec-auditor` passes (index row present, lifecycle valid, plan archived).
4. S1 exit criteria (when implemented under this spec): two roles log in on the demo laptop, see role-filtered nav, and print a Bangla-footered test PDF — with T1–T12's red-first tests green.

## Out of scope

S2–S7 implementation (each sprint gets its own spec, 0006–0011, at sprint start per G1); any change to the architecture docs or the PRD.

## Risks / open questions

- P11 (demo date + team size) is unanswered; the plan assumes 2–3 engineers over ~14 weeks (estimate) — renegotiate sprint mapping when the PM answers.
- Spike A/B failure paths are pre-recorded in ADR-0009/0014; a failed spike invokes the fallback and updates the ADR before dependent work, not after.
