# 0001 — Tasks

- [x] Write `spec.md` and archive `plan.md` (this spec, per Rule 0)
- [x] `.gitignore` — ignores `.claude/settings.local.json`, secrets, OS/editor noise, future build output
- [x] `git init` on `main`; verified `settings.local.json` is ignored via `git check-ignore`
- [x] `.claude/hooks/spec-integrity.py` — dependency-free Python 3
- [x] Verified script: clean case silent (0 bytes); broken case emits HIGH for `Approved` spec without `plan.md`
- [x] Fixed false positive — ID regex matched dates (`2026-07-26`); slug must now start with a letter. Regression-tested both directions
- [x] Emit `systemMessage` JSON when run as a hook (plain text when run from a tty) so findings actually surface
- [x] `.claude/settings.json` — project-scoped `Stop` hook, 15s timeout, wrapped `|| true` so it can never break a turn
- [x] Validated hook nesting with `jq -e` (exit 0) and confirmed `settings.local.json` still parses
- [x] Pipe-tested the exact command string from settings.json
- [x] `docs/architecture/README.md` — deliverables 1–10 mapped to paths, with the post-ADR review gate
- [x] `docs/architecture/01-adr/README.md` — ADR index with Q1–Q15 coverage checklist
- [x] Archived prior plan → `docs/specs/0000-.../plan.md`, **credentials redacted**, provenance stated
- [x] `docs/specs/0000-.../spec.md` — retroactive baseline record, honest about the unrecoverable v1.0 plan
- [x] Root `README.md` — reading order, repo map, non-negotiables, first task for the architect
- [x] Updated `docs/specs/README.md` index
- [x] Baseline commit
- [x] `spec-auditor` run — no High findings
