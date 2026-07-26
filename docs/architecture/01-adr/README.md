# ADR Index

One file per decision: `NNNN-kebab-title.md`. Format and rules: the `adr-write` skill.

Status values: `Proposed` · `Accepted` · `Superseded by NNNN`. Superseded ADRs are never deleted — the record of a reversed decision is the point.

## Decisions

| ADR | Title | Status | Answers | Date |
|---|---|---|---|---|
| — | *none yet* | — | — | — |

## Required coverage

Each PRD §16 open question needs an ADR. Tick as they land.

- [ ] **Q1** Deployment model (cloud / on-prem / hybrid)
- [ ] **Q2** Offline strategy — exactly what keeps working, and reconciliation
- [ ] **Q3** Multi-branch / multi-tenancy readiness
- [ ] **Q4** Device integration approach (analyzers, DICOM MWL, biometrics)
- [ ] **Q5** Reporting / print engine (thermal + A4, pixel-faithful)
- [ ] **Q6** Data migration tooling from legacy systems
- [ ] **Q7** Audit-trail depth vs storage cost
- [ ] **Q8** Patient portal / app architecture (Phase 3)
- [ ] **Q9** Backup / DR mechanics
- [ ] **Q10** Bangla in SMS templates & report footers (English UI, Bangla content)
- [ ] **Q11** Concurrency control for stock / beds / dues
- [ ] **Q12** Module entitlement & licensing toggles
- [ ] **Q13** BEFTN bank payout batch export (forward-looking; not MVP)
- [ ] **Q14** TDS / VAT tax engine placement (forward-looking; not MVP)
- [ ] **Q15** Auth hardening — 2FA, idle lock, dynamic menu-tree permissions

Plus, beyond the numbered questions: **stack choice** and **database choice** each need their own ADR with the RAM budget stated.
