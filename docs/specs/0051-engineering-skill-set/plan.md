# 0051 — Plan

## Approved: 2026-08-06

Approved as four explicit decisions taken by the product owner on 2026-08-06, recorded verbatim:

| Decision | Choice |
|---|---|
| Stale scope gate | **Rewrite completely** — "it's not MVP anymore, we need to deliver a full fledged production grade ERP solution as HMS" |
| TDD gate strength | **Tiered, invariant-first** — first failing test at the cheapest layer that can hold the real invariant; mandatory for domain logic, money, state transitions, permissions; Razor markup and CSS exempt |
| Enforcement | **Skill + new architecture tests**, tests written in a follow-up spec |
| Drift found during audit | **Skills only; log the drift** for a follow-up spec |

## Approach

Six skills, each owning a disjoint concern, all citing `code-conventions` rather than restating it.
`code-conventions` remains the single home for monolith layout, `HmsTx`, singleton services and
concurrency; nothing here duplicates those.

| Skill | Owns | Status |
|---|---|---|
| `scope-routing` | is this ours to build, and against which requirement | rewrite of `mvp-scope-check` |
| `tdd-loop` | the order of writing, and which tier takes the first test | new |
| `schema-and-indexing` | entity shape, constraints, and which columns are indexed | new |
| `crud-completeness` | when a data-management screen is finished | new |
| `cross-module-flow` | what a module owes another across a schema boundary | new |
| `domain-modelling` | where invariants live; SOLID/DDD inside this codebase's idiom | new |

## Steps

1. **`scope-routing`** — `git mv .claude/skills/mvp-scope-check .claude/skills/scope-routing`,
   rewrite the body around the Phase-2 frame: the product is the full §5 22-module HMS ERP;
   the PRD defines what each module *is*; sequencing comes from
   `docs/architecture/11-build-plan-phase2.md`; genuinely new scope routes to
   `09-questions-for-pm.md`. Preserve the two override rules that are still true (design-for,
   money-model) and the anti-patterns. Update the four referencing files.

2. **`tdd-loop`** — the red-green-refactor order, the tier-selection table keyed to the five
   existing test projects, how to run one test without a full Docker round-trip, the exemption
   boundary, and the "state that you saw it fail" obligation lifted from `code-conventions:149`
   and given a home.

3. **`schema-and-indexing`** — schema-per-module ownership, `int` money, `DateTimeOffset` UTC,
   check constraints at the database not only the page model (the 0039 lesson), soft-delete shape.
   Index rules: every FK column, every list-page filter and sort column, every uniqueness
   invariant as a real unique index, partial indexes for the `Active`/`MergedInto` predicates,
   composite column order equality-then-range, and `gin_trgm_ops` for any `ILIKE '%…%'` column.
   Name the five known-open search surfaces explicitly so the gap cannot read as compliance.

4. **`crud-completeness`** — a definition-of-done checklist across read path, write path,
   authorization, audit, concurrency, and the test at each tier. Grounded in the 0037 silent-write
   failure (transaction committed without flushing) and the 0045 failure (a create path with no
   edit path).

5. **`cross-module-flow`** — the obligation catalogue: a clinical order must produce a charge, a
   dispense must decrement stock, a notification must be an outbox row in the same transaction, a
   terminal state must not orphan a dependent. How to prove it: the money-seam test pattern in
   `tests/Hms.Integration.Tests/WardMoneySeamTests.cs` and the verify-script thread.

6. **`domain-modelling`** — where an invariant belongs (database constraint > service method >
   page model), aggregate boundary = transaction boundary = one `HmsTx.RunAsync` call, when a
   value object earns its place, and the explicit reconciliation: SOLID here means small stateless
   services with one reason to change, not ports and adapters. DDD tactical patterns are welcome;
   DDD infrastructure patterns (repository over EF, MediatR, CQRS) remain an ADR question per
   `code-conventions`.

7. **`notes.md`** — record the five unindexed search surfaces with file and line, the index-density
   spread, and the two enforcement tests agreed for the follow-up spec.

8. **Verify** — resolve every file path cited in the new skills; grep the skill set for `frozen`,
   `§9A.2`, `mvp-scope-check`; confirm the spec index row.

## Files

- `.claude/skills/scope-routing/SKILL.md` (moved + rewritten)
- `.claude/skills/{tdd-loop,schema-and-indexing,crud-completeness,cross-module-flow,domain-modelling}/SKILL.md` (new)
- `CLAUDE.md`, `.claude/skills/{spec-flow,module-spec,adr-write}/SKILL.md` (reference updates)
- `docs/specs/0051-engineering-skill-set/{spec,plan,tasks,notes}.md`
- `docs/specs/README.md` (index row)

## Not done here

No migration, no architecture test, no production code. Both are named in `notes.md` as follow-up
specs.
