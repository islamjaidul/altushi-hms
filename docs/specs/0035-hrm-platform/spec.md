# 0035 — Wave 0: the platform a second SKU needs

- **Status:** Done
- **Date:** 2026-07-31
- **PRD ref:** §16 Q12 (entitlement), §7 (UI principles carry to both hosts)
- **ADR ref:** ADR-0025 (two-host product line), ADR-0026 (entitlement enforcement completion)
- **Parent:** `docs/specs/0034-hrm-product-line/` — this is its Wave 0

## Problem

Nothing HR-specific ships in this spec. It removes the four things that make a second SKU impossible,
and it must land before a single HR screen is written — retrofitting any of them afterwards means
rewriting HR.

1. **No shareable UI.** The layout, print partials, CSS tokens, fonts, JavaScript, the `hms-date` tag
   helper, the money/date formatters and `HmsPageModel` all live inside `src/Hms.Web`. A page in any
   other assembly has no layout to render into and no formatter to call.
2. **No usable transaction seam.** `HmsTx`/`TxScope` exposes fourteen module DbContexts as properties
   and cannot compile without all fourteen assemblies. Every page model injects it. An HR page in a
   razor class library cannot.
3. **Entitlement is cosmetic.** ADR-0016 specified three enforcement choke points; only nav
   composition was built. A de-entitled module's URLs still work, and `EntitlementState.Grace` /
   `ReadOnly` are computed and never read. Selling a module separately requires the boundary to be
   real.
4. **No second host, and no way to build one safely.** Identity, cookies, authorization, forwarded
   headers, entitlement loading and the migration advisory lock are hand-rolled in one `Program.cs`.
   Copying them into a second one guarantees drift, and drift in that particular list is a security
   defect rather than an inconsistency.

## Requirements

- **R1 — `src/Hms.Shell` razor class library.** Holds `_Layout`, `_Letterhead`, `_PrintTools`,
  `_SheetFooter`, `tokens.css`, `app.css`, `wwwroot/js/*`, the fonts, `HmsDateTagHelper`, `Ui.cs`,
  `HmsPageModel`, and `OrgIdentity` (today's `HospitalIdentity`, renamed for a market that is not
  only hospitals). Both hosts reference it. ERP behaviour is unchanged.
- **R2 — `IHrTx` / `HrScope` seam** in `Hms.Hr`, exposing exactly `Kernel`, `Auth`, `Hr`. The ERP host
  adapts its existing `HmsTx`; the HRM host implements it directly. `HmsTx` gains one `Hr` property,
  like every other module. No existing page changes.
- **R3 — ADR-0026 enforcement.** `RequireModuleAttribute` applied by route-prefix convention;
  entitlement checked alongside role policy; grace banner; read-only write-gating; admin upload of a
  replacement entitlement, verified offline, persisted and audited.
- **R4 — `src/Hms.Hr.Web` host** with `AddHmsPlatform()` / `UseHmsPlatform()` shared bootstrap in the
  kernel, so the two composition roots cannot drift on auth.
- **R5 — Host/database interlock.** A kernel `host_kind` row written at first boot; a host of a
  different kind refuses to start rather than serving a half-migrated database. HRM→ERP upgrade is
  allowed and tested; ERP→HRM is refused.
- **R6 — Branch resolution** for the HRM host (P28); the ERP host keeps its constant.
- **R7 — Deploy artifacts** for the second SKU: `deploy/hrm.Dockerfile`, `compose.hrm.yml`,
  `entitlements/hrm-only.json`, and a runbook section including vendor key custody.
- **R8 — Guards must see the new roots.** Every guard and architecture test that hardcodes
  `src/Hms.Web` is re-pointed, plus a meta-test that fails if a page exists outside the scanned set.

## Acceptance criteria

1. `dotnet build hms-erp.slnx` succeeds with `TreatWarningsAsErrors=true`, and the existing Playwright
   suite passes unchanged against the ERP host after the UI extraction — the extraction is
   behaviour-preserving or it has failed.
2. An HR page compiled into a razor class library renders inside the shared layout, in both hosts,
   with tokens, fonts, icons and the `hms-date` input working identically.
3. A single business action spanning kernel and HR commits in one transaction under **both** hosts.
4. With an entitlement omitting Billing, a user holding `billing.*` gets **403** from `/billing/opd`
   in the ERP host. Proven by an endpoint test.
5. In `Grace`, a banner names the expiry date and everything works. In `ReadOnly`, `GET` succeeds and
   mutating handlers refuse with an operator-readable message — except the entitlement-upload screen,
   which stays writable so a paid-up customer can recover.
6. Uploading a validly signed entitlement swaps modules in-process without a restart, survives a
   restart, and writes an audit event with before/after module sets. An unsigned, malformed, or
   wrong-customer file is refused with a clear message and changes nothing.
7. A database created by the HRM host boots under the ERP host; a database created by the ERP host is
   refused by the HRM host with a message naming the mismatch.
8. No guard is blind: a deliberately introduced hex colour, external host reference, native
   `<input type="date">` or unguarded page under any UI root fails the build.

## Out of scope

- Any HR entity, screen, or business rule — that is spec 0036 (Wave A).
- Multi-branch resolution in the **ERP** host. Unchanged; P28 scopes the amendment to the HRM SKU.
- Choke point 3 (background workers). Vacuously satisfied — there are none in `src/`, deliberately.
  It becomes real when the live biometric feed introduces the first one, and that spec owns it.

## Risks / open questions

- **The UI extraction touches every screen's rendering path.** Highest-risk item in the spec, and the
  reason it is first: nothing else can be built on a layout that is about to move. Mitigation is that
  it is a move, not a rewrite, with the Playwright suite as the net.
- **Runtime-mutable entitlement** makes `EntitlementProvider` writable after startup where it was
  write-once. Kept safe by making the swap a single atomic reference assignment.
- **Signing becomes operational.** The vendor private key must never reach a customer machine;
  `eng/dev-keys/` stays development-only and gitignored. Custody and rotation go in the runbook.
- **`Hospital:*` config keys are live** on `hms.specshipper.com`. `OrgIdentity` reads `Org:*` with
  fallback to `Hospital:*`; the existing keys are not renamed.
</content>
