# 0004 — Plan

## Approved: 2026-07-26 (direct user request in-session: "create a development prompt for a 15-year .NET staff engineer — best practices, design patterns, DB query standards, indexing, code-first, TDD (super important), security guardrails — for handoff")

1. Write `docs/staff_engineer_prompt.md` in the architect-prompt house style (ROLE / MISSION / INPUTS reading order / non-negotiable guardrails / working order / decide-vs-escalate / first-response format).
2. Ground every guardrail in the existing baseline: ADRs (0001–0019), `03-data-model.md` §6 money invariants, `04` boundary rules, `05` UI grammar, `06` memory budget, `08` sprint order — cite, don't copy.
3. Make TDD operational: name the invariants, the real-Postgres concurrency tests, the test pyramid, and the merge-blocking rules.
4. Record spec 0004 (this directory) and update the specs index.
