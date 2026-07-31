# 0035 — Plan

## Approved: 2026-07-31

Wave 0 of spec 0034. Ordered so each step compiles and tests green before the next begins; the UI
extraction is first because everything else renders into it.

## 1. Traceability matrix (requirement → surface)

| # | Requirement | Surface | Status |
|---|---|---|---|
| R1 | Shared UI library | `src/Hms.Shell/` | Build |
| R2 | Transaction seam | `Hms.Hr/HrScope.cs`, `Hms.Web/HrTxAdapter.cs`, `Hms.Hr.Web/HrTx.cs` | Build |
| R3 | Entitlement enforcement | `Hms.Kernel/Entitlements/RequireModule*.cs`, `EntitlementProvider`, `/admin/entitlement` | Build |
| R4 | Second host + shared bootstrap | `src/Hms.Hr.Web/`, `Hms.Kernel/Hosting/HmsPlatform.cs` | Build |
| R5 | Host/database interlock | `kernel.host_kind` + startup check in `UseHmsPlatform` | Build |
| R6 | Branch resolution (HRM host) | `Hms.Kernel/Auth/CurrentBranch.cs` | Build |
| R7 | Deploy artifacts | `deploy/hrm.Dockerfile`, `compose.hrm.yml`, `entitlements/hrm-only.json`, RUNBOOK | Build |
| R8 | Guards see new roots | `eng/check-*.sh`, arch tests, `role-journeys.py` | Build |

## 2. Build order

### Step 1 — `src/Hms.Shell` razor class library (R1)

Move, do not rewrite:

| From | To |
|---|---|
| `src/Hms.Web/Pages/Shared/_Layout.cshtml` and the three print partials | `src/Hms.Shell/Areas/…/Shared/` (RCL page-discovery path) |
| `src/Hms.Web/wwwroot/css/{tokens,app}.css`, `wwwroot/js/*`, `wwwroot/fonts/*` | `src/Hms.Shell/wwwroot/` — served at `/_content/Hms.Shell/…` |
| `src/Hms.Web/TagHelpers/HmsDateTagHelper.cs` | `src/Hms.Shell/TagHelpers/` |
| `src/Hms.Web/Ui.cs` | `src/Hms.Shell/Ui.cs` |
| `src/Hms.Web/Pages/HmsPageModel.cs` | `src/Hms.Shell/HmsPageModel.cs` |
| `src/Hms.Web/HospitalIdentity.cs` | `src/Hms.Shell/OrgIdentity.cs` — reads `Org:*`, falls back to `Hospital:*` (the live VM uses the old keys; do not rename them) |

`_ViewImports.cshtml` in both hosts adds `@addTagHelper *, Hms.Shell` and `@using Hms.Shell`. Asset URLs in
the layout become `/_content/Hms.Shell/…`. `HmsPageModel.BranchId` becomes virtual so R6 can override it.

**Gate:** ERP builds, boots, and the existing Playwright suite passes unchanged.

### Step 2 — transaction seam (R2)

In `Hms.Hr`: `HrScope` (three context properties) and `IHrTx` (two `RunAsync` overloads).
In `Hms.Web`: one `Hr` property on `TxScope`, plus an `IHrTx` adapter that wraps `HmsTx` and projects
its scope — same connection, same transaction, G19 unchanged.
In `Hms.Hr.Web`: a direct implementation using the same `Attach` pattern.

### Step 3 — entitlement enforcement (R3)

- `RequireModuleAttribute`, `ModuleEntitlementRequirement`, `ModuleEntitlementHandler` in
  `Hms.Kernel/Entitlements/`.
- Applied by Razor Pages convention over route prefixes, so no page can forget it.
- `EntitlementProvider` becomes atomically swappable; `Current` reads whatever is live.
- Read-only gating on non-GET at the same boundary layer, with the entitlement-upload screen exempt.
- Grace banner in the shared layout.
- `/admin/entitlement` upload: verify offline, refuse unsigned/malformed/wrong-customer, persist,
  audit before/after module sets.

### Step 4 — shared bootstrap + second host (R4, R5, R6)

`Hms.Kernel/Hosting/HmsPlatform.cs`:
- `AddHmsPlatform(...)` — Identity, cookie policy, security-stamp revalidation, permission policy
  provider, deny-by-default fallback, antiforgery, kernel services, entitlement provider.
- `UseHmsPlatform(...)` — forwarded headers, static files, auth, `/health`, host-kind interlock.

`src/Hms.Hr.Web/Program.cs` is then thin: connection string, three contexts, HR services, nav
registry, `AddHmsPlatform`, `UseHmsPlatform`, `MapRazorPages`.

`kernel.host_kind` single row written at first boot (`erp` | `hrm`). ERP accepts either (upgrade
path); HRM refuses `erp`.

`CurrentBranch` in the kernel: ERP returns 1; HRM resolves per user.

### Step 5 — deploy artifacts (R7)

`deploy/hrm.Dockerfile` (publishes `src/Hms.Hr.Web`), `compose.hrm.yml`, a signed
`entitlements/hrm-only.json` from `eng/gen-hrm-entitlement.sh`, and a RUNBOOK section covering
install, entitlement replacement, and vendor key custody.

### Step 6 — guards (R8)

Re-point `eng/check-{ui-tokens,no-external-hosts,no-native-date,fkeys}.sh`,
`check-lifecycle-traceability.sh`, `role-journeys.py`, `ViewGuardPermissionTests`,
`HandlerPermissionTests` at all UI roots. Add the meta-test that fails on a page outside the scanned
set. Add `hr` to the additive-migration loop in `.github/workflows/ci.yml`. Fix
`ModuleBoundaryTests.Modules[]` (missing Emr/Ot/Radiology) and the stray `Hms.Billing`
project-reference in `Hms.Registration.csproj`.

## 3. Decisions

ADR-0025 and ADR-0026 are written with this spec. No PM questions originate here — P26–P29 belong to
the parent spec 0034.

## 4. Verification

1. `dotnet build hms-erp.slnx` — warnings are errors.
2. All six `eng/check-*.sh` guards, re-pointed.
3. `dotnet test hms-erp.slnx` — existing suites unchanged, plus new entitlement endpoint tests and
   both-direction host-boot tests.
4. Existing Playwright suite against the ERP host: the extraction's regression net.
5. Both hosts boot locally; `docker compose -f deploy/compose.hrm.yml up -d` serves the HRM host.
6. `eng/verify/measure-rss.sh` on both images.
</content>
