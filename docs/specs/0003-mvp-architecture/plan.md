# 0003 — Plan

## Approved: 2026-07-26 (user exited plan mode after two refinement rounds: .NET/extensibility steer + Altushi HMS design reference)

# Execute the Architect Role — Produce All 10 `docs/architecture/` Deliverables

## Context

`docs/architect_prompt.md` is a self-contained handoff brief: an Enterprise Principal Software Architect must design the HMS ERP MVP (8 modules, PRD §9A.2) for a Bangladesh private hospital under construction, targeting a **single 2 vCPU / 3 GB RAM VM**, tolerant of power cuts and internet outages, and produce **10 deliverable documents** in `docs/architecture/` (which does not exist yet). The user has decided this session should **execute that role** and plan **all 10 deliverables in one pass** (the prompt's "stop after Deliverable 2" gate applies to writing *code*, which we are not doing — this is a docs-only repo; plan approval here serves as the PM review).

Nothing in `docs/architecture/` exists today. The PRD (v1.1) is complete and hands the architect 15 open questions (§16.2 Q1–Q15) to answer as ADRs.

## Governance (project hard rules that bind this work)

1. **Spec first (Rule 0):** create `docs/specs/0003-mvp-architecture/` via the `spec-flow` skill before writing any architecture doc — `spec.md` (what/why, cites §16.3 + architect_prompt.md), archive this plan as `plan.md` on approval, `tasks.md` = the 10 deliverables. Add the row to `docs/specs/README.md` index. Close to `Done` at the end.
2. **Role boundary (Rule 1):** no PRD edits. Business questions that surface go into `09-questions-for-pm.md`, never decided unilaterally. No business/scope decisions inside architecture docs.
3. **No fabrication (Rule 3 + prompt ground rule 3):** every RAM figure, library capability, and version number is either verified (WebSearch/WebFetch against real docs) or explicitly marked *estimate*. The memory budget table is labeled "estimated — to validate against measured container usage" since no code exists yet.
4. **Scope frozen (Rule 2):** design *seams* for excluded modules (folio, accruals, multi-branch — constraint C2) but design documents never expand the 8-module MVP.
5. Use skills: `spec-flow` (step 1), `adr-write` for every ADR, `prd-lookup` for citations (cite `§` refs, never line numbers).

## Preliminary architecture direction (to be validated in the ADRs, stated so the PM can veto early)

**PM steer (user, this session):** the 3 GB VM is the *MVP/demo* footprint only — production hardware will be substantially larger (rack-class server). So the stack must be an **industry-standard, enterprise-extendable** choice that *also* fits the small box, not a small-box specialist. The user named **.NET Core** as the exemplar, and local development/testing happens on **macOS**.

Direction (formalised and RAM-validated in the ADRs):

- **Stack: .NET (ASP.NET Core, current LTS)** — industry standard for enterprise ERP work, fully cross-platform (develops and tests natively on macOS incl. Apple Silicon; deploys in Linux containers), strong long-term hiring/support story, and a trimmed ASP.NET Core service realistically fits a ~300–600 MB app budget (figure to be verified/measured in the stack ADR, marked estimate until then). ORM: EF Core with migrations.
- **Modular monolith, single runtime process** for the MVP — one ASP.NET Core host with strict in-process module boundaries (one project/assembly per module, communicating via internal contracts + a mediator/domain-event layer). This is the extensibility mechanism: on big production hardware the same boundaries scale up (more workers, read replicas) and, if ever needed, individual modules can be split out along the already-enforced seams without re-architecture. The application-architecture ADR states this scale-up path explicitly.
- **PostgreSQL** as the only stateful service on the MVP box — also covers queueing (`SKIP LOCKED` job table), type-ahead search (trigram indexes), and audit storage, eliminating Redis/Elasticsearch-class extras from the 3 GB budget. On production hardware the same schema simply gets more RAM/replicas; nothing to migrate.
- **Server-rendered web UI (Razor) with progressive enhancement** (thin JS for type-ahead/barcode/keyboard grammar) — low RAM, works on 1366×768 counter PCs, no CDN on the critical path (offline demo). The UI-architecture doc keeps a stated seam for a richer front-end later if scale demands it.
- **LAN-first offline model:** the hospital LAN + local VM *is* the offline story (internet outage ≠ LAN outage); browser-side capture queues are scoped narrowly if at all. Q2's ADR does the honest analysis.
- **Dev/test environment:** everything runs on the user's Mac — .NET SDK natively or the same Docker Compose stack via Docker Desktop; the compose file doubles as the presenter-laptop demo variant (deliverable 7's laptop requirement), so "runs on the Mac" is verified continuously, not at the end.
- The stack ADR still records the alternatives considered (Go, Django/Rails, Phoenix) and the reversal triggers, per ADR discipline — but the decision criterion now weights enterprise extensibility and ecosystem maturity alongside the RAM fit, per the PM steer above.

