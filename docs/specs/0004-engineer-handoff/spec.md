# 0004 — Staff-engineer handoff prompt

- **Status:** Done
- **Date:** 2026-07-26
- **PRD ref:** §9A (scope being built), §7/§8/§11/§12 (binding inputs referenced)
- **MVP:** in scope (enables the build; adds no product scope)

## Problem

The architecture package (spec 0003) is complete, but there is no implementation-facing handoff: a Staff Software Engineer (human or agent) starting the build has no single brief stating the working method (TDD), engineering/database/security guardrails, escalation boundaries, and reading order. The architect prompt (`docs/architect_prompt.md`) plays this role for design; its counterpart for implementation is missing.

## Requirements

- [M] A self-contained, paste-ready prompt at `docs/staff_engineer_prompt.md`, mirroring the architect prompt's format.
- [M] TDD as the mandatory working method, with the money invariants and concurrency tests named as permanent executable specifications.
- [M] Security guardrails (deny-by-default authZ, parameterized data access, web hygiene, secrets/PII, supply chain) stated as non-negotiables.
- [M] Database standards: code-first additive migrations, indexing/EXPLAIN discipline, transaction and locking patterns per ADR-0015.
- [M] Clear decide-vs-escalate boundaries (engineer ↔ architect/ADRs ↔ PM) and a first-response format that gates implementation on an approved S1 spec.
- [S] References architecture docs by path and PRD by §, never duplicating their content.

## Acceptance criteria

1. `docs/staff_engineer_prompt.md` exists, self-contained, citing only real files/sections.
2. It contradicts no ADR and adds no product scope beyond §9A.2.
3. TDD requirements are testable statements (what blocks a merge), not slogans.

## Out of scope

The implementation itself (each sprint gets its own spec per the prompt's G1); any change to architecture docs or the PRD.

## Risks / open questions

None — content is derived entirely from spec 0003's outputs and standard practice; anything contentious is already routed to `09-questions-for-pm.md`.
