# 0001 — Notes

## How each acceptance criterion was verified

| AC | Verified by |
|---|---|
| 1. Baseline commit; clean tree | `git log --oneline` → one commit `8c54002`; `git status --short` empty. `git check-ignore -v .claude/settings.local.json` confirms the personal settings file is excluded. |
| 2. Integrity check runs at end of turn, reports the three classes, never blocks | Pipe-tested with the exact command string from `settings.json` (`echo '{}' \| python3 …`): emits `systemMessage` JSON when findings exist, **0 bytes when clean**, `exit=0` always. Deliberately-broken fixture (an `Approved` spec with no `plan.md`) produced the expected HIGH. `jq -e` confirms hook nesting. |
| 3. `docs/architecture/` scaffold + ADR index | `docs/architecture/README.md` maps deliverables 1–10 to paths and states the post-ADR review gate; `01-adr/README.md` carries the Q1–Q15 coverage checklist. |
| 4. Prior plan archived, marked retroactive | `docs/specs/0000-prd-and-competitor-analysis/plan.md`, with a provenance header. Credentials redacted — verified by grepping the output for the secrets (`CLEAN`). |
| 5. Root README names the reading order | `README.md` → orientation → PRD §9A → architect_prompt → PRD by section → specs. |
| 6. `spec-auditor` reports no High findings | **Initially failed** — see below. Re-verified after fixes. |

## Audit outcome (the interesting part)

The `spec-auditor` agent was run before closing and returned **4 findings (1 High, 2 Medium, 1 Low)**. All four were legitimate; all four were fixed rather than argued with.

| Finding | Resolution |
|---|---|
| **High** — the 5 skills and the `spec-auditor` agent were committed with no spec covering them. Spec 0001 referenced them as tools it *used*, never as artifacts it *built*. | Correct: they were authored in a session **before** the spec archive existed, so no spec could have preceded them. Recorded retroactively as [0002](../0002-agent-tooling/spec.md) rather than pretending 0001 covered them. |
| **Medium** — index said `Done`, `spec.md` header said `In Progress`. | The closing step had not actually run. Header flipped to `Done` with this file as the verification record. |
| **Medium** — the integrity hook only checked that a spec's *name* appeared in the index, never that the **Status matched** — which is exactly why it stayed silent through the contradiction above. | Real gap in my own checker. Added a status-parity check (parses the index row's Status cell and compares to the header). Confirmed it now catches the very mismatch it previously missed. A checker that gives false assurance is worse than no checker. |
| **Low** — `docs/specs/README.md` said IDs start at `0001-` while the archive starts at `0000-`. | Wording corrected to explain `0000` as the retroactive baseline. |

## Lesson worth keeping

The hook passed silently while the archive was actually inconsistent. **Silence from an automated check is only as trustworthy as the checks it actually performs** — the auditor caught what the hook could not, which is the argument for keeping both layers rather than relying on the cheap one.

## Follow-ups (not blocking handoff)

- A **blocking** `PreToolUse` hook is still deferred until source code exists (PRD-side rationale in `spec.md` → Out of scope). Revisit when the architect's first code lands.
- Competitor crawl artifacts backing the `[obs: …]` tags live outside the repo and are not reproducible from it. The auditor flagged this as unverifiable and it is — noted in [0000](../0000-prd-and-competitor-analysis/spec.md) as a known gap.
