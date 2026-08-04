# Spec Archive

Every change to this project is specified here **before** it is built, and the record is kept **after** it ships. This directory is the project's memory: why each change was made, what was decided, and what actually happened.

## Layout

```
docs/specs/
  README.md              ← this index
  NNNN-slug/
    spec.md              WHAT & WHY  — problem, requirements, acceptance criteria
    plan.md              HOW         — approach, files, steps (the approved plan, archived)
    tasks.md             checklist   — ticked off as work proceeds
    notes.md             AFTERWARDS  — deviations, surprises, follow-ups (only if any)
```

IDs are sequential and never reused. Slugs are kebab-case. Numbering starts at `0000-`, reserved for the retroactive baseline that records work predating this archive; new work starts at `0001-`.

**Retroactive specs.** A record written *after* the fact (because the work predates the archive) must say so in its header and must not invent a plan it never had:

```markdown
- **Retroactive:** yes — predates the spec archive, so no plan.md exists
```

That line exempts the spec from the "Approved+ must have `plan.md`" rule, but **only for finished work** (`Done` / `Superseded` / `Abandoned`). A `Draft`/`Approved`/`In Progress` spec cannot use it — live work must archive its plan going forward.

Use it only when no plan genuinely ever existed — never to skip archiving one that does.

> **The exemption is a self-attestation the integrity hook cannot verify.** Nothing proves a plan "never existed"; the hook only checks that the claim is well-formed and applied to finished work. Honesty here depends on review (`spec-auditor`), not automation — so do not read hook silence as proof that a retroactive claim is legitimate.

## Lifecycle

`Draft` → `Approved` → `In Progress` → `Done` (or `Superseded by NNNN` / `Abandoned — reason`)

A spec is never deleted or rewritten after `Done`. Corrections happen in a **new** spec that supersedes it — same rule as the PRD's no-hard-delete principle.

`Done` means **the specified work was produced** — not that its artifacts passed external (e.g., PM) review. Review outcomes live in the artifacts' own status headers; post-close outcomes (audits, follow-ups) are appended to the spec's `notes.md`.

## How this relates to the other docs

| Doc | Holds |
|---|---|
| `docs/project_manager.md` (PRD) | Product truth — requirements, scope, personas. Changes to it need a spec. |
| `docs/architecture/01-adr/` | Technical **decisions** (ADRs) — durable, one per decision. |
| `docs/specs/` | Units of **work** — one per change. Links out to the PRD § it serves and any ADR it triggers. |

A spec cites the PRD section it implements; an ADR records a decision the spec surfaced. Don't duplicate content between them — link.

## Index

