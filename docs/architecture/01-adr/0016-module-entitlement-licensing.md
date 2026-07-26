# 0016 — Module entitlement & licensing toggles (Q12)

- **Status:** Accepted
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q12

## Context

Commercial packaging sells modules per customer (§9 phases); the MVP itself is "8 of 22 modules enabled". Enforcement must not require code changes per sale, must work fully offline (C8 — **no licence check may be on the demo's critical path**, edge 1), and must not tempt anyone into scattering `if (moduleEnabled)` through business logic.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Online licence server | Central control | Violates offline constraint outright |
| Build-per-customer | Simple mentally | Fleet of divergent artifacts; update nightmare |
| **Signed entitlement file + choke-point enforcement (chosen)** | One artifact for all customers; offline-valid; enforcement in exactly three places | Determined tampering by a hostile customer is possible — accepted (see below) |

## Decision

- **Entitlement artifact:** a signed file (vendor key pair; public key baked into the app) listing customer, licensed modules, branch count, expiry/grace policy. Loaded at startup and on admin upload — **no network call, ever**.
- **Enforcement choke points, exactly three:** (1) navigation composition (unlicensed groups never render — same mechanism as role filtering, §7 U1); (2) endpoint authorization (module attribute checked alongside role policy); (3) background workers (a module's jobs don't schedule). Business logic itself stays entitlement-free — the boundary layers guard entry.
- **Data outlives entitlement:** disabling a module hides behaviour, never data; re-enabling restores access (aligned with N10 retention).
- **Expiry behaviour** (grace period, read-only fallback vs. hard stop) is a **business policy** → `09-questions-for-pm.md` with a recommended default (long grace + prominent banner; a hospital must never be locked out of clinical data by a billing dispute).
- **Honesty:** a self-hosted system is ultimately tamperable by its owner. The goal is honest-customer enforcement + audit evidence, not DRM; contract does the rest. Stated so sales doesn't over-promise.

## Consequences

- New sale/packaging = new entitlement file, zero deploys.
- MVP demo ships with an "all 8 modules" entitlement; a full-product demo file can light up future modules' nav stubs if sales wants the tease (PM call).

## Reversal trigger

Evidence of systematic entitlement tampering in the field → revisit with hardware-anchored or hosted-component licensing as a commercial decision, weighed against the offline constraint.
