# 0010 — S6: Demo kit + performance

- **Status:** In Progress
- **Date:** 2026-07-26
- **PRD ref:** §9A.4 (timed tests), §14 (volumes), §8 N1 (latency), §7 U14 (micro-help)
- **MVP:** in scope

## Problem
The demo must never feel empty (edge 4), must reset in seconds (edge 5), and the memory-budget
table must become measurement, not estimate (DoD).

## Requirements
- [M] Golden snapshot + `demo-reset.sh` < 30 s (template-database copy) + dual instances (edge 6).
- [M] Seed: demo cast/roles (S1) + starter catalog/prices; 90-day §14-shaped history generator.
- [M] Demo-load test (25–40 operators) → measured 06 §2 budget table replacing estimates.
- [S] Micro-help pages; print golden-file suite completion; §9A.4 timed UI tests in CI.

## Acceptance criteria
1. Reset script self-times < 30 s on the demo laptop.
2. Budget table columns updated with measured values (or the measurement blocked-reason recorded).
3. Offline checklist (07 §3) passes on a cold laptop.

## Out of scope
Sales collateral; §14 design-ceiling load (06 §5 scale-up path handles it).

## Risks / open questions
Measurement requires the compose stack on target-class hardware — see notes for what ran here.
