# 0007 — Multi-branch & multi-tenancy readiness (Q3)

- **Status:** Accepted
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q3 (§8 N9)

## Context

§14: customers grow to 2–4 branches wanting consolidated MD dashboards; product ambition is many customers. Q3 says don't preclude either. The MVP serves exactly one facility, and 3 GB affords no platform machinery.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Shared multi-tenant platform now | One fleet to run | Tenant isolation of clinical/financial data is a compliance and engineering tax the MVP can't pay; conflicts with on-prem model (ADR-0005) |
| Ignore branches until needed | Cheapest today | Retrofitting a branch key into every money table is C2's named failure mode, replayed |
| **Single-tenant install + branch_id from day one (chosen)** | Costs one column and one config row now; consolidation becomes a query or a read-replica feed later | Slight ubiquitous ceremony (every business row carries `branch_id`) |

## Decision

- **Tenancy:** one hospital (legal entity) per install/database. No cross-customer sharing, ever, in this product line — fleet efficiency comes from identical images, not shared databases.
- **Branches:** every business table carries `branch_id` (FK to a `branch` master; MVP has exactly one row). Number series, counters, rate plans and permissions are already branch-scoped keys (ADR-0004). UHID scope (per-branch vs. per-hospital patient identity) is a **business decision** routed to the PM with a recommended default of hospital-wide UHID (`09-questions-for-pm.md`).
- **Consolidated MD dashboards** (2–4 branches, §14): design intent recorded now — branch installs push day-close summaries and KPI aggregates (not row-level clinical data) to a small consolidation endpoint over the outbound channel; read-only, eventually-consistent, tolerant of branch offline. Built when a real multi-branch customer exists; nothing in the MVP schema blocks it.

## Consequences

- The MVP pays ~zero RAM and one column of ceremony for a clean growth path.
- No shared-platform economics in v1 — accepted; ADR-0005's fleet tooling carries multi-customer operations instead.

## Reversal trigger

A signed customer needs live cross-branch workflows (e.g., one branch bills a test performed at another) — that invalidates "summaries only" and triggers a real distributed-workflow spec.
