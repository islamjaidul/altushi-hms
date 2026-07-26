# 0011 — S7: Buffer, rehearsals, release candidate

- **Status:** Draft
- **Date:** 2026-07-26
- **PRD ref:** §9A (DoD gate)
- **MVP:** in scope

## Problem
S7 is the human sprint: rehearsal fix-list, two full 20-minute runbook rehearsals with a
non-team keyboard driver (edge 10), and the release-candidate tag gated on the 9-item MVP DoD
in `docs/architect_prompt.md`.

## Requirements
- [M] Two rehearsals with a non-team driver; fix-list burned down.
- [M] DoD checklist verified item-by-item; RC tagged only when all 9 hold.
- [S] Runbook set complete (deploy/RUNBOOK.md sections all verified on hardware).

## Acceptance criteria
1. DoD walkthrough recorded in notes.md with per-item evidence links.
2. RC tag exists; demo-reset + full-thread run green on the tagged image.

## Out of scope
New features of any kind (buffer is for fixing, not building).

## Risks / open questions
Cannot start until the UI pass and S6 measurement close — this spec stays Draft as the gate.
