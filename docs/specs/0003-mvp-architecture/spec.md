# 0003 — MVP Architecture (execute the architect handoff)

- **Status:** Done
- **Date:** 2026-07-26
- **PRD ref:** §16 (Q1–Q15 + handoff checklist), §9A (MVP scope), §8, §6
- **MVP:** in scope

## Problem

The PRD (v1.1) and the architect handoff brief (`docs/architect_prompt.md`) are complete, but `docs/architecture/` does not exist. No technical decisions have been made: no stack, no data model, no offline/concurrency strategy, no deployment design. Implementation cannot start, and the sales demo cannot be planned, until the architecture deliverables exist and PRD §16 Q1–Q15 are answered as ADRs.

## Requirements

- [M] Produce all 10 deliverables named in `docs/architect_prompt.md` under `docs/architecture/`.
- [M] Answer PRD §16 Q1–Q15 as ADRs (one decision per record), plus stack, database, application architecture, and ID/numbering ADRs.
- [M] Respect the non-negotiable constraints C1–C9 of the brief; scope stays exactly §9A.2.
- [M] Memory budget table for the 2 vCPU / 3 GB MVP box (≤ 2.6 GB, ≥ 400 MB headroom); figures marked estimates until measured.
- [M] Every one of the brief's 34 edge cases receives a stated design response or reasoned deferral.
- [M] PM steer (2026-07-26): stack must be industry-standard and enterprise-extendable (production hardware will be far larger than the MVP box); local dev/test on macOS must work.
- [M] Frontend follows the user-supplied design reference "Altushi HMS (standalone).html" (archived under `docs/architecture/assets/`).
- [S] Genuine business questions land in `09-questions-for-pm.md` with recommended defaults, never decided unilaterally.

## Acceptance criteria

1. `docs/architecture/00…09` files exist; `01-adr/` contains ADRs covering Q1–Q15 with none missing (verifiable by sweep).
2. Memory budget table totals ≤ 2.6 GB with headroom stated; no swap dependency in steady state.
3. Edge-case sweep: all 34 items traceable to a design response.
4. No business/scope decisions inside architecture docs; no tech leaked into the PRD (spec-auditor clean).
5. Every `§` citation in the new docs resolves to a real PRD section.

## Out of scope

Writing application code, seed-data scripts, or Dockerfiles themselves — this spec covers the architecture documents only. Scope changes to §9A.2 (PM-owned).

## Risks / open questions

- RAM figures are estimates until containers exist; DoD's "validated against measured usage" is deferred to implementation (recorded in notes.md).
- No demo date exists in the PRD → build plan uses relative sprints (recommended default; PM can pin dates later).
