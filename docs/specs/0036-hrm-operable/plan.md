# 0036 — Plan

Ordered so each step is independently verifiable. The two CI guards land with the fixes they
protect, and each is proven to fail on the tree before the fix.

## 1. The guards first, so the fixes are provable

- `eng/check-icon-glyphs.sh` — extract every `class="icon…">ligature` from `src/**/*.cshtml` plus the
  nav registries' icon arguments; read the subset's GSUB ligature set; fail naming any missing.
  Needs `fontTools`; skips with a loud message if unavailable so CI without it does not silently pass.
- `eng/check-css-classes.sh` — every `class="…"` token in `src/**/*.cshtml` must be defined in
  `src/Hms.Shell/wwwroot/css/*.css`, or be on a short allow-list of utility/state names set from
  JavaScript. Fails on `filter-row`, `check`, `inline-form` today.

Both wired into `.github/workflows/ci.yml` next to `check-ui-tokens.sh`.

## 2. CSS (R4)

`.filter-row` — the search-and-submit row the HR screens assume: flex, `align-items: end`, `gap`,
wrapping, with the free-text input taking `flex: 1 1 220px` so the button never lands on top of it.
`.check` — label + checkbox on one baseline, 18px hit target (§7: non-technical operators, low
typing speed). `.inline-form` — a form that lays out as a row inside a table cell or card head.
Tokens only; `check-ui-tokens.sh` stays green.

## 3. Icons (R5)

`eng/build-icon-subset.sh` — subsets `MaterialSymbolsOutlined[FILL,GRAD,opsz,wght].ttf` down to the
ligature list the guard extracts, writes `MaterialSymbolsOutlined-subset.woff2`. The full font is a
build input, not a runtime one: `check-no-external-hosts.sh` is unaffected. Six ligatures join the
existing 117.

## 4. Platform identity administration (R1, R2, R3)

New in `src/Hms.Shell`:

| Type | Role |
|---|---|
| `PlatformScope(KernelDbContext, AuthDbContext)` + `IPlatformTx` | the two-context seam, mirroring `HrScope`/`IHrTx` |
| `PlatformPerm` | `admin.users.manage`, `admin.audit.read` — claims both hosts share |
| `PermissionCatalog` / `PermissionDescriptor(Claim, Label, Group)` | what this host ships, DI-registered |
| `Pages/Admin/Users.cshtml(.cs)` | moved from `src/Hms.Web/Pages/Admin/`, `HmsTx` → `IPlatformTx`, `ModuleNav.Registry` → injected catalogue |

Hosts:

- `Hms.Web/Program.cs` — `PlatformTxAdapter` over `HmsTx`; catalogue = ERP claims + `HrPerm.Claim.All`.
  Delete `Pages/Admin/Users.cshtml(.cs)`; `ModuleNav` keeps the nav row pointing at the same route.
- `Hms.Hr.Web/Program.cs` — `PlatformTx` (two contexts, one connection, one transaction); catalogue =
  `HrPerm.Claim.All` + `PlatformPerm.All`; an `Administration` nav group with Users & Roles.

`HrSeed` — `System Admin` is granted the whole catalogue, and the grant is **reconciled on every
start**, not only on first run, so the live database is repaired without a manual SQL step.

## 5. Org masters CRUD (R6)

`/hr/masters` in `Hms.Hr.Screens`, `[Authorize(Policy = HrPerm.PolicyManage)]`. One page, six
sections chosen by a `?tab=` query so the operator never loses their place. Create and rename inline;
`Active` toggles. No delete path — an `employee_assignment` row references these forever (hard rule
4's reasoning applied to masters). `/hr/policies` rows link here instead of to `null`.

Shifts carry start/end and a cross-midnight flag; leave types carry paid/unpaid and whether
sandwiched non-working days count; pay components carry earning/deduction and taxability. Each is
already a column on the entity — this screen only exposes them.

## 6. Employee record (R7)

`/hr/employees/{id}` — identity and contact, current assignment, employment-event history,
leave balances, last 30 attendance days, and a pay-structure card rendered **only** under
`hr.salary.read`. The ownership rule from 0035 stands: your own record needs no permission.

## 7. Chrome (R8)

`_Layout` takes its default F-keys and build line from `OrgIdentity`, which each host configures.
HRM: `F2 New employee · F3 Search · F10 Payroll`. Sidebar foot shows the SKU name and entitled
module count.

## 8. Seed (R9)

`HrDemoSeed` in `Hms.Hr.Web`, behind `Seed:DevUsers`, idempotent on employee count:

- 8 units, 24 designations, 6 grades, 3 shifts, 6 leave types, 7 pay components, 1 payroll policy.
- 100 employees from fixed Bangla name lists with a seeded `Random(20260802)` — deterministic.
  Codes from `NumberSeriesService`. Phones `+8801[3-9]…`, bank routing/account shaped for BEFTN.
- One `employee_assignment` and one `employee_pay_structure` each, effective from the join date,
  amounts derived from grade so the sheet is internally consistent.
- 90 days of `attendance_day` rows honouring the Fri–Sat weekly off, with a believable rate of late
  arrivals and a handful of missing-out-punch exceptions so the exception list is not empty.
- Leave applications across `Applied`, `Recommended`, `Approved`, `Rejected`.
- One payroll run for the previous month, taken to `Locked`, so the salary sheet and payslips render.

Pay figures are obviously round demo numbers (spec 0035's confidentiality note).

## Verification

1. `dotnet build hms-erp.slnx` — warnings are errors.
2. `eng/check-{ui-tokens,css-classes,icon-glyphs,no-external-hosts,no-native-date,fkeys,lifecycle-traceability}.sh`.
3. `dotnet test` per project — architecture, web, integration. New: `PermissionCatalogTests` (the
   catalogue covers every `[Authorize]` policy in both hosts), `SystemAdminHoldsEverythingTests`.
4. Both hosts boot locally; `/admin/users` and every `/hr/*` route render.
5. Deploy to the VM, rebuild the HRM image, and walk all ten screens signed in as `admin`,
   checking the rendered HTML for shell, sidebar count, primary action and no unresolved ligature.