| ID | Title | Status | PRD ref | Date |
|---|---|---|---|---|
| [0000-prd-and-competitor-analysis](0000-prd-and-competitor-analysis/spec.md) | PRD authoring & competitor analysis (retroactive baseline) | Done | whole doc | 2026-07-26 |
| [0001-handoff-readiness](0001-handoff-readiness/spec.md) | Architect handoff readiness | Done | §16.3, §9A | 2026-07-26 |
| [0002-agent-tooling](0002-agent-tooling/spec.md) | Agent tooling: CLAUDE.md, skills, spec-auditor (retroactive) | Done | n/a | 2026-07-26 |
| [0003-mvp-architecture](0003-mvp-architecture/spec.md) | MVP architecture — all 10 architect deliverables, Q1–Q15 as ADRs | Done | §16, §9A, §8, §6 | 2026-07-26 |
| [0004-engineer-handoff](0004-engineer-handoff/spec.md) | Staff-engineer handoff prompt (TDD, guardrails, escalation) | Done | §9A | 2026-07-26 |
| [0005-mvp-build-execution](0005-mvp-build-execution/spec.md) | MVP build execution plan & S1 walking skeleton + spikes | In Progress | §9A, §7, §8, §11, §12 | 2026-07-26 |
| [0006-money-spine](0006-money-spine/spec.md) | S2: money spine — registration, invoice, payment | Done | §9A.2, §11, §5A | 2026-07-26 |
| [0007-diagnostics-approvals](0007-diagnostics-approvals/spec.md) | S3: diagnostics + approval engine | Done | §9A.2, §11, §12 | 2026-07-26 |
| [0008-lis-dayclose](0008-lis-dayclose/spec.md) | S4: LIS + delivery + day-close | Done | §9A.2, §11, §6.6 | 2026-07-26 |
| [0009-admin-hardening](0009-admin-hardening/spec.md) | S5: dashboard, admin, import, hardening | Done | §9A.2, §8, §12 | 2026-07-26 |
| [0010-demo-kit](0010-demo-kit/spec.md) | S6: demo kit + performance | In Progress | §9A.4, §14, §8 | 2026-07-26 |
| [0011-release-candidate](0011-release-candidate/spec.md) | S7: buffer, rehearsals, release candidate | Draft | §9A | 2026-07-26 |
| [0012-ui-pass](0012-ui-pass/spec.md) | UI pass: working screens per Altushi reference | Done | §7, §9A.2 | 2026-07-26 |
| [0013-mvp-requirement-gaps](0013-mvp-requirement-gaps/spec.md) | Close the MVP requirement gaps ([M] items with no screen) + traceability matrix | Done | §5, §5A, §9A.2 | 2026-07-26 |
| [0014-phase2-review-and-plan](0014-phase2-review-and-plan/spec.md) | Architect review of the MVP + Phase-2 build plan (fourteen modules released) | Done | §5, §5A, §9, §9A.3 | 2026-07-27 |
| [0015-shared-input-layer](0015-shared-input-layer/spec.md) | Shared input layer (date/search/type-ahead) + Wave-0 safety rails | Done | §7, §12, §14 | 2026-07-27 |
| [0016-pharmacy](0016-pharmacy/spec.md) | M11 Pharmacy — outdoor core + multi-outlet stock spine (Wave 1) | Done | §5 M11, §5A-11, §11, §12 | 2026-07-27 |
| [0017-ipd-folio](0017-ipd-folio/spec.md) | M6 IPD & patient folio + R4 bill-block (Wave 2) | Done | §5 M6, §5A-8/9, R4, §11, §12 | 2026-07-27 |
| [0018-front-desk](0018-front-desk/spec.md) | M2 Front Desk / help desk (Wave 2) | Done | §5 M2, §12 | 2026-07-27 |
| [0019-public-displays](0019-public-displays/spec.md) | R3 public queue display + report-status self-lookup (Wave 2) | Done | §5A.2 R3, §8 N5 | 2026-07-27 |
| [0020-lifecycle-gaps](0020-lifecycle-gaps/spec.md) | Patient-lifecycle gaps found by end-to-end smoke testing (phone search, silent discharge with dues, invisible outdoor charges) | Done | §7, §5 M6, R4, §3.2 | 2026-07-27 |
| [0021-terminal-exits-and-double-submit](0021-terminal-exits-and-double-submit/spec.md) | Money stranded on death/absconded exits, and double-billed invoices | Done | §11, §5 M6, §3.2, §7 | 2026-07-27 |
| [0022-pharmacy-coverage](0022-pharmacy-coverage/spec.md) | Full pharmacy-module coverage: staff-sale tagging + audit-search defect | Done | §5 M11, §5A-11, §8 N5 | 2026-07-27 |
| [0023-measured-memory-and-golive](0023-measured-memory-and-golive/spec.md) | Measured memory, 90-day seeded history, and a rehearsed go-live (Wave-0 close) | Done | §14, §8, §16 | 2026-07-27 |
| [0024-emr-prescription](0024-emr-prescription/spec.md) | M5 Prescription & EMR + 5A-7 nursing charts (Wave 3) | Done | §5 M5, §5A-7, §11, §12 | 2026-07-28 |
| [0025-operation-theatre](0025-operation-theatre/spec.md) | M7 Operation Theatre — scheduling, team, completion billing (Wave 3) | Done | §5 M7, §11, §12 | 2026-07-28 |
| [0026-radiology](0026-radiology/spec.md) | M10 Radiology — modality worklist, templated reports, e-signed delivery (Wave 3) | Done | §5 M10, §5A-10, §11, §12 | 2026-07-28 |
| [0027-day-close-business-day](0027-day-close-business-day/spec.md) | Day-close compared a Dhaka business day against a UTC date (night-shift defect) | Done | §5 M4, P2, edge 16/17 | 2026-07-28 |
| [0028-qa-lifecycle-suite](0028-qa-lifecycle-suite/spec.md) | QA patient-lifecycle suite: canonical doc, role-driven runner, QA agent | Done | §5, §7, §12, §11, §14 | 2026-07-28 |
| [0029-lifecycle-suite-rerunnable](0029-lifecycle-suite-rerunnable/spec.md) | The lifecycle suite must survive being run twice (fixture return, crash-to-failure, nav-smoke) | Done | §8, §14 | 2026-07-28 |
| [0030-grant-drift-and-appointments-authz](0030-grant-drift-and-appointments-authz/spec.md) | Grant drift on the deployment, and a permission enforced nowhere | Done | §12, §3.2, §5 M3 | 2026-07-28 |
| [0031-lifecycle-coverage-high-gaps](0031-lifecycle-coverage-high-gaps/spec.md) | Close the High-severity lifecycle gaps (money invariants, control gates, concurrency) | Done | §5 M4/M6/M8/M9, §8, §11, §12 | 2026-07-28 |
| [0032-module-qa-sweep](0032-module-qa-sweep/spec.md) | Module-by-module QA sweep — per-module coverage on UI/e2e/business-logic axes | Done | §5, §5A, §7, §11, §12 | 2026-07-28 |
| [0033-user-guide](0033-user-guide/spec.md) | Operator user guide — per-role handbook + honest money-flow answer | Done | §4, §5, §6, §7 | 2026-07-29 |
| [0034-hrm-product-line](0034-hrm-product-line/spec.md) | HRM as a dual-SKU product line (M16 HR & Payroll, sellable standalone) | In Progress | §5 M16, §5A-16/17, §3.4, §10, §11, §12 | 2026-07-31 |
| [0035-hrm-platform](0035-hrm-platform/spec.md) | Wave 0 — shared UI library, transaction seam, entitlement enforcement, second host | Done | §5 M16, ADR-0025/0026 | 2026-07-31 |
| [0036-hrm-operable](0036-hrm-operable/spec.md) | The HRM SKU has to be operable by an administrator — superuser grants, Users and Roles on both hosts, org-master CRUD, employee record, UI defects, 100-person demo seed | Done | §5 M16, §5 M21, §7, §12 | 2026-08-02 |
| [0037-hrm-silent-writes](0037-hrm-silent-writes/spec.md) | Six HR screens report success and save nothing — the transaction commits without flushing; plus two 500s on ordinary input and the roster's lost week | Done | §5 M16, §5 M21, §7, §11, §12 | 2026-08-02 |
| [0038-full-qa-audit](0038-full-qa-audit/spec.md) | Full product QA audit — every built module, route by route, PRD-cross-checked, for senior-engineer handoff | Done | §5, §5A, §7, §11, §12, §16 | 2026-08-02 |
| [0039-lifecycle-hardening](0039-lifecycle-hardening/spec.md) | Close the 0038 findings and the schema gaps behind them — input tier, schema constraints, payroll, tenancy | Done | §5, §5A, §7, §8, §11, §12, §16 | 2026-08-03 |
| [0040-login-validation-and-return-url](0040-login-validation-and-return-url/spec.md) | Login validation the operator can see (the browser was refusing the submit), and sign-out that remembers the screen | Done | §7, §12, §8 N2 | 2026-08-03 |
| [0041-nursing-station](0041-nursing-station/spec.md) | R5 Nursing Station — ward monitor, MAR from prescription + overdue visibility (LC-NUR-06), care tasks, ward duty assignment, indoor prescribing | Done | §5 M5/M6/M16, §5A-7, §5A.2 R5, §7, §12 | 2026-08-03 |
| [0042-nursing-cross-module-hardening](0042-nursing-cross-module-hardening/spec.md) | Cross-module audit closure: consultant-visit entry+charge, diagnostics-counter folio branch, terminal-exit orphans, branch/state write guards, allergy surface, Rx→pharmacy link, money-seam tests | Done | §5 M6, §5 M5, §11, §12 | 2026-08-03 |
| [0043-front-desk-friction](0043-front-desk-friction/spec.md) | Front-desk friction found in operator testing: a shared family phone reported as a duplicate, "FRONT DESK" rendered twice while "Indoor (IPD)" vanished, and a cashier retyping the consultation the appointment already named | Done | §5 M1, §5 M3, §5 M4, §7, §3.2 | 2026-08-03 |

