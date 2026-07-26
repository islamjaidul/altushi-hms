# 0006 — Offline strategy: LAN-first server, queued egress, no browser replicas (Q2)

- **Status:** Accepted
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q2 (constraint C8)

## Context

§8 N2: registration, billing and lab must keep working during an internet outage, and power-cut recovery must not lose or duplicate a saved invoice. The PM accepts **scoped** offline if precisely defined. The demo site (construction building) has no internet at all (edge case 1).

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **LAN-first: on-prem server is the system of record (chosen)** | Internet outage is a non-event for counters; no data-sync correctness risk on money paths | A LAN/server failure does stop work (mitigated: supervised process restart, healthchecks, UPS recommendation; honest limitation stated) |
| Browser-local offline (IndexedDB queues, service workers) | Survives even LAN loss | Client-side replicas of money data on shared counter PCs: sync conflicts vs. C9's "no silent overwrite", gap-free numbering impossible offline, XSS/theft surface on §8 N5 data — cost far exceeds §9A need |
| Per-counter desktop app with local DB | Full offline | A fleet of syncing databases = the two-master problem × N counters |

## Decision

**What works with no internet (everything on the LAN):** the entire golden thread — registration, serials, billing, discounts/approvals, barcode printing, LIS, report delivery, day-close, dashboards, backups. All assets (fonts incl. Bangla, JS, icons) ship in the app image; nothing external is on any critical path.

**What does not, and how it degrades:** outbound SMS (queued in the jobs table with the trigger event and timestamp; drains automatically on reconnect; visible "SMS pending" state, simulation mode for demos — edge 3); off-site backup push (deferred, local backups continue); remote support.

**Power-cut posture (edge 7):** every money write is one committed transaction (invoice + lines + number issuance + audit atomically). Postgres WAL (`synchronous_commit=on` on money paths) makes committed work durable; Docker `restart: unless-stopped` + healthchecks target service-restored ≤ 2 minutes after power returns; ADR-0004 numbering makes duplicate invoices structurally impossible on recovery. Client screens reconnect (SSE auto-retry) without operator action. UPS for server + switch is a deployment recommendation (estimate: 600–1000 VA class; PM/customer procurement).

## Consequences

- Zero sync code on money paths = the concurrency guarantees of ADR-0015 hold everywhere.
- Honest sales positioning: "offline" means the *hospital's own network*, matching how both competitors' on-prem installs actually behave; wording is PM-owned.
- A future genuinely-disconnected mode (e.g., outreach camps) would be a new spec, not a stretch of this one.

## Reversal trigger

A pilot customer demonstrates a real, recurring LAN-partition workload (e.g., a billing counter in a separate building on a flaky link) that loses work — then design a *single-counter* capture-and-forward queue for registration only, with numbers assigned server-side on drain.
