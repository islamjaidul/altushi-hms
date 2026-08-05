---
name: module-spec
description: Write or extend a module specification, sub-feature list, or user stories inside the HMS ERP PRD (docs/project_manager.md) in its established house style. Use when adding a module, adding sub-features to an existing one, or writing user stories and acceptance criteria for this project.
---

# Module spec authoring

Match the existing PRD style exactly — the doc is a handoff artifact and inconsistency costs the architect time. Read a neighbouring module first (`grep -n '^### Module ' docs/project_manager.md`) and mirror it.

## Shape of a module section

```markdown
### Module N — <Name>

**Responsibility:** One or two sentences. What this module *owns* — the entity or process no other module governs.

**Sub-features**
- [M] Must-have sub-feature
- [S] Should-have
- [C] Could-have

**User stories**
- **USN.1** As a <persona from §4>, I want <capability>, so that <outcome>.
  **AC:** Testable conditions. Only on stories where correctness is non-obvious.
```

## Rules

- **MoSCoW every sub-feature** — `[M]` / `[S]` / `[C]`. No untagged bullets.
- **Personas come from §4** (P1–P15) by name/role. Don't invent a persona; add one to §4 first if genuinely missing.
- **Story numbering follows the module number** (Module 9 → US9.1, US9.2 …).
- **Cross-module links** use `[links M15]` or `→ posts to folio (M6)` so the architect can trace the seam.
- **Source-tag live-observed items** with `[obs: MEDISpa]` / `[obs: PrimeMIS]` / `[obs: both]` — only with real evidence behind it. New enrichments from competitor systems go in **§5A**, not retro-fitted into §5.
- **Business language only.** No stack, schema, API, or library names — that's the architect's territory.
- Acceptance criteria state *observable outcomes* ("posting appears on folio within a minute; poster identity recorded"), not implementation.

## After editing the PRD

1. If you added a module, update the §5 domain map, the §10 data dictionary (owner/readers), §11 states if it introduces any, and §12 permissions.
2. If it introduces a workflow state, add it to §11 and mark approval-gated transitions with `⚿`.
3. Bump the changelog table at the top of the PRD.
4. Check what requirement it serves — a module the PRD does not describe is new scope for the PM, not ours to invent (see `scope-routing`).
