# 0001 — Architect handoff readiness

- **Status:** In Progress
- **Date:** 2026-07-26
- **PRD ref:** §16.3 (handoff checklist), §9A (MVP scope)
- **MVP:** enabling work — not a product module, so §9A.2 scope freeze does not apply

## Problem

The PRD (v1.1) and the architect prompt are complete, but the repository is not ready to hand to a Principal Software Architect:

1. **No version control.** Nothing tracks change, so spec-driven development cannot be enforced and the architect has no history to reason about.
2. **Spec discipline is advisory only.** Rule 0 in `CLAUDE.md` depends on an agent choosing to comply; nothing verifies it.
3. **No landing place for architect output.** `docs/architect_prompt.md` requires 10 deliverables in `docs/architecture/` (incl. `01-adr/`), but that tree does not exist.
4. **Planning history is outside the repo.** Prior plans live in `~/.claude/plans/`, violating the "all plans archived in docs" rule.
5. **No entry point.** An architect arriving cold has no README telling them what to read, in what order.

## Requirements

- [M] Git repository initialised with a sensible ignore set and a baseline commit
- [M] Deterministic (harness-executed) integrity check for the spec archive — not dependent on agent goodwill
- [M] `docs/architecture/` scaffold matching the 10 deliverables in `docs/architect_prompt.md`, with an ADR index
- [M] Prior planning history archived into `docs/specs/`, honestly labelled as retroactive
- [M] Root `README.md` giving a reading order for a cold start
- [S] Spec archive audited clean before handoff is declared

## Acceptance criteria

1. `git log` shows a baseline commit; `git status` is clean afterwards.
2. The integrity check runs automatically at end of turn and reports: `Approved`+ specs missing `plan.md`, specs missing from the README index, and spec dirs missing `spec.md`. It is advisory (never blocks work).
3. `docs/architecture/README.md` exists and enumerates deliverables 1–10 with their target paths; `01-adr/` contains an index.
4. `docs/specs/0000-*/` holds the archived prior plan, marked retroactive.
5. Root `README.md` names the reading order: README → PRD §9A → architect_prompt → specs.
6. `spec-auditor` reports no High-severity findings.

## Out of scope

- **Blocking** `PreToolUse` enforcement — deferred until source code exists; today it would only fire on doc edits (friction with no benefit).
- Any product feature, or the architect's own technical decisions.
- CI pipelines (no code to build yet).

## Risks / open questions

- **Committing to `main` directly.** For a repository's first import commit this is standard; branch-per-change applies from spec 0002 onward. *Default taken: baseline commit on `main`.*
- **Hook noise.** A Stop hook firing every turn can annoy. *Default taken: advisory (exit 0, prints only findings, silent when clean).*
