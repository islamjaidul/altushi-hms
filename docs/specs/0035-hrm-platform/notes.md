# 0035 — Notes

Written during the build. Deviations, surprises and what is still open.

## The repository did not compile before this spec started

`dotnet build` of `src/Hms.Web` failed with **84 errors at `HEAD` (17e162c)**, on a clean worktree,
with the only SDK installed on this machine (**10.0.302**). Verified by checking `HEAD` out into a
separate worktree and building it there before touching anything — the failure was not ours.

There is **no `global.json`**, and CI pins only `dotnet-version: "10.0.x"`, so CI floats to whatever
10.0.x is current and would have hit the same wall. The repo was green against an earlier 10.0.x.

Three Razor parser incompatibilities, all pre-existing, **all now fixed** — the repo builds.

| # | Pattern | Example | Sites |
|---|---|---|---|
| 1 | `<text>` block inside a code block | `<text> — @Ui.Money(amt)</text>` | 9, in 7 files |
| 2 | Two sibling elements in one code block | `<span>@r.Before</span> <span>&#x2192;</span>` | 3 |
| 3 | **Markup starting at column 1** inside a code block | `@if (!signed)\n{\n<form …>` | 1 (`Emr/Consult.cshtml`) |

The parser was falling back to C# mode and reading prose as identifiers (`The name 'role' does not
exist in the current context`); in #2 the `#` of `&#x2192;` was additionally read as a preprocessor
directive. `<span>` renders identically to `<text>` in every one of these positions, and #3 was
whitespace only — so all three fixes are behaviour-preserving.

**#3 took a minimal reproduction to find.** Indentation looks irrelevant and the tag balance in
`Consult.cshtml` was correct, so pattern-matching produced nothing. A 15-line scratch page reproduced
it, and three variants isolated it in one build cycle: identical markup passes when indented and
fails at column 1. Worth remembering — this class of failure is invisible to inspection.

## What was verified, and how

**Full solution: `dotnet build hms-erp.slnx` → 0 errors, 0 warnings** with
`TreatWarningsAsErrors=true`. All five content guards pass. 168 tests pass (42 architecture,
22 kernel, 104 web); the Testcontainers integration and Playwright suites were not run here.

### The standalone HRM SKU

Booted against Postgres 17.5 on a fresh `hrm` database:

- Migrated and seeded cleanly; `/health` returns `{"status":"ok"}`.
- The database has exactly **three schemas — `kernel`, `adm`, `hr`** — and 42 tables in `hr`
  (41 + `__ef_migrations`). No clinical schema exists. The two-host decision demonstrated, not asserted.
- `kernel.host_kind` = `hrm`.
- The shell library serves from the second host: `/_content/Hms.Shell/css/tokens.css`, `app.css`,
  `js/app.js` and the fonts all 200, and the title reads *"Sign in — Meghna Textiles Ltd."* —
  neutral branding, not a hospital (P27).
- `GET /hr` unauthenticated → **302 to `/login?ReturnUrl=%2Fhr`** (G10 deny-by-default intact).

### HR inside the ERP

Booted against a fresh `hms_hrtest` database with the all-modules licence:

- 42 `hr` tables alongside the thirteen clinical schemas; `kernel.host_kind` = `erp`.
- Signed in as `farid` (HR Officer): `/hr`, `/hr/employees`, `/hr/payroll`, `/hr/policies`,
  `/hr/leave`, `/hr/me` all **200**, rendering in the shared shell with HR in the sidebar.

### ADR-0026 choke point 2 — the commercial guarantee

With `deploy/entitlements/hr-only-on-erp.json` (modules: `Hr`, `Admin`) loaded into the **full ERP
host**, signed in as `shahid`, who **holds `billing.*`**:

| Route | Result |
|---|---|
| `/billing/opd`, `/billing/dues` | **refused** → `/denied?ReturnUrl=…` |
| `/lis/board`, `/ipd/board`, `/pharmacy/pos` | **refused** |
| `/hr`, `/hr/employees`, `/hr/payroll` (as `farid`) | 200 |
| sidebar | HR links only |

