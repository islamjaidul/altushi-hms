# 0051 — Engineering skill set for production-grade delivery

- **Status:** Done
- **Date:** 2026-08-06
- **PRD ref:** §5, §7, §8 N1, §11, §12, §16
- **Scope:** in scope (full 22-module product, per CLAUDE.md hard rule 2)

## Problem

The project's agent skill set was written for a docs-and-MVP phase and has not kept up with a
codebase that now carries 15 module schemas, 89 test files and two shipping SKUs. Three concrete
consequences:

1. **`mvp-scope-check` is actively wrong.** It states MVP scope is *frozen* at §9A.2 and lists
   Pharmacy, IPD, OT and HR/Payroll as "explicitly deferred" — all four are built and shipped
   (specs 0022, 0025, 0034–0037). CLAUDE.md hard rule 2 still routes scope questions to it, so the
   canonical scope gate tells an engineer to defer work that already exists in `src/Modules/`.

2. **No skill governs schema or index design**, and drift is measurable: six `ILIKE` search
   surfaces exist and exactly one is backed by an index (`ix_patient_name_trgm` on `reg.patient`).
   Patient search on a family phone, drug search in the pharmacy, employee search in HR, the payer
   filters on Dues and Refund, and the four-column audit search all run unindexed substring
   matches. On the §16 box (2 vCPU / 3 GB) at the §14 volumes, that is a sequential scan per
   keystroke against a table that grows for five years. Index density across the fifteen contexts
   ranges from 2 to 57 with no written rule distinguishing deliberate from accidental.

3. **Three disciplines the team relies on are unwritten**: test-driven development (one sentence
   at the foot of `code-conventions`), what makes a CRUD screen actually finished, and what a
   module owes other modules when a business event crosses a schema boundary. Each is currently
   carried by the memory of whoever wrote the last similar screen, which is how the six silent-write
   defects of spec 0037 and the cross-module gaps of spec 0042 arrived.

Who feels it: every engineer or agent starting a module, screen or migration; and ultimately the
operator, who meets the result as a slow search box, a screen that cannot edit what it created, or
a charge that never posted.

## Requirements

- [M] Scope routing reflects reality: the product is a full production-grade HMS ERP, not a frozen
  8-module MVP. Every referencing document points at the corrected gate.
- [M] A TDD skill states the order (failing test first), the tier rule for choosing where the first
  test goes, and what is exempt — reconciled with the Testcontainers cost of the integration tier.
- [M] A schema-and-indexing skill gives normative rules for entity shape, constraint placement,
  and which columns must carry an index, including the substring-search case.
- [M] A CRUD-completeness skill defines observable done-ness for a data-management screen, covering
  the read path, the write path, authorization, audit, concurrency and the tests for each.
- [M] A cross-module-flow skill states what a module owes another when a business event crosses a
  schema boundary, and how that obligation is proven.
- [S] A domain-modelling skill states where invariants live in this codebase's idiom, reconciling
  SOLID/DDD guidance with the standing prohibition on MediatR/CQRS/repository layers.
- [M] Every new skill is grounded in code that exists — cited files, real guards, real test classes.
  No rule is stated that the codebase does not already demonstrate or that this spec does not
  explicitly propose.
- [S] The index and CRUD rules become machine-checkable, so they hold without review attention.

## Acceptance criteria

1. `.claude/skills/` contains skills covering TDD, schema/indexing, CRUD completeness,
   cross-module data flow, and domain modelling; each has a frontmatter `description` that names
   the situations that should trigger it.
2. No skill in `.claude/skills/` asserts that the MVP is frozen or that a shipped module is
   deferred. Verified by grep for `frozen`, `deferred`, `§9A.2` across the skill set.
3. Every file that referenced the old scope gate by name resolves to the corrected skill —
   `CLAUDE.md`, `spec-flow`, `module-spec`, `adr-write`. Verified by grep returning no stale name.
4. Each new skill's factual claims are traceable: file paths cited in the skills exist. Verified by
   resolving each cited path.
5. The measured drift is recorded as follow-up work with enough detail to spec it, not silently
   fixed here.

### How each was verified (closed 2026-08-06)

1. Six skills present in `.claude/skills/`: `scope-routing` (rewritten), `tdd-loop`,
   `schema-and-indexing`, `crud-completeness`, `cross-module-flow`, `domain-modelling`. Each carries
   a trigger-shaped frontmatter `description`.
2. `grep -rn "mvp-scope-check\|§9A\.2\|MVP scope is" .claude/skills/` — the four surviving `§9A.2`
   hits are citation-format examples in `prd-lookup`, `spec-flow` and `adr-write`, plus
   `scope-routing`'s own statement that the freeze was lifted. No skill asserts a live freeze.
   The `spec-flow` spec template's `**MVP:** … deferred (per §9A.2)` field, which would have
   reproduced the stale frame in every future spec, is now `**Scope:**`.
3. `CLAUDE.md:24`, `spec-flow:25`, `module-spec:42`, `adr-write:43` all updated; grep for the old
   name across `*.md`/`*.json` returns only spec 0002, which is append-only history.
4. Scripted resolution of every `src/`, `tests/`, `eng/`, `docs/`, `.github/` path cited across the
   six skills: **0 missing**. Twenty cited types (`InputGateCoverageTests`, `WardMoneySeamTests`,
   `HmsPageModel`, `AuditWriter`, `InvoiceValue`, `Bounds`, the seven orchestrators, …) and nine
   cited methods all resolve in `src/` or `tests/`.
5. `notes.md` records the five unindexed search surfaces with file:line, the seven DbContexts
   outside the CI additive-migration gate, the index-density spread, and the three enforcement
   tests agreed for a follow-up spec.

## Out of scope

- **Fixing the five unindexed search surfaces.** Adding indexes is a schema change and gets its own
  spec under hard rule 0. This spec records the finding; it does not migrate.
- **Writing the enforcement tests.** The architecture tests that would fail on an unindexed FK, an
  unindexed `ILIKE` column, or a soft-delete entity queried without its filter are agreed in
  principle (see `notes.md`) and belong to a follow-up spec alongside the migrations they police.
- Changing the PRD, any ADR, or any production code.
- Revising the five skills found healthy (`prd-lookup`, `module-spec`, `spec-flow`, `adr-write`,
  `qa-lifecycle`) beyond the rename references in criterion 3.

## Risks / open questions

- **Skill-set size.** Six skills carrying architecture guidance risks the same rule appearing in two
  places and drifting apart. Mitigation: `code-conventions` stays the single home for the monolith
  layout, `HmsTx` and concurrency; the new skills cite it rather than restate it, and each owns a
  disjoint concern.
- **A documented rule the codebase violates is a liability**, because it reads as satisfied. The
  five unindexed searches are named explicitly in the schema skill as known-open, with a pointer to
  the follow-up spec, so the gap cannot be mistaken for compliance.
- **Renaming `mvp-scope-check`.** Recommended default, taken: rename to `scope-routing` and update
  the four referencing files, because a skill whose name says "MVP" will keep re-teaching the wrong
  frame no matter what its body says. Spec 0002 records the old name as history and is append-only;
  it is not edited.
