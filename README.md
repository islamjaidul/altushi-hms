# HMS ERP

Hospital Management System ERP for the **Bangladesh** private-hospital market (50–300 beds, private hospitals · clinics · diagnostic centres).

**Status:** architecture approved (19 ADRs) · S1–S7 implementation pass done · **live at [hms.specshipper.com](https://hms.specshipper.com)**. Remaining work is tracked per sprint in `docs/specs/` (UI screen pass, printer-hardware spikes, S6 load measurement, S7 rehearsals).

## Live demo & test credentials

Production demo: **https://hms.specshipper.com** — password for every account is `Demo#1234`. Each account shows only its role's modules (nav = permissions ∩ entitlements, enforced server-side).

| Username | Name | Role | Sees |
|---|---|---|---|
| `jashim` | Jashim Uddin | Receptionist | Registration, Appointments |
| `rasel` | Rasel Ahmed | Billing Operator | Billing (invoice, dues, day-close), Diagnostics order, patient directory |
| `ripon` | Ripon Das | Lab Technologist | LIS work board, sample collection, result entry |
| `farhana` | Dr. Farhana Rahman | Pathologist | LIS work board, result verification |
| `shahid` | Shahid Alam | Billing Supervisor | Billing + approvals inbox (discount/refund decisions) |
| `admin` | System Admin | Admin | Users & roles, audit viewer, approvals, masters, SMS tray |
| `md` | Dr. Chairman | MD | MD dashboard, approvals, audit viewer |

Demo data only — do not enter real patient information. Local run: `cd deploy && HMS_ENV=Development HMS_SEED=true docker compose up -d` → http://localhost:8080 (same accounts).

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
| `docs/architecture/` | Architect | All technical decisions: 19 ADRs, domain/data model, UI architecture, deployment, demo kit, build plan. |
| `docs/specs/` | Everyone | Spec archive — one directory per change (`spec.md`, `plan.md`, `tasks.md`). Specs 0005–0011 are the S1–S7 build records. |
| `src/`, `tests/` | Engineer | .NET 10 modular monolith (kernel + 8 module assemblies) and the four test projects (unit, architecture, integration on real Postgres, print-golden). |
| `deploy/`, `demo/`, `eng/` | Engineer | Compose stack + Dockerfile + runbook · demo-reset/snapshot kit · CI guard scripts. |
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

## Building & testing

Requires the .NET 10 SDK and Docker (integration tests run against real PostgreSQL via Testcontainers — SQLite proves nothing for the money paths and is banned for them).

```sh
dotnet build hms-erp.slnx                 # 0 warnings tolerated
dotnet test tests/Hms.Kernel.Tests        # fast unit
dotnet test tests/Hms.Architecture.Tests  # module-boundary rules (ADR-0003)
dotnet test tests/Hms.Integration.Tests   # money invariants, concurrency races, state machines
dotnet test tests/Hms.PrintGolden.Tests   # Bangla PDF rendering
sh eng/check-no-external-hosts.sh src && sh eng/check-ui-tokens.sh src && sh eng/check-fkeys.sh src
```