The permission was held and the licence still refused, by URL. That is the difference between a menu
filter and a product boundary, and it is what makes selling a module separately meaningful.

The first attempt at this **failed**: only `/hr` had been mapped, so `/billing/opd` returned 200.
`ModuleNav.RoutePrefixes` now derives the prefix→module map from the nav registry itself and throws
if two modules claim one prefix — a hand-listed map is precisely what goes stale.

### Database invariants, proven by trying to violate them

| Invariant | Result |
|---|---|
| Two overlapping open pay structures for one employee | **Refused** — `ex_employee_pay_structure_no_overlap` |
| Leave balance driven below zero | **Refused** — `ck_leave_balance_not_overdrawn` |
| `net_pay ≠ gross − deductions + shortfall` | **Refused** — `ck_payroll_line_net` |

8 exclusion constraints and 12 check constraints landed.

## Deviations from the plan

1. **`Hms.Ui` → `Hms.Shell`.** A namespace `Hms.Ui` shadows the `Ui` static class every page calls:
   from inside `Hms.Web.Pages`, the simple name `Ui` reaches the namespace first —
   `CS0118: 'Ui' is a namespace but is used like a type`.
2. **`Hms.Hr.Ui` → `Hms.Hr.Screens`**, for the identical reason, discovered the hard way: HR page
   models in `Hms.Hr.Ui.Pages.Hr` could not resolve `Ui.Money`. Same trap, second bite.
3. **Entity files split.** House style is one file per DbContext; `hr` has 41 tables, so entities are
   grouped into `HrEntities.{Masters,People,Time,Leave,Pay,Policy}.cs` with the context and factory
   still in `HrDbContext.cs`. The convention was written when modules were small.
4. **`Login`/`Logout`/`Denied` moved into the shell.** Not in the plan, but both hosts need them, and
   duplicating an authentication screen is exactly the drift ADR-0025 §4 exists to prevent.
5. **`Microsoft.EntityFrameworkCore.Design` added to `Hms.Hr`** (`PrivateAssets=all`), so the module
   drives its own `HrDbContextFactory`. A module's migration should not depend on a host compiling —
   which it could not, at the time.
6. **`Taka` promoted to the kernel** (`Hms.Kernel/Money/Taka.cs`) carrying billing's `RoundHalfUp`
   verbatim, plus `ApplyBp` and `AbsorbResidue`. Payroll needed billing's exact rounding and must not
   reference M11 to get it.
7. **`Can(...)` takes literal strings, not `HrPerm.Claim.*` constants.** The constants read better,
   but `check-lifecycle-traceability.sh` greps for `Can("…")` and reported the four handler-enforced
   permissions as enforced nowhere. A guard that cannot see enforcement is worse than a magic string.
   `HrIndependenceTests` asserts the two constant lists cannot drift.

## Fixed in passing

- **`ModuleBoundaryTests.Modules[]` omitted Emr, Ot and Radiology** — three modules shipping entirely
  unguarded by the test that exists to guard them. Now fourteen, including Hr.
- **`CrossContextQueryTests.Scopes[]` omitted six contexts** (Pharm, Ipd, Emr, Ot, Radiology) — the
  cross-context join guard could not see them either. Now all fifteen.
- **`Hms.Registration.csproj` project-referenced `Hms.Billing`** (the implementation, not its
  contracts), hidden only by Roslyn's unused-reference elision. Removed.
- **CI's additive-migration gate covered 3 of 14 contexts**; `hr` is now in the loop, because payroll
  is money and a dropped column there destroys a locked salary sheet.

## Still open

- **Integration and Playwright suites not run** here (Testcontainers + browser). No HR integration
  tests exist yet — the payroll arithmetic cases named in the plan (split-period proration, rounding
  residue, post-lock arrears, night-shift pairing, punch idempotency) are unwritten.
