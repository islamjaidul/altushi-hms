# 0002 — Agent tooling: CLAUDE.md, skills, and the spec-auditor agent

- **Status:** Done
- **Date:** 2026-07-26
- **Retroactive:** yes — predates the spec archive, so no plan.md exists (see Known gap)
- **PRD ref:** n/a — repository tooling, not a product requirement
- **MVP:** n/a — does not touch §9A.2 product scope

> **Retroactive record.** Raised by the `spec-auditor` audit under
> [0001](../0001-handoff-readiness/notes.md), which correctly found that six tooling files were
> committed with no spec covering them. They were authored **before the spec archive existed**,
> so no spec could have preceded them — the archive and several of these files were created in
> the same session. Recorded here so the archive has no silent gap. Not a pre-work specification.

## Problem

Agents working on this repo needed to (a) navigate a 123 KB PRD without burning a context window,
(b) respect the PM/architect role boundary, (c) stop silently expanding the frozen MVP scope, and
(d) follow one consistent authoring style. Without shared instructions each session re-derived
these rules, inconsistently.

A second problem emerged once spec-driven development was requested: **instructions alone are
advisory** — a skill only helps if an agent chooses to invoke it.

## What was delivered

**Always-loaded (every session, ~5 KB total):**
- `CLAUDE.md` — project rules. Deliberately pointers, not content: it forbids reading the PRD
  whole and mandates `§`-section citation over line numbers (which drift). Rule 0 defines
  spec-driven development; Rules 1–5 cover the role boundary, frozen scope, evidence discipline,
  no financial hard deletes, and effective-dated prices.

**Loaded on demand (progressive disclosure — only the `description` line is resident):**
- `.claude/skills/prd-lookup/` — §-section map + grep recipes; the main token-saving lever.
- `.claude/skills/mvp-scope-check/` — the 8-module scope gate, including the subtle
  *design-for vs build* distinction from PRD §9's binding rule.
- `.claude/skills/adr-write/` — ADR template with the 3 GB RAM budget and a mandatory
  `Reversal trigger`.
- `.claude/skills/module-spec/` — PRD house style (MoSCoW tags, §4 personas, story numbering)
  and the four sections to update after adding a module.
- `.claude/skills/spec-flow/` — the spec-driven workflow and templates.
- `.claude/agents/spec-auditor.md` — read-only compliance auditor in its own context.

## Acceptance criteria

1. Permanent context cost stays small — **verified**: `CLAUDE.md` ~3.2 KB plus ~1.9 KB of skill
   and agent `description` lines; all bodies stay on disk until invoked.
2. Every skill's `name:` matches its directory, and the agent's matches its filename — **verified**
   by script; all 6 OK (a mismatch prevents loading).
3. Skills agree with `CLAUDE.md`, `README.md`, and `docs/specs/README.md` on paths, lifecycle
   states, and ADR location — **verified** by the `spec-auditor` audit ("Cross-doc agreement:
   checked clean").
4. The auditor is genuinely useful, not decorative — **verified the hard way**: on its first real
   run it found 4 issues in my own work, including a false-assurance bug in the integrity hook.

## Out of scope

- Additional skills for things Claude already does well — deliberately capped at 5. A
  "write good markdown" skill is pure token cost with no behavioural effect.
- Blocking enforcement hooks (deferred in [0001](../0001-handoff-readiness/spec.md)).

## Known gap

These files predate the archive, so there is **no plan.md** for this spec — nothing was planned
in advance to archive. Recording the absence rather than back-dating a fabricated plan.