| [0044-critical-values](0044-critical-values/spec.md) | A critical lab value looked exactly like a mildly abnormal one: critical bands, distinct flags, and the explicit acknowledgement US9.3 requires before a panic result can be released | Done | §5 M9 (US9.3), §5 M10, §7 U12 | 2026-08-03 |
| [0045-complete-a-patient-identity](0045-complete-a-patient-identity/spec.md) | US1.4 shipped half-built: an unconscious patient could be registered without a name and then never given one — no edit screen and no update path existed at all | Done | §5 M1 (US1.4), §7, §12, edge 25 | 2026-08-03 |
| [0046-departments-and-nursing-receive](0046-departments-and-nursing-receive/spec.md) | Departments above wards; department-scoped nursing station with an explicit receive step capturing arrival vitals + handover | Approved | §5 M6, §5A.2 R5, §7, §12 | 2026-08-04 |
| [0047-discharge-certificate-document](0047-discharge-certificate-document/spec.md) | The discharge certificate had no document: render the frozen body as a printable sheet, editable summary pre-issue, kind-valid admission list | Approved | §5 M6, §7 | 2026-08-04 |
| [0048-ipd-bill-counter](0048-ipd-bill-counter/spec.md) | A dedicated IPD billing counter: settlement requires an IPD session, the IPD session cannot bill OPD, plus an IPD cashier workspace | Approved | §5 M4, §5 M6, §12 | 2026-08-04 |
| [0049-permission-matrix-editor](0049-permission-matrix-editor/spec.md) | Role permissions as checkboxes + one Save; no self-signout, no lockout, instant self-refresh | Approved | §5 M21, §12, §7 | 2026-08-04 |
| [0050-evergreen-rebrand-shell-polish](0050-evergreen-rebrand-shell-polish/spec.md) | Sylhet Evergreen Hospital rebrand (name, logo, favicon, SEH- series), blurred login, collapsible sidebar groups | Approved | §7 | 2026-08-04 |
<!-- Add one row per spec, newest last. Keep Status in sync with the spec's own header. -->