- **`eng/verify/hr-thread.py`** — no end-to-end thread script for HR yet.
- **Wave 0 remainder:** admin entitlement-upload screen (`/admin/entitlement`) and per-user branch
  resolution for the HRM host (P28). The `ReadOnlyEntitlementMiddleware` and
  `EntitlementProvider.Replace` behind them are built and wired; only the screen is missing.
- **Waves B–D** (spec 0037–0039): comp-off, OT bank, bonus, increment, notice board; PF/tax/welfare
  ledgers, loans, expenses; letters, reports, auth history.
- **Employee↔user linking** has no screen, so self-service (`/hr/me`) shows its "ask HR to link your
  login" state for everyone until one exists.
- **`BillingService.RoundHalfUp`** should now delegate to `Taka.RoundHalfUp` rather than duplicating it.

---

## Post-close: deployed to the shared VM, 2026-08-01

Appended after the spec closed (archive README: post-close outcomes go here, the spec is not
rewritten). Live at **https://hrm.specshipper.com**, image `hrm-app:dev`, commit `e329582`.

### Two bugs the deploy found that local work could not

**1. The HRM host's public key was never committed.** `.gitignore`'s blanket `*.pem` has an
explicit un-ignore for `src/Hms.Web/vendor-public-key.pem`; the HRM host's copy had none. Local
builds passed because the file was on disk. The VM failed with `MSB3030` halfway through a Docker
build. `eng/check-published-content-tracked.sh` now asserts every file a `.csproj` publishes is
tracked by git, and is in CI's guard step — it fails on the broken tree and passes on the fix.

**2. `db-init/01-roles.sh` hardcoded `psql -d hms`.** The standalone stack sets `POSTGRES_DB=hrm`,
so on a real HRM deployment the init script died with `database "hms" does not exist` — leaving a
Postgres with **no `hms_migrator` and no `hms_app` at all**. A failed init script does not stop
Postgres restarting, so the container merely looked *unhealthy* rather than broken. Any HRM-only
customer following RUNBOOK §10's "compose.hrm.yml unmodified" would have hit this.

Note how it surfaced: the shared-box deploy was designed to *avoid* starting that second Postgres,
and only found the bug because `--no-deps` was missing and it started anyway. The shortcut exposed
the flaw in the thing it was shortcutting.

**3. `--no-deps` is load-bearing.** Naming `app` does not stop compose pulling in the base file's
database, and `depends_on: []` in an override does not clear it — compose merges the long-form
mapping rather than replacing it.

### Verified on the deployment

| Claim | Evidence |
|---|---|
| Three schemas only | `\dn` on `hrm` → `kernel`, `adm`, `hr`. No clinical schema exists. |
| 41 HR tables | 42 counted in `hr` (41 + `__ef_migrations`) |
| Host interlock | `kernel.host_kind` = `hrm` |
| Health | `GET /health` → `{"status":"ok"}` over HTTPS |
| Deny-by-default | `/hr` unauthenticated → 302 `/login?ReturnUrl=%2Fhr` |
| Shell RCL serves | `/_content/Hms.Shell/{css/tokens.css,css/app.css,js/app.js,fonts/…}` all 200 |
| Neutral branding (P27) | `<title>Sign in — Meghna Textiles Ltd.</title>` |
| All eight HR screens | 200 as `farid` (HR Officer) over HTTPS |
| **No clinical code** | authenticated, `/billing/opd`, `/ipd/board`, `/lis/board`, `/pharmacy/pos`, `/emr/queue`, `/radiology/worklist` all **404** — absent, not merely refused |
| Footprint | **125.9 MiB** RSS vs the ERP's 194.8 MiB; image 339 MB |
| ERP unaffected | `hms-app-1` still `Up 3 days (healthy)`, never rebuilt; `/health` 200 |

The 404s are the load-bearing result. A 403 would prove a guard; a 404 proves the code is not in
the image — which is what ADR-0025 claims and what a customer is actually buying.

### Deliberate compromises on this box