## Frontend design reference (user-supplied, binding for 05-ui-architecture)

`~/Downloads/Altushi HMS (standalone).html` — a self-contained interactive design demo ("Altushi HMS", Vue-style single page, role-switchable). Decoded and analysed; it defines:

- **Shell:** grouped left sidebar (13 groups ≈ PRD module map, per-department colour dots), top bar (breadcrumb, global search, "Viewing as" role switcher, notifications, user chip), status footer (server/DB state, user+role, counter, F-key hints, date). 9 roles with per-role nav filtering — implements PRD U1 directly.
- **Design tokens:** font *Public Sans* (+ *Material Symbols Outlined* icons; a bundled Bengali-capable woff2 for Bangla output); neutral sage-grey scale (`#17211F` text → `#E9EEEC` surfaces), primary blue `#1B5E9C` (active `#22456B`, dark `#134A7D`), danger `#B3403F`, success `#2F7D4F`, amber `#A9711C`; `DEPT_COLORS` accent per module group.
- **Patterns to adopt:** KPI stat tiles; one reusable list/register template ("same list template reused across registers, ledgers, setup screens"); POS billing layout (catalogue left / cart right, F2/F3/F9/F10 shortcuts, hold/recall); kanban-style LIS sample pipeline advanced by barcode scan; bed board; approvals inbox; single letterhead system for every printable (money receipt, prescription, lab report, IPD bill, daily collection, payslip — hospital identity configured once in Settings, "In words: … Taka Only"); toasts.
- **~49 named page ids** covering the full ERP. The **MVP subset** (registration, patients, appointments, opd-billing, due-collection, refunds/approvals, test-order, lis, result-entry, report-delivery, daily-collection/day-close, dashboard, reports, users, roles, settings) maps 1:1 onto the 8 MVP modules; the rest are the post-MVP screens — the design itself validates the C2 extension seams.
- Demo data is already Bangladesh-correct: ৳, bKash/Nagad tenders, referrer capture, Sylhet localities, BEFTN payslip note — reuse as seed-data reference for `07-demo-kit.md`.

