# Architecture

**Owner:** Principal Software Architect. This tree holds all technical decisions. Business requirements live in `docs/project_manager.md` (PRD) and must not be re-decided here — see the role boundary in `/CLAUDE.md`.

**Start here:** read `docs/architect_prompt.md`. It is the brief: mission, non-negotiable constraints, the 2 vCPU / 3 GB RAM budget, and the 34 edge cases to design for.

## Status

**All 10 deliverables drafted 2026-07-26** (spec `docs/specs/0003-mvp-architecture/`) — status *Draft for PM review*. ADR index with Q1–Q15 coverage: [`01-adr/README.md`](01-adr/README.md). Frontend design reference: `assets/altushi-hms-demo.html` (binding for `05-ui-architecture.md`). Memory-budget figures are estimates pending measured validation (build plan S6).

## Expected deliverables

| # | File | Contents |
|---|---|---|
| 1 | `00-architecture-overview.md` | Stack + justification vs the 3 GB budget, component diagram, how the PRD §6 folio/day-close spine is realised, how deferred modules plug in later |
| 2 | `01-adr/NNNN-*.md` | One ADR per decision. **Must cover PRD §16 Q1–Q15.** Index: `01-adr/README.md` |
| 3 | `02-domain-model.md` | Entities & lifecycles from PRD §10; state machines from §11 |
| 4 | `03-data-model.md` | Schema, keys, indexes, audit/versioning, UHID & invoice numbering |
| 5 | `04-api-and-module-boundaries.md` | Module contracts; where approval engine & notifications sit; the day-close→ledger audit boundary (§6.6) |
| 6 | `05-ui-architecture.md` | How PRD §7 is *enforced*; the ~25 high-frequency screens; print pipeline |
| 7 | `06-deployment.md` | Docker/compose layout, **memory budget table**, capacity ceiling + scale-up triggers, backup/restore, laptop-demo variant |
| 8 | `07-demo-kit.md` | Seed data (incl. seeded history), one-command reset, offline checklist, 20-min demo runbook |
| 9 | `08-build-plan.md` | Sprint plan to demo date, risks, dependency-ordered tasks |
| 10 | `09-questions-for-pm.md` | Business questions, each with a recommended default |

## Gate

**Stop after deliverable 2 (ADRs) for PM review.** Do not begin implementation until the stack, offline, and concurrency decisions are approved.

## Rules

- Every decision is budgeted against **2 vCPU / 3 GB RAM**; state the RAM cost of each component.
- Cite the PRD (`§8 N2`, `§9A.2`) rather than restating it.
- Mark estimates as estimates; never assert an unverified library capability or benchmark.
- Architecture work follows spec-driven development like everything else — see `/CLAUDE.md` Rule 0 and the `spec-flow` skill.
