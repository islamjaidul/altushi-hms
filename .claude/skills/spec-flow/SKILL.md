---
name: spec-flow
description: Run the spec-driven development workflow for this project — create, update, or close a spec in docs/specs/ and archive the approved plan. Use before starting any non-trivial change (feature, module, PRD edit, architecture work, bug fix), when a plan is approved, and when work finishes. Also use when asked to check whether a change has been specified or archived.
---

# Spec-driven flow

**No non-trivial change is made without a spec in `docs/specs/`, and no spec is left unclosed after the work ships.** See `docs/specs/README.md` for layout and lifecycle.

## When a spec is required

Required for: a new module or screen · any PRD edit · architecture/ADR work · a change touching money, permissions, or audit · anything spanning more than one file or session.

Not required for: typo/formatting fixes, a single-line correction, or answering a question without changing anything. When unsure, write the spec — it costs one small file.

## The loop

**1. Before work — create the spec**

```bash
ls docs/specs/                              # next free ID
mkdir -p docs/specs/0007-opd-billing-screen
```
Write `spec.md` (template below). Status `Draft`. Add a row to the index table in `docs/specs/README.md`.
Route the scope first with the `scope-routing` skill — if no §5/§5A requirement covers it, the spec records the question to the PM instead of the build.

**2. On plan approval — archive the plan**

Copy the approved plan verbatim into `docs/specs/NNNN-slug/plan.md`, then set Status `Approved`. Plans that live only in a session or in `~/.claude/plans/` are **not** archived — the docs copy is the record. Add an `## Approved` line with the date.

**3. During work — keep `tasks.md` current**

Tick items as they complete. If reality departs from the plan, append to `notes.md` with the reason — do not silently rewrite `plan.md`; the plan is a record of what was agreed.

**4. After work — close it**

Set Status `Done`, confirm acceptance criteria are met (state how each was verified), update the README index, and record follow-ups in `notes.md`. If a technical decision was made, write the ADR (`adr-write` skill) and link it from the spec.

## Templates

**`spec.md`**
```markdown
# NNNN — <Title>

- **Status:** Draft | Approved | In Progress | Done | Superseded by NNNN | Abandoned
- **Date:** YYYY-MM-DD
- **PRD ref:** §<n> (the requirement this serves)
- **Scope:** in scope (§5 M<n>) | defect repair | question for the PM (see `scope-routing`)

## Problem
What is wrong or missing, and who feels it. No solution here.

## Requirements
- [M] …  (MoSCoW-tagged, business language)

## Acceptance criteria
1. Observable, testable outcomes — how we know this is done.

## Out of scope
What this spec deliberately does not cover.

## Risks / open questions
Anything needing a PM or architect decision, with a recommended default.
```

**`plan.md`** — the approved plan, verbatim. Head it with `# NNNN — Plan` and `## Approved: YYYY-MM-DD`.

**`tasks.md`**
```markdown
# NNNN — Tasks
- [ ] Task, in dependency order
- [ ] Verification step (how it was tested)
```

## Rules

- **Cite, don't copy.** Reference `§9A.2` or an ADR; never duplicate PRD text into a spec — duplicates drift.
- **Specs are append-only after `Done`.** Corrections go in a new spec that supersedes the old.
- **Keep them short.** A spec is a contract, not an essay — most fit on one screen.
- **Business language in `spec.md`**, technical detail in `plan.md`/ADRs. Same PM/architect boundary as the rest of the project.
- **The index in `README.md` must always match** the specs on disk. Use the `spec-auditor` agent to check.
