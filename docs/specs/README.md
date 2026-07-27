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

<!-- Add one row per spec, newest last. Keep Status in sync with the spec's own header. -->
