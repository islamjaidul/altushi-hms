# 0026 — Entitlement enforcement completion: choke point 2 and the expiry ladder

- **Status:** Accepted
- **Date:** 2026-07-31
- **Completes:** ADR-0016 (module entitlement & licensing, Q12); answers **P6**
- **Spec:** `docs/specs/0034-hrm-product-line/`, `docs/specs/0035-hrm-platform/`

## Context

ADR-0016 specified **exactly three** enforcement choke points: navigation composition, endpoint
authorization, and background workers. Only the first was built.

- `NavComposer.Compose` intersects permissions with entitled modules — the sidebar is correct.
- There is no endpoint check. `grep -rn "RequireModule\|ModuleAttribute" src/` returns nothing. A
  user holding `billing.*` on a deployment whose entitlement omits Billing can reach `/billing/opd`
  by typing the URL. The menu hides it; nothing refuses it.
- `EntitlementState.Grace` and `EntitlementState.ReadOnly` are computed in `EntitlementFile.Load` and
  read nowhere outside unit tests. PM answer **P6** — thirty-day grace with a banner, then read-only,
  never a lockout of clinical data — is undelivered.
- Choke point 3 is vacuously satisfied: there are no background workers in `src/` at all.

While every deployment was entitled to every module, this was latent. Selling HR as a standalone
product makes it live: the module boundary becomes the product boundary, and today that boundary is a
menu filter.

## Decision

**Build choke point 2 and the expiry ladder now, as platform work, before the first separately-sold
module ships.**

### Choke point 2 — endpoint enforcement

- `RequireModuleAttribute(string module)` in `Hms.Kernel.Entitlements`, enforced by an authorization
  requirement and handler that reads `EntitlementProvider`.
- Applied by **Razor Pages convention over a route prefix**, not by hand per page. Hand-applied
  attributes are forgotten; a convention that maps `/hr/*` to `RequireModule("Hr")` cannot be.
- A de-entitled module returns **403**, the same as a missing permission, and lands on the existing
  `/denied` page with a message naming the module rather than the permission.
- Ordering: entitlement is checked **alongside** role policy, not instead of it. Losing an entitlement
  never grants access, and holding one never substitutes for a permission.

### The expiry ladder (P6, as recommended)

| State | Behaviour |
|---|---|
| `Active` | Normal. |
| `Grace` (past expiry, within `graceDays`) | Everything works. A persistent banner in the layout states the expiry date and days remaining. |
| `ReadOnly` (past grace) | `GET` always succeeds — **data is never locked away**. Mutating handlers refuse with a clear operator message naming the licence, not a stack trace. |

Read-only gating is enforced at the same boundary layer as the module check, so business logic stays
entitlement-free per ADR-0016. The one deliberate exception: the entitlement-upload screen itself must
remain writable in `ReadOnly`, or a customer who has paid cannot apply the file that fixes it.

### Entitlement replacement without a deploy

ADR-0016 says the file is loaded "at startup **and on admin upload**". The upload half is built here:
an admin screen accepts a signed file, verifies it offline against the baked-in public key, refuses
anything unsigned, malformed, or naming a different customer, and swaps it into `EntitlementProvider`
in-process. The accepted file is persisted so the change survives a restart, and every upload is
audited with the before/after module sets.

`EntitlementProvider` therefore becomes safely mutable at runtime — the swap is a single atomic
reference assignment, and readers take whatever is current.

## Consequences

- "Sold separately" becomes a property the code enforces rather than a promise the contract carries
  alone. The proof is an endpoint test: an HR-only entitlement in the ERP host makes `/billing/opd`
  return 403 for a user who holds the billing permission.
- Renewal is an operator action on a screen, not a redeploy — which matters much more for a
  separately-sold product with its own renewal cycle than it did for a bundled ERP.
- Signing becomes an operational process. The vendor private key never ships to a customer machine;
  `eng/dev-keys/` remains development-only and gitignored, and key custody and rotation are documented
  in the runbook.
- ADR-0016's honesty clause stands unchanged: a self-hosted system is tamperable by its owner. This
  raises the floor from "menu filter" to "the application refuses", which is what an honest customer
  and an audit need. It is not DRM and must not be sold as such.

## Reversal trigger

If convention-based application proves too coarse for some future surface — a page legitimately
serving two modules — replace the route-prefix convention with explicit attributes plus a test that
every page under a module's route prefix carries one. Do not silently drop the check for that page.
</content>
