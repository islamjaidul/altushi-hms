# 0005 — Deployment model: on-premise-first, hosted-capable (Q1)

- **Status:** Accepted
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q1

## Context

§8 N2 requires counters to survive internet outages; §8 N3 requires data export; the commercial need (Q1) is both a hosted offer and an on-prem story. Both competitors sell "cloud with offline". Bangladesh reality (§3.3): unreliable ISPs, one IT-capable person per hospital (§16.1 A4).

## Options considered

| Option | Pros | Cons | RAM cost |
|---|---|---|---|
| **On-prem VM per hospital (chosen primary)** | Counters keep working with zero internet; latency on LAN; data physically at the hospital (export trivially satisfied) | Vendor must manage remote fleets; hardware is customer-procured | the 3 GB box |
| Cloud-hosted per hospital (offered variant) | No customer hardware; easier fleet ops | Internet outage stops the hospital — violates N2 unless paired with on-prem fallback complexity | n/a (cloud) |
| Hybrid sync (cloud master + site replica) | Marketing-friendly | Two-master sync of financial data is the hardest problem in the product; nothing in §9A needs it | double |

## Decision

**Primary: single on-prem VM (or mini-PC class hardware) per hospital**, Docker Compose, identical artifact set for the demo laptop. **Hosted variant** uses the exact same images on a cloud VM for customers who accept internet dependence (e.g., diagnostic-center-only customers) — one codebase, a deployment-time choice, no separate build. Remote support via outbound-only tunnel (e.g., WireGuard/SSH), never inbound port-forwarding. Off-site encrypted backup push when internet is available (ADR-0013).

## Consequences

- N2 satisfied structurally at on-prem sites; the hosted variant's outage posture is disclosed honestly in sales material (PM owns that wording).
- Fleet management (updates across sites) is an operational investment: versioned images + scripted `compose pull && up` maintenance runbook; maintenance never blocks emergency registration (§8 N6) because updates are off-peak and rollback is an image tag.
- No cloud-master/site-replica sync is built or promised in MVP (see ADR-0006 scope).

## Reversal trigger

If pilot sales show most buyers demand vendor-hosted with offline parity, revisit a site-appliance + hosted-mirror design as its own funded phase — not as an MVP patch.
