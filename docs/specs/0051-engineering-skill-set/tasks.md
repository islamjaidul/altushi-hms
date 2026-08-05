# 0051 — Tasks

- [x] Rename `mvp-scope-check` → `scope-routing` (`git mv`) and rewrite the body for the full-product frame
- [x] Update the four references: `CLAUDE.md:24`, `spec-flow:25`, `module-spec:42`, `adr-write:43`
- [x] Write `tdd-loop`
- [x] Write `schema-and-indexing`
- [x] Write `crud-completeness`
- [x] Write `cross-module-flow`
- [x] Write `domain-modelling`
- [x] Correct the two factual errors found in `code-conventions` (the `check-fkeys.sh` guard row),
      and add the sibling-skill pointer so the six do not drift apart
- [x] Replace the stale `**MVP:** … (per §9A.2)` field in the `spec-flow` spec template with `**Scope:**`
- [x] Record the measured drift in `notes.md` with file:line
- [x] Verify: every `src/`, `tests/`, `eng/`, `docs/`, `.github/` path cited across the six skills
      resolves on disk — 0 missing
- [x] Verify: 20 cited types and 9 cited methods all exist in `src/` or `tests/`
- [x] Verify: no skill says `mvp-scope-check`, "MVP scope is", or treats `§9A.2` as a live
      constraint; the four remaining `§9A.2` mentions are citation-format examples plus the
      lifted-freeze statement in `scope-routing` itself
- [x] Add the index row to `docs/specs/README.md`; set Status `Done`
