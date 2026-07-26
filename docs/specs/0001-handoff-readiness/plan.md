# 0001 — Plan

## Approved: 2026-07-26

Approach: complete the enabling work in dependency order, then audit before declaring handoff-ready.

1. **Git** — `git init`, `.gitignore` (ignore `.claude/settings.local.json`, OS/editor noise, future build output), baseline commit on `main`.
2. **Integrity script** — `.claude/hooks/spec-integrity.py`, dependency-free Python 3 (verified available). Checks: spec dirs have `spec.md`; `Approved`/`In Progress`/`Done` specs have `plan.md`; every spec dir appears in the README index. Prints findings, exits 0, silent when clean.
3. **Stop hook** — wire the script into `.claude/settings.json` (project-scoped and committed, unlike personal `settings.local.json`) using the `update-config` skill so the schema is correct rather than guessed.
4. **Architecture scaffold** — `docs/architecture/README.md` mapping deliverables 1–10 from `docs/architect_prompt.md` to paths, plus `01-adr/README.md` as the ADR index seeded with the required Q1–Q15 coverage.
5. **Archive prior plans** — `docs/specs/0000-prd-and-competitor-analysis/plan.md` from `~/.claude/plans/`, clearly marked reconstructed-retroactively (the PRD-authoring plan was overwritten by the later live-analysis plan; do not pretend otherwise).
6. **Root README** — reading order, repo map, doc ownership boundary, current status.
7. **Audit & close** — run the `spec-auditor` agent, fix any High findings, set spec 0001 `Done` with verification notes, update the index, commit.

## Files

- Create: `.gitignore`, `.claude/hooks/spec-integrity.py`, `.claude/settings.json`, `docs/architecture/README.md`, `docs/architecture/01-adr/README.md`, `docs/specs/0000-prd-and-competitor-analysis/plan.md`, `README.md`
- Edit: `docs/specs/README.md` (index), `docs/specs/0001-handoff-readiness/{spec,tasks,notes}.md`

## Verification

Run the integrity script directly (clean + deliberately-broken case), confirm the hook fires at end of turn, run `spec-auditor`, confirm `git status` clean.
