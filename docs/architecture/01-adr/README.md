# ADR Index

One file per decision: `NNNN-kebab-title.md`. Format and rules: the `adr-write` skill.

Status values: `Proposed` · `Accepted` · `Superseded by NNNN`. Superseded ADRs are never deleted — the record of a reversed decision is the point.

**"Accepted" = architect-final.** PM review governs the deliverable set as a whole (see `docs/architecture/README.md` status); business questions remain open in `../09-questions-for-pm.md`, and a PM answer that contradicts an ADR supersedes it via a new ADR.

## Decisions

| ADR | Title | Status | Answers | Date |
|---|---|---|---|---|
| [0001](0001-stack-dotnet-aspnet-razor.md) | Stack: .NET / ASP.NET Core, server-rendered Razor | Accepted | stack | 2026-07-26 |
| [0002](0002-database-postgresql-only-stateful-service.md) | PostgreSQL as the only stateful service | Accepted | database | 2026-07-26 |
| [0003](0003-modular-monolith.md) | Modular monolith with enforced seams | Accepted | app architecture | 2026-07-26 |
| [0004](0004-identifiers-and-numbering.md) | Identifiers, gap-free numbering, fiscal year, business day | Accepted | edge 15–16 | 2026-07-26 |
| [0005](0005-deployment-model.md) | On-premise-first, hosted-capable deployment | Accepted | Q1 | 2026-07-26 |
| [0006](0006-offline-strategy-lan-first.md) | LAN-first offline strategy, queued egress | Accepted | Q2 | 2026-07-26 |
| [0007](0007-multi-branch-multi-tenancy.md) | Multi-branch & tenancy readiness | Accepted | Q3 | 2026-07-26 |
| [0008](0008-device-integration-connector-agent.md) | Device integration via local connector agents | Accepted | Q4 | 2026-07-26 |
| [0009](0009-printing-reporting-engine.md) | Printing/reporting engine (thermal + A4 + PDF) | Accepted | Q5 | 2026-07-26 |
| [0010](0010-data-migration-tooling.md) | Data migration & bulk import pipeline | Accepted | Q6 | 2026-07-26 |
| [0011](0011-audit-trail-mechanism.md) | Tiered in-transaction audit trail | Accepted | Q7 | 2026-07-26 |
| [0012](0012-patient-portal-readiness.md) | Patient portal readiness (Phase 3 seam) | Accepted | Q8 | 2026-07-26 |
| [0013](0013-backup-restore-dr.md) | Backup, restore & DR (WAL + drilled restore) | Accepted | Q9 | 2026-07-26 |
| [0014](0014-bangla-text-path.md) | Bangla text path end-to-end | Accepted | Q10 | 2026-07-26 |
| [0015](0015-concurrency-control.md) | Concurrency control (constraints, row locks, versions) | Accepted | Q11 | 2026-07-26 |
| [0016](0016-module-entitlement-licensing.md) | Module entitlement & licensing toggles | Accepted | Q12 | 2026-07-26 |
| [0017](0017-beftn-payout-readiness.md) | BEFTN payout batch readiness (forward-looking) | Accepted | Q13 | 2026-07-26 |
| [0018](0018-tds-vat-tax-engine-placement.md) | TDS/VAT tax engine placement (forward-looking) | Accepted | Q14 | 2026-07-26 |
| [0019](0019-auth-hardening.md) | Auth hardening: 2FA, idle lock, dynamic menus, shared logins (amended 2026-07-27: security-stamp revalidation) | Accepted | Q15 | 2026-07-26 |
| [0020](0020-shared-input-layer.md) | Shared input layer: one date/search/type-ahead contract | Accepted | review §1, 05 §3, §7 U5/U9/U13 | 2026-07-27 |
| [0021](0021-stock-ledger-kernel.md) | Stock ledger kernel: append-only moves, FEFO issue, bill-spine sales | Accepted | §5 M11/M12, §11, 11-build-plan §4 | 2026-07-27 |
| [0022](0022-upgrade-path-testing.md) | Upgrade-path testing: boot against previous release's data in CI | Accepted | review debt #1 | 2026-07-27 |
| [0023](0023-grant-drift-report-not-reconcile.md) | Grant drift is reported, not silently reconciled | Accepted | QA F1, LC-XCUT-14 | 2026-07-28 |
| [0024](0024-concurrency-load-target.md) | What "forty operators at once" must mean on 2 vCPU / 3 GB | **Proposed** | LC-XCUT-11, §8 N1 | 2026-07-28 |

## Required coverage

Each PRD §16 open question needs an ADR. Tick as they land.

- [x] **Q1** Deployment model (cloud / on-prem / hybrid)
- [x] **Q2** Offline strategy — exactly what keeps working, and reconciliation
- [x] **Q3** Multi-branch / multi-tenancy readiness
- [x] **Q4** Device integration approach (analyzers, DICOM MWL, biometrics)
- [x] **Q5** Reporting / print engine (thermal + A4, pixel-faithful)
- [x] **Q6** Data migration tooling from legacy systems
- [x] **Q7** Audit-trail depth vs storage cost
- [x] **Q8** Patient portal / app architecture (Phase 3)
- [x] **Q9** Backup / DR mechanics
- [x] **Q10** Bangla in SMS templates & report footers (English UI, Bangla content)
- [x] **Q11** Concurrency control for stock / beds / dues
- [x] **Q12** Module entitlement & licensing toggles
- [x] **Q13** BEFTN bank payout batch export (forward-looking; not MVP)
- [x] **Q14** TDS / VAT tax engine placement (forward-looking; not MVP)
- [x] **Q15** Auth hardening — 2FA, idle lock, dynamic menu-tree permissions

Plus, beyond the numbered questions: **stack choice** and **database choice** each need their own ADR with the RAM budget stated.
