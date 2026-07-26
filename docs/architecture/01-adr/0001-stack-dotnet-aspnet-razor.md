# 0001 — Application stack: .NET (ASP.NET Core, server-rendered Razor)

- **Status:** Accepted
- **Date:** 2026-07-26

## Context

The stack must satisfy three pulls at once: (a) the MVP runs on 2 vCPU / 3 GB (§16 + architect brief budget), (b) the PM has directed (2026-07-26) that the choice be **industry-standard and enterprise-extendable** because production hardware will be far larger than the MVP box, and (c) the operator UX (§7 U4/U5) is keyboard-first on modest counter PCs with **no CDN or internet on the critical path** (§8 N2, demo edge case 1). Development and testing happen on macOS.

## Options considered

| Option | Pros | Cons | RAM cost (app process, estimate) |
|---|---|---|---|
| **.NET / ASP.NET Core + Razor (chosen)** | Enterprise ecosystem & hiring pool; cross-platform (native macOS dev, Linux deploy); mature ORM (EF Core), auth (Identity), background hosting; strong long-term support cadence | Heavier baseline RSS than Go; team must hold the line against "enterprise sprawl" (unneeded layers) | ~200–400 MB steady (estimate; must be measured — spec 0003 notes) |
| Go + server-side templates | Smallest RSS (~50–150 MB); single binary | Thinner ERP-shaped ecosystem (ORM, reporting, auth); weaker fit to PM's "industry standard for enterprise" steer | ~100 MB |
| Django / Rails | Fast CRUD productivity; batteries included | Interpreter RAM comparable to .NET without its typing/refactorability at ERP scale; weaker fit to PM steer | ~300–500 MB with workers |
| SPA (React/Angular) + API backend | Rich client interactions | Second toolchain, larger payloads on LAN PCs, CDN temptation, more RAM & build complexity; §7 needs are achievable server-rendered (the Altushi reference is plain HTML/CSS+JS) | adds node build chain (dev) |

## Decision

**ASP.NET Core on the current LTS release of .NET** (.NET 10 LTS at time of writing — verify the exact LTS at build start), server-rendered **Razor** views plus a thin, self-hosted vanilla-JS layer (type-ahead, barcode wedge capture, F-key shortcuts, SSE updates). EF Core + Npgsql for data access; hand-written SQL where money integrity demands it (see ADR-0002, ADR-0015). One container image, Linux, built and run identically on the dev Mac (Apple Silicon: multi-arch image) and the VM.

## Consequences

- Easy: hiring, long-term maintenance, later scale-out on big hardware (Kestrel scales with cores/RAM), strict typing across a large domain model.
- Hard/accepted: baseline RSS is the single biggest line in the memory budget (`06-deployment.md`); we set a container limit and measure before the demo. Razor discipline: no component framework sprawl; the Altushi design's shared templates are implemented once as layout/partials.
- The front-end remains swappable: if a richer client is ever justified (Q8 portal, tablet apps), the module contracts (ADR-0003) already expose the needed application services.

## Reversal trigger

Measured steady-state app RSS cannot be held under ~600 MB serving 25 concurrent operators on the 3 GB box after tuning (server GC off, trimming, response caching) — revisit toward Go for the counter-critical services. Or: the PM's enterprise steer changes.
