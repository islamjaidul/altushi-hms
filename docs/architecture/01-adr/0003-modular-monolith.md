# 0003 — Application architecture: modular monolith with enforced seams

- **Status:** Accepted
- **Date:** 2026-07-26

## Context

C2 (the brief) makes retrofit-pain the named failure mode: competitors bolted folios under live billing and are still paying. §9A.3 defers 14 of 22 modules, §14 says a large customer reaches 150 operators/4 branches, and the PM steer (2026-07-26) demands enterprise extensibility — while the MVP box is 2 vCPU / 3 GB, which rules out distributed anything.

## Options considered

| Option | Pros | Cons | RAM cost |
|---|---|---|---|
| **Modular monolith (chosen)** | One process fits the budget; module boundaries enforced at compile time (assembly references); in-process domain events cost nothing; later split follows the existing seams | Requires discipline: boundary erosion is the classic monolith death | one app process (~200–400 MB est.) |
| Microservices now | "Born scalable" | 8+ processes × runtime baseline + broker ≈ certain budget blowout; operational burden vs. §16.1 A4 (one IT-capable staff member per hospital) | ≥ 1.5 GB |
| Plain monolith (no internal boundaries) | Fastest to write | Recreates the competitors' retrofit trap; C2 explicitly forbids it | same as chosen |

## Decision

One ASP.NET Core host; **one assembly per module** (Registration, Appointments, Billing, Diagnostics, LIS, Dashboard, Admin/Approvals, Notifications) plus a **shared kernel** (approval engine, audit writer, numbering, printing, authZ, domain-event bus). Rules, enforced by project references and an architecture test in CI:

1. Modules reference only the kernel and other modules' **contract** interfaces — never their internals or tables.
2. Cross-module writes happen via contracts (e.g., Diagnostics asks Billing to create charge lines); cross-module notifications via in-process domain events (report-verified → notification job).
3. Each module owns its schema (its migrations, its tables); cross-module reads for dashboards go through read-model views, not foreign table joins in module code.
4. The future-module seams (folio parent on charge lines, doctor/referrer attribution, `branch_id`, bed entity) are part of the kernel model from day one — see `02-domain-model.md` §3.

## Consequences

- Splitting a module onto its own host later = replace the in-process contract with a transport behind the same interface; schema ownership already separated.
- Cost accepted: some ceremony (contracts, events) that a small CRUD app wouldn't need. That ceremony *is* the C2 insurance.
- CI architecture test (e.g., assembly-reference assertions) keeps the boundary honest after handoffs.

## Reversal trigger

If the boundary tax measurably slows the golden-thread build before the demo, the PM-visible fallback is to relax rules **inside** the diagnostics↔LIS pair only — never around Billing/Admin, whose seams are the C2 obligation.