Actions: copy the file into `docs/architecture/assets/altushi-hms-demo.html` so the reference is durable; `05-ui-architecture.md` adopts its shell, tokens and patterns as the binding UI spec (mapped to §7's 15 principles) instead of inventing a new grammar; `07-demo-kit.md` mirrors its seed-data flavour.

## Work plan — the 10 deliverables, in the prompt's order

All files under `docs/architecture/`. Each deliverable states its design response to the relevant subset of the prompt's **34 edge cases** (that list is a rubric, not an appendix — every item gets a stated response somewhere, and each deliverable ends with a coverage note saying which ones it discharged).

| # | File | Key content | Primary PRD inputs |
|---|---|---|---|
| 1 | `00-architecture-overview.md` | Chosen stack w/ RAM justification; component diagram (mermaid); how the §6 integration spine (folio + counter day-close → ledger holding structure) is realised; how excluded modules plug in later (C2 seams: folio-ready encounter/charge model, accrual-ready consultant events, branch-scoped keys); demo topology (VM + presenter laptop) | §6, §9A, §8, §14 |
| 2 | `01-adr/` (~18 ADRs, one file each via `adr-write`) | See ADR inventory below | §16.2, §8, §12 |
| 3 | `02-domain-model.md` | Entities/relationships/lifecycles from §10; explicit state machines from §11 incl. Blocked/due-hold and bill Edit/Reset/Refund/Special-Discount request states; folio-ready + accrual-ready seams shown concretely (which MVP tables already carry the folio FK shape) | §10, §11, §5A |
| 4 | `03-data-model.md` | Concrete schema: tables, keys, indexes, constraints; UHID generation; gap-free fiscal-year-resetting invoice/receipt numbering (edge cases 15–16); effective-dated rate plans (C6, edge 13); append-only audit + versioning mechanism (Q7); rounding rule (edge 30); business-day boundary config (edge 16) | §10, §11, §14 |
| 5 | `04-api-and-module-boundaries.md` | Internal module contracts; approval engine (C7) and notification dispatch as shared services; day-close posting boundary — modules post summaries, never write ledger entries directly (§6.6) | §6.6, §5 M20/M21 |
| 6 | `05-ui-architecture.md` | How §7's 15 principles are *enforced* (component grammar, patient banner, Save/Cancel placement, keyboard map, type-ahead + barcode input handling, print pipeline: thermal 58/80mm + A4 + PDF fallback, Bangla render path for customer text — edge 9); names the ~25 high-frequency screens (§7.3) with interaction contracts | §7, §9A.4 |
| 7 | `06-deployment.md` | Compose layout + per-container memory limits; **the memory budget table** (≤ 2.6 GB, ≥ 400 MB headroom, no swap — estimates flagged); reverse proxy/TLS; healthchecks/restart/log rotation; backup + scripted restore drill (edge 8); disk retention/alerting (edge 32); NTP/clock stance (edge 31); honest capacity ceiling + scale-up triggers + upgrade path to 30–100 operators without re-architecture; laptop-demo variant (edges 1, 6, 7) | §8, §14, budget section |
| 8 | `07-demo-kit.md` | Seed dataset spec (realistic BD hospital: departments, doctors, ~200 tests w/ prices+TATs, beds, per-role users, seeded historical transactions — edge 4); one-command reset w/ target seconds (edge 5); offline demo checklist (edges 1–3, 10); 20-min golden-thread runbook (§9A.2 seam) | §9A, edge cases 1–10 |
| 9 | `08-build-plan.md` | Sprint-by-sprint plan (relative: Sprint 1…N — no demo date exists in the PRD, so the timeline is expressed in sprints with the golden thread demonstrable earliest); dependency-ordered tasks; risks; scope-cut *recommendations* flagged to PM, never enacted | §9A, DoD list |
| 10 | `09-questions-for-pm.md` | Every genuine business question found en route, each with a recommended default (fiscal-year convention, business-day boundary default, delegation policy for absent approvers — edge 19, retention windows, demo dataset realism vs. anonymity) | §16.3 "PM-owned" list |

### ADR inventory (deliverable 2 — each: context, options, decision, consequences, reversal trigger)

Q-answering: **Q1** deployment model (on-prem VM primary + hosted variant) · **Q2** offline/outage strategy (also C8) · **Q3** multi-branch/multi-tenancy readiness (also N9) · **Q4** device-integration approach (local agent pattern; MVP only printers/scanners per §13 phase 1) · **Q5** printing/reporting engine (N8 pixel-fidelity + thermal + PDF fallback) · **Q6** data-migration tooling · **Q7** audit depth & storage · **Q8** patient-portal readiness · **Q9** backup/DR · **Q10** Bangla text path (fonts/encoding through PDF + print) · **Q11** concurrency control (N4: serials, dues, sequences) · **Q12** module entitlement/licensing toggles · **Q13** BEFTN batch export readiness (forward-looking — data model only) · **Q14** TDS/VAT engine placement (forward-looking) · **Q15** auth hardening (2FA, idle lock, dynamic menu trees, shared-login mitigation — edge 29).

Plus non-Q ADRs the prompt requires: **stack choice**, **database choice**, **application architecture (modular monolith)**, and one for **ID/numbering strategy** (UHID + gap-free invoice sequences — it's contentious enough to deserve its own record).

## Execution order

1. `spec-flow`: create spec 0003, archive this plan, tasks checklist.
2. Deliverables in order 1 → 10 (decisions get made while drafting 00, formalised as ADRs in 01; later docs cite ADRs rather than re-arguing them). Verify any external factual claim (library capability, RAM figure) before writing it, or mark it estimate.
3. Maintain an edge-case coverage checklist (all 34) in the spec's `tasks.md`; no deliverable is done while its edge cases lack a stated response.
4. Close spec 0003 (`Done`, notes.md for deviations), update specs index.

## Verification

- **Prompt DoD check:** `docs/architecture/` deliverables 1–10 exist (DoD bullet); memory budget table present and totals ≤ 2.6 GB with headroom stated (validation against *measured* usage is explicitly deferred to implementation and marked so).
- **Edge-case sweep:** grep each of the 34 edge-case topics against the deliverables; every one has a designed response or an explicit reasoned deferral.
- **Q1–Q15 sweep:** every §16.2 question maps to exactly one ADR file.
- **Run the `spec-auditor` agent** at the end — spec 0003 closed, plan archived, index in sync, no PRD contamination (no tech leaked into `docs/project_manager.md`, no business decisions inside `docs/architecture/`).
- Consistency: every `§` citation in the new docs resolves to a real PRD section (`grep -n '^## \|^### ' docs/project_manager.md`).

## Estimated shape of the output

~14 files (10 deliverables with `01-adr/` holding ~18 short ADRs, plus spec 0003's four files). This is a large documentation effort — likely the longest single piece is `03-data-model.md`; the ADR set is the intellectual core.
