# Spec Archive

Every change to this project is specified here **before** it is built, and the record is kept **after** it ships. This directory is the project's memory: why each change was made, what was decided, and what actually happened.

## Layout

```
docs/specs/
  README.md              ← this index
  NNNN-slug/
    spec.md              WHAT & WHY  — problem, requirements, acceptance criteria
    plan.md              HOW         — approach, files, steps (the approved plan, archived)
    tasks.md             checklist   — ticked off as work proceeds
    notes.md             AFTERWARDS  — deviations, surprises, follow-ups (only if any)
```

IDs are sequential and never reused. Slugs are kebab-case. Numbering starts at `0000-`, reserved for the retroactive baseline that records work predating this archive; new work starts at `0001-`.

**Retroactive specs.** A record written *after* the fact (because the work predates the archive) must say so in its header and must not invent a plan it never had:

```markdown
- **Retroactive:** yes — predates the spec archive, so no plan.md exists
```

That line exempts the spec from the "Approved+ must have `plan.md`" rule, but **only for finished work** (`Done` / `Superseded` / `Abandoned`). A `Draft`/`Approved`/`In Progress` spec cannot use it — live work must archive its plan going forward.

Use it only when no plan genuinely ever existed — never to skip archiving one that does.

> **The exemption is a self-attestation the integrity hook cannot verify.** Nothing proves a plan "never existed"; the hook only checks that the claim is well-formed and applied to finished work. Honesty here depends on review (`spec-auditor`), not automation — so do not read hook silence as proof that a retroactive claim is legitimate.

## Lifecycle

`Draft` → `Approved` → `In Progress` → `Done` (or `Superseded by NNNN` / `Abandoned — reason`)

A spec is never deleted or rewritten after `Done`. Corrections happen in a **new** spec that supersedes it — same rule as the PRD's no-hard-delete principle.

## How this relates to the other docs

| Doc | Holds |
|---|---|
| `docs/project_manager.md` (PRD) | Product truth — requirements, scope, personas. Changes to it need a spec. |
| `docs/architecture/01-adr/` | Technical **decisions** (ADRs) — durable, one per decision. |
| `docs/specs/` | Units of **work** — one per change. Links out to the PRD § it serves and any ADR it triggers. |

A spec cites the PRD section it implements; an ADR records a decision the spec surfaced. Don't duplicate content between them — link.

## Index

| ID | Title | Status | PRD ref | Date |
|---|---|---|---|---|
| [0000-prd-and-competitor-analysis](0000-prd-and-competitor-analysis/spec.md) | PRD authoring & competitor analysis (retroactive baseline) | Done | whole doc | 2026-07-26 |
| [0001-handoff-readiness](0001-handoff-readiness/spec.md) | Architect handoff readiness | Done | §16.3, §9A | 2026-07-26 |
| [0002-agent-tooling](0002-agent-tooling/spec.md) | Agent tooling: CLAUDE.md, skills, spec-auditor (retroactive) | Done | n/a | 2026-07-26 |

<!-- Add one row per spec, newest last. Keep Status in sync with the spec's own header. -->
