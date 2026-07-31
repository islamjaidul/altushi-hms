# 0035 — Tasks

## Unblocking (not planned; the repo did not compile)

- [x] Reproduce the 84-error build failure at `HEAD` in a clean worktree — proves it is pre-existing
- [x] Fix `<text>` blocks in code blocks — 9 sites, 7 files
- [x] Fix sibling elements in code blocks — 3 sites (`Admin/Audit`, `Admin/Sms`, `Public/Queue`)
- [x] Isolate the third pattern with a minimal repro (markup at column 1) and fix `Emr/Consult.cshtml`
- [x] `dotnet build hms-erp.slnx` → **0 errors, 0 warnings**

## R1 — shared UI library

- [x] `src/Hms.Shell` razor class library (`Hms.Ui` renamed — namespace/type collision on `Ui`)
- [x] Move `_Layout`, `_Letterhead`, `_PrintTools`, `_SheetFooter`, `tokens.css`, `app.css`, js, fonts
- [x] Move `HmsDateTagHelper`, `Ui.cs`, `HmsPageModel`, `Documents.cs`
- [x] `HospitalIdentity` → `OrgIdentity`, reading `Org:*` with `Hospital:*` fallback (P27)
- [x] Move `Login` / `Logout` / `Denied` — both hosts need them
- [x] Assets served at `/_content/Hms.Shell/…`; fonts referenced relatively so they resolve
- [x] `HmsPageModel.BranchId` made virtual for P28

## R2 — transaction seam

- [x] `HrScope` + `IHrTx` in `Hms.Hr`
- [x] `HmsTx.Hr` property — one line, like every other module
- [x] `HrTxAdapter` in the ERP host; `HrTx` in the HRM host
- [x] Zero changes to the 76 existing page models

## R3 — entitlement enforcement (ADR-0026)

- [x] `RequireModuleAttribute`, `ModuleEntitlementRequirement`, `ModuleEntitlementHandler`
- [x] `module:` policies resolved by `PermissionPolicyProvider`, alongside `perm:` not instead of it
- [x] `ModuleRouteConvention` applied by route prefix
- [x] `ModuleNav.RoutePrefixes` derived from the nav registry, throwing on a shared prefix
- [x] `ReadOnlyEntitlementMiddleware` — P6's grace → read-only ladder, upload screen exempt
- [x] `EntitlementProvider.Replace` — atomic swap, refuses a different customer's licence
- [x] **Proven**: HR-only licence on the ERP refuses `/billing/opd` to a holder of `billing.*`
- [ ] `/admin/entitlement` upload screen — provider and middleware built, screen not written

## R4–R7 — second host, interlock, deploy

- [x] `HmsPlatform.AddHmsPlatform` / `UseHmsPlatform` — shared identity, cookie, authz, headers
- [x] `src/Hms.Hr.Web` host + `HrSeed` (roles, cast, approval policies; **no policy numbers**)
- [x] `kernel.host_kind` interlock — ERP adopts an HRM database, HRM refuses an ERP one
- [x] `deploy/hrm.Dockerfile`, `deploy/compose.hrm.yml`
- [x] `eng/gen-dev-entitlement.sh` emits three signed files (ERP, HRM, HR-only-on-ERP)
- [ ] Per-user branch resolution in the HRM host (P28)
- [ ] RUNBOOK section for the HRM SKU and vendor key custody

## R8 — guards and tests

- [x] `check-lifecycle-traceability.sh` scans every `Pages` root and every `*Perm.cs`
- [x] HR routes added to `eng/verify/role-journeys.py`; guard passes
- [x] `ModuleBoundaryTests` — added Emr, Ot, Radiology (previously unguarded) and Hr
- [x] `CrossContextQueryTests` — added the six missing scopes
- [x] `HrIndependenceTests` — HR references no module or host; permission lists cannot drift
- [x] `hr` added to CI's additive-migration loop
- [x] Removed the stray `Hms.Billing` project reference from `Hms.Registration`
- [x] 168 tests pass (42 architecture, 22 kernel, 104 web)
- [ ] HR integration tests (Testcontainers) and `eng/verify/hr-thread.py`
