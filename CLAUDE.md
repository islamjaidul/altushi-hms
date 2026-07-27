# hms-erp

Hospital Management System ERP product for the **Bangladesh** private-hospital market (50–300 beds). Currently **docs-only / pre-implementation**.

## Files

| File | Role |
|---|---|
| `docs/project_manager.md` | The PRD (v1.1) — single source of truth for requirements. 1,350+ lines. |
| `docs/architect_prompt.md` | Handoff prompt for the Principal Software Architect. |
| `docs/specs/` | **Spec archive** — one dir per change: `spec.md` (what/why), `plan.md` (approved plan), `tasks.md`, `notes.md`. Index in `docs/specs/README.md`. |
| `docs/architecture/` | Architect's output (ADRs, models, deployment). Does not exist yet. |

## Token discipline

**Never read `docs/project_manager.md` whole** (~123 KB). Grep for the section you need:
`grep -n '^## 9A\.' docs/project_manager.md` then read with `offset`/`limit`. Use the `prd-lookup` skill.
Cite sections as `§9A.2`, never line numbers — they drift.

## Hard rules

0. **Spec-driven development.** No non-trivial change without a spec in `docs/specs/` **first**, and no change left unarchived after. On plan approval, copy the plan to `docs/specs/NNNN-slug/plan.md` — a plan that exists only in a session is not archived. Applies to features, PRD edits, and architecture work; not to typos or answering questions. Invoke the **`spec-flow`** skill for the workflow, and the **`spec-auditor`** agent to check compliance. Specs are append-only once `Done` — corrections supersede, never overwrite.
1. **Role boundary.** The PRD is a PM document: business requirements only, no technology decisions. Stack/DB/deployment belong to the architect in `docs/architecture/`. Don't leak tech choices into the PRD, or business/scope decisions into architecture docs.
2. **Scope is the full 22-module product of PRD §5** — the PM lifted the §9A.2 MVP freeze on 2026-07-27 (`docs/architect_review_prompt.md`; sequencing in `docs/architecture/11-build-plan-phase2.md`). The PRD still defines what each module *is*: implement §5/§5A, never invent requirements; genuinely new scope goes to the PM (`09-questions-for-pm.md`). Use `mvp-scope-check` for routing.
3. **No fabrication.** Competitor findings carry `[obs: MEDISpa]` / `[obs: PrimeMIS]` / `[obs: both]` source tags; every tag traces to a captured artifact. Never add a tag without evidence, and never assert a competitor feature, regulation, or library capability that isn't verified. Mark estimates as estimates.
4. **No financial hard deletes**, ever — corrections are reversals. Every financial/clinical write is attributable (user + time); audit is append-only.
5. **Prices are effective-dated.** A historical invoice must always reproduce its historical price.

## Product constants

- English-only operator UI · **BDT**, whole-taka entry · timezone **Asia/Dhaka** (no DST)
- Operators are **30–55, non-technical, low typing speed** — PRD §7's UX principles are binding requirements
- Deployment target: **single VM, 2 vCPU / 3 GB RAM** (a real design constraint, see §16 + `architect_prompt.md`)
- Must tolerate power cuts and internet outages (PRD §8 N2)

## Conventions

- Markdown docs use `§` section refs and `[M]`/`[S]`/`[C]` MoSCoW tags — match the existing style when editing.
- User stories: `As a <persona>, I want <capability>, so that <outcome>`, with `**AC:**` on critical ones.
- Bump the PRD changelog table at the top when changing the PRD.