The VM has ~3 GB shared across four products and was already ~950 MB into swap, so the HRM runs
**app-only against the ERP's Postgres** in a separate `hrm` database (`compose.hrm.vm.yml`), not
the full stack. Consequences, all recorded in RUNBOOK §10:

- **The `hrm` database is not backed up** — `hms-backup-1` dumps only `hms`.
- It shares the ERP's connection ceiling (the known CONC-4 pool issue).
- The entitlement is a **dev** licence signed with `eng/dev-keys/`, naming *"Meghna Textiles Ltd.
  (DEV)"* and expiring 2028-07-01. Not a sale artifact.
- `HRM_SEED=true`, so the demo cast exists with the shared `Demo#1234` password. Like the ERP
  deployment, this is a demo and has not been through RUNBOOK §9's go-live switch.

### Unchanged by deployment

Everything under "Still open" above remains open. In particular **no test covers a single taka of
payroll arithmetic**, and the deployment does not make that better — it makes it reachable.

### Post-deploy fix: sign-in landed on 404

Reported by the user immediately after deployment, and correct: **login worked, the landing page
did not exist.** `Login.cshtml.cs` ends `LocalRedirect(returnUrl ?? "/")`, so signing in from the
login page directly — rather than being bounced there off a protected URL — goes to `/`. The ERP
answers that with its dashboard at `@page "/"`; the HRM host had no page at `/` at all.

**Why the deployment verification above did not catch it.** Every row in that table is true and
none of them exercise the redirect: the HR routes were requested directly, and the login POST was
checked for a 302 and a working cookie, which is exactly what it returned. The defect lives only
in *following* the redirect. Route-by-route checking cannot see it — a host is not usable because
its pages work, it is usable because the path a user walks works. Re-verified by following the
redirect for all four seeded accounts: each lands on `/hr` with 200.

The root is claim-aware rather than a blind redirect to `/hr`: `/hr` requires `hr.read`, while the
self-service **Employee** role holds only `hr.leave.apply` and would have been denied there —
trading one dead end for another. Staff hold both, so ordering decides it. *Not runtime-verified:*
no Employee-role user is seeded, so that branch is reasoned, not observed.

`HostRootRouteTests` asserts every host has a page at `/`, and fails on the tree without the fix.

### Post-deploy fix: the login form had no validation

Neither box was checked, and `OnPostAsync` never consulted `ModelState`. Beyond the missing
message, the sign-in call runs with `lockoutOnFailure: true` and Identity charges a wrong password
**against the account** — so a real username with an empty password consumed one of that account's
attempts. On a shared counter PC, stray Enter presses lock out a colleague who did nothing. The
guard now returns *before* the sign-in call, not merely before the redirect.

Per-field messages replace the single "check the username and password", which is right for a
wrong credential and useless for an empty box because it does not say which box. `.input-invalid`
already existed in `app.css` and was used by nothing — the affordance had been designed and never
wired up. Only `.help-text.bad` is new.

**The first attempt shipped broken and I reported it as working.** `Validate` called
`password.Length`; MVC binds an empty field to `null` (`ConvertEmptyStringToNull`), which
overwrites the `= ""` initialiser — so the validation meant to stop blank submits returned HTTP 500
on every one. The unit tests passed because I wrote them with `""`: I tested the contract as I
imagined it rather than as the framework calls it. Both facts are the lesson, and the second is the
worse one — seven green tests bought confidence they had not earned. The suite now leads with
`null` and was checked against the broken expression first, where it reproduces the
`NullReferenceException`.

A related discrepancy, found only by testing the deployment: `ConvertEmptyStringToNull` tests
`IsNullOrWhiteSpace`, not `IsNullOrEmpty`, so a *wholly* whitespace password binds to null upstream
and can never be submitted — the code accepts it, the framework never delivers it. Passwords with
real characters and surrounding spaces are unaffected, which is what "never trimmed" rests on.

Live matrix, all eight cases (blank/blank, no username, no password on a real account, whitespace
only, wrong password, unknown user, username with spaces, valid) verified against
`hrm.specshipper.com` after deployment — not inferred from the tests.
