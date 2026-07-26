# HMS ERP

Hospital Management System ERP for the **Bangladesh** private-hospital market (50–300 beds, private hospitals · clinics · diagnostic centres).

**Status:** requirements complete, awaiting architecture. No implementation yet.

## Reading order

New here? Read in this order — don't start with the PRD, it's 1,350 lines.

1. **This file** — orientation.
2. **`docs/project_manager.md` §9A** — the frozen MVP scope (8 modules). If you read one section, read this one.
3. **`docs/architect_prompt.md`** — the architect brief: constraints, the 2 vCPU / 3 GB RAM budget, 34 edge cases, definition of done.
4. **`docs/project_manager.md`** — the full PRD, by section as needed (`grep -n '^## '` to navigate; never read it whole).
5. **`docs/specs/README.md`** — what has been decided and built, and why.

## Repository map

| Path | Owner | Contents |
|---|---|---|
| `docs/project_manager.md` | Project Manager | **The PRD** (v1.1) — requirements, 22 modules, personas, data flows, state machines, permissions, volumetrics. Single source of truth for *what* and *why*. |
| `docs/architect_prompt.md` | Project Manager | Handoff brief for the Principal Software Architect. |
| `docs/architecture/` | Architect | All technical decisions: ADRs, domain/data model, deployment. **Empty — awaiting architect.** |
| `docs/specs/` | Everyone | Spec archive — one directory per change (`spec.md`, `plan.md`, `tasks.md`). |
| `CLAUDE.md` | Everyone | Working rules for AI agents on this repo. Read it before contributing. |
| `.claude/` | Everyone | Agent tooling: skills, the `spec-auditor` agent, and the spec-integrity hook. |

## The product in one paragraph

An integrated hospital ERP whose differentiator is not its feature list — competitors already have the modules — but the **seams between them**: a doctor orders tests and they appear at the billing counter with no re-typing; payment prints sample barcodes; the lab resolves them by scan; verification fires the report-ready SMS; and every taka, including who approved which discount, lands on the Managing Director's dashboard. Built for operators aged 30–55 with low computer literacy, on infrastructure that loses power and internet regularly.

## Non-negotiables

- **English-only** operator UI · **BDT**, whole-taka entry · timezone **Asia/Dhaka**
- **No financial hard deletes** — corrections are reversals; audit is append-only
- **Prices are effective-dated** — a historical invoice always reproduces its historical price
- **MVP scope is frozen** at PRD §9A.2; additions go to the PM, never built silently
- Deployment target: **single VM, 2 vCPU / 3 GB RAM**

## How work happens here

Spec-driven. Every non-trivial change gets a spec in `docs/specs/` **before** it is built, and the approved plan is archived alongside it. See `CLAUDE.md` Rule 0. A `Stop` hook checks archive integrity automatically; the `spec-auditor` agent audits on demand.

## First task for the architect

Read `docs/architect_prompt.md`, then reply with the four-part first response it asks for (restated mission + top risks, preliminary stack with RAM costs, top 10 questions for the PM, deliverable sequence). **Stop after the ADRs for PM review** — do not begin implementation before the stack, offline, and concurrency decisions are approved.
