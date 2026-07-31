# 0025 — Two hosts, one codebase: HR & Payroll as a separately deployable product

- **Status:** Accepted
- **Date:** 2026-07-31
- **Extends:** ADR-0003 (modular monolith), ADR-0005 (deployment model), ADR-0016 (entitlement)
- **Spec:** `docs/specs/0034-hrm-product-line/`

## Context

A customer wants HR & Payroll without the hospital. ADR-0016 already assumed module-wise selling —
*"new sale/packaging = new entitlement file, zero deploys"* — but the physical architecture cannot
honour it for a single-module customer:

- `src/Hms.Web` owns every Razor Page, every `AddDbContext` registration, every cross-module
  orchestrator, and `HmsTx`, whose `TxScope` exposes all fourteen module DbContexts as properties.
  `HmsTx` therefore cannot compile without all fourteen module assemblies, and every page model in
  the application injects it.
- Shipping "just HR" today means shipping the whole hospital system with a smaller menu: fourteen
  schemas migrated into the customer's database, fourteen assemblies in memory, and a product whose
  visible surface is a rounding error of what is installed.

ADR-0003's boundaries are sound and are not the problem. The problem is that "one host" was treated
as a property of the architecture when it is really a property of one *package*.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Entitlement-only gating of the monolith | Cheapest; no new project | HR customer still receives all fourteen schemas and modules; requires choke point 2 anyway; the product's footprint and attack surface bear no relation to what was sold |
| Separate repository for HRM | Maximum independence | Guarantees divergence of kernel, auth, UI tokens, deploy tooling and guards; every kernel fix applied twice; two CIs |
| **Two hosts, one codebase (chosen)** | One source of truth; each SKU ships only what it is; kernel fixes land once; one CI | A shared-UI extraction and a transaction seam must be built first; two composition roots to keep aligned |

## Decision

**A host is a packaging choice, not an architectural layer.** The repository grows a second
composition root; nothing else about ADR-0003 changes.

```
Hms.Kernel      shared, module-agnostic
Hms.Shell          NEW razor class library: layout, print partials, tokens, fonts,
                tag helpers, formatting, page-model base, organisation identity
Hms.Hr          module — references Kernel ONLY
Hms.Hr.Contracts what M15/M17 will consume
Hms.Hr.Screens       NEW razor class library: every /hr/* page, HR permissions, HR nav

Hms.Web         host — ERP SKU  → Kernel + Hms.Shell + all modules + Hms.Hr.Screens
Hms.Hr.Web      host — HRM SKU  → Kernel + Hms.Shell + Hms.Hr + Hms.Hr.Screens
```

Consequences of that shape, decided here:

1. **Module screens may live in a razor class library.** Previously all pages lived in the host. A
   module whose screens must appear in more than one host puts them in an RCL. Modules that will only
   ever ship inside the ERP need not move.
2. **Permissions and nav for such a module live with its RCL**, not in the host's `Perm.cs` /
   `ModuleNav.cs` — an RCL cannot reference the host. Hosts compose registries by concatenation.
3. **`TxScope` is not generalised.** Instead a module that must run under multiple hosts defines a
   narrow scope interface exposing only the contexts it needs (`IHrTx`/`HrScope`). Each host supplies
   an implementation: the ERP host adapts its existing `HmsTx` (so G19 — one business action, one
   transaction — is unchanged); the HRM host builds a three-context scope with the same `Attach`
   pattern. Existing pages are untouched.
4. **Host bootstrap is shared.** Identity, cookies, authorization, forwarded headers, entitlement
   loading and the migration advisory lock move into `AddHmsPlatform()` / `UseHmsPlatform()` in the
   kernel. Two `Program.cs` files that each hand-roll authentication would drift, and the drift would
   be a security defect rather than an inconsistency.
5. **One database per host kind.** Per ADR-0007 there is already one database per customer. A kernel
   `host_kind` row is written at first boot; a host of a different kind refuses to start against it,
   rather than silently serving a half-migrated database.
6. **Upgrade is bidirectional-aware.** A database created by the HRM host must boot under the ERP
   host (which then migrates the remaining contexts) — the HRM→ERP upsell path. The reverse is
   refused. Both directions are CI-tested alongside ADR-0022's upgrade-path job.

## Consequences

- The HRM SKU's image contains no clinical code and its database has three schemas, so the product a
  customer buys is the product they receive.
- Guards and architecture tests that hardcode `src/Hms.Web` become blind spots the moment code lives
  elsewhere. Every guard is re-pointed at all UI roots, and a meta-test fails the build if a page
  exists outside the scanned set.
- A third SKU later (say, Accounts-only) is a new host project and an entitlement file, not a
  redesign.
- Cost: the shared-UI extraction touches the layout and asset paths every existing screen depends on.
  It is behaviour-preserving by construction and the existing Playwright suite is the regression net.

## Reversal trigger

If the two composition roots start accumulating genuinely divergent behaviour — not just different
module sets — the shared-UI library has failed to hold the line, and the honest response is to merge
back to one host and accept entitlement-only gating for single-module customers.
</content>
