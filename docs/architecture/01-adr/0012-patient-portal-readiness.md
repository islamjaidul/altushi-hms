# 0012 — Patient portal/app readiness (Q8)

- **Status:** Accepted
- **Note:** forward-looking; portal is Phase 3
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q8

## Context

Phase 3 wants online reports, booking, payments (§9 Phase 3, §13 I12). Q8's constraint: it must not complicate Phase 1. The on-prem, LAN-first posture (ADR-0005/0006) means the portal cannot simply be "the same server on the internet".

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Build portal endpoints into MVP now | "Ready" | Violates scope C1; drags internet exposure onto the LAN box; zero §9A value |
| Ignore entirely | Cheapest | Risks Phase-3 requiring identity/report-model rework |
| **Record the seams, build nothing (chosen)** | Phase 1 stays simple; Phase 3 has a stated path | Discipline cost only |

## Decision

Portal, when funded, is a **separate internet-facing service** (cloud-hosted) that talks to hospital installs over the existing outbound channel (same transport as consolidated dashboards, ADR-0007): hospitals push report-ready artifacts (final PDFs + minimal metadata) and receive booking/payment requests as inbound *jobs* the hospital app owns and confirms. The LAN box never accepts inbound internet connections (ADR-0005 support tunnel excepted).

Seams the MVP already provides, at no extra cost: verified reports exist as archived PDFs with stable identities (ADR-0009/0011); patients have UHID + phone as a portal identity anchor; appointments and payments are approval/consume flows the engine already models. **Nothing else is built now.**

## Consequences

- Phase 1 carries zero portal code, zero extra attack surface.
- Phase 3 inherits an eventually-consistent, hospital-authoritative model — online booking is a *request*, the hospital system remains the source of truth (consistent with C9).

## Reversal trigger

PM re-scopes the portal earlier than Phase 3 → it becomes its own spec + funded service; this ADR's seams are its starting contract.
