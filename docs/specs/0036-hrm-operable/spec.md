# 0036 — The HRM SKU has to be operable by an administrator

- **Status:** Done
- **Date:** 2026-08-02
- **PRD ref:** §5 M16, §5 M21 (user management & RBAC), §5A-16, §7 (UX principles), §12 (role matrix)
- **ADR ref:** ADR-0025 (two-host product line), ADR-0027 (payroll policy as configuration)
- **Parent:** `docs/specs/0034-hrm-product-line/` — Wave A completion

## Problem

The HRM SKU deployed and every automated check passed. Then someone signed in as `admin` and could
not do anything. Four separate defects, and **not one of them was visible to any check this
repository runs** — the same blind spot spec 0035's notes recorded, hit again from four directions.

### 1. The administrator is not an administrator

`HrSeed.Roles` grants `System Admin` exactly two claims: `hr.read` and `hr.policy.manage`. Every
other role in the seed has more. The consequences compound:

- The sidebar renders **four of eight** entries. Attendance, Roster, Payroll and My Leave are gated
  on claims the admin does not hold, so they are filtered out — correctly, by a filter working
  exactly as designed on a role that was seeded wrong.
- `/hr/employees` renders its **Add employee** button inside `@if (Model.Can("hr.employee.manage"))`.
  The admin does not hold it. The button is absent, and the screen offers no other way in.
- Every route check passed because each was run as a role that *did* hold the claim.

The deeper question is what `System Admin` means in a single-tenant product sold to one employer.
In the ERP it is one role among many in a hospital with a real segregation-of-duties story. In a
standalone HRM bought by a 200-person garment factory, the person who installs it *is* the HR head.
A superuser role that cannot open payroll is not a security posture, it is a broken install.

### 2. There is no user or role management on this host at all

`/admin/users` — create user, assign role, reset password, deactivate, and toggle any permission on
any role — exists, is complete, and lives in `src/Hms.Web/Pages/Admin/`. `Hms.Web` is the ERP host.
The HRM host ships none of it. So the one screen that could have fixed defect 1 from inside the
product is the screen the SKU does not have. An HRM customer has no way to create a second user.

This is a packaging defect that ADR-0025 anticipated for UI and the transaction seam, and missed for
identity administration: §5 M21 is not a hospital feature, it is a platform feature.

### 3. Three CSS classes the HR screens use do not exist

`.filter-row`, `.check` and `.inline-form` appear in the HR Razor pages and in **neither**
`tokens.css` nor `app.css`. Unstyled, `.input` keeps its `width: 100%` and the submit button falls
onto the next line at baseline alignment, sitting across the input's bottom border. That is the
"button overlapped with input" in the report, on `/hr/employees` and `/hr/leave` both.

An undefined class is not an error in HTML. Nothing warns, nothing fails, the page returns 200.

### 4. Six icons render as their own names

`MaterialSymbolsOutlined-subset.woff2` is a 117-ligature subset cut for the ERP's screens in spec
0012. HR introduced icons that subset never contained, so `person_off`, `pending_actions`, `rule`,
`groups`, `person` and `local_shipping` fall through to the fallback font and render as literal
lowercase text — `PERSON_OFF` sprawling across a dashboard tile. `local_shipping` proves this is not
only an HR defect: **the ERP has been shipping one broken glyph since spec 0012.**

### 5. Consequences of 1–4 that look like missing features

- **No org-master CRUD.** `/hr/policies` lists Units, Designations, Grades, Shifts, Leave types and
  Pay components, each showing `none`, four of the six with `Link: null`. It is a checklist of things
  the operator is told to configure with no screen on which to configure them. Payroll therefore
  cannot run, and the screen correctly says so — about a state the product gives no way to leave.
- **`/hr/employees/{id}` is a dead link.** The employee table links every name to a page that does
  not exist. 404 on the most obvious click on the screen.
- **The chrome is a hospital's.** The HRM host's status bar reads `F2 New patient · F3 Item search ·
  F10 Payment`, and the sidebar foot reads `MVP build`. Wrong product, and P27's neutral-vocabulary
  commitment broken on the two lines every screen shows.
- **Nothing to look at.** The database holds zero employees, so every screen is an empty state and
  no feature can be demonstrated.

## Requirements

- **R1 — a superuser that is one.** `System Admin` holds every permission the host ships, HR and
  platform alike, and gains them on an existing install rather than only a fresh one. The role's
  content comes from the permission catalogue, so a permission added later is not silently
  ungranted.
- **R2 — Users & Roles on both hosts.** The screen moves to `src/Hms.Shell/Pages/Admin/`, backed by a
  narrow `IPlatformTx` (`Kernel` + `Auth`) that each host implements, the way `IHrTx` already works.
  Create user, assign role, reset password, activate/deactivate, create role, and grant/revoke any
  permission. The ERP's copy is deleted, not duplicated — one route, one implementation.
- **R3 — a permission catalogue.** Each host registers what permissions it ships, with a
  human-readable label and group. The role matrix renders from it, so it can never offer a
  permission no screen enforces, nor omit one that exists.
- **R4 — the three missing classes**, defined in `app.css` in token terms, matching the house
  patterns already there.
- **R5 — the icon subset covers every ligature the product uses**, and `eng/check-icon-glyphs.sh`
  fails CI when a `.cshtml` uses one the font lacks. Regenerating is a scripted, repeatable step.
- **R6 — org masters CRUD** at `/hr/masters`: units, designations, grades, shifts, leave types and
  pay components — create, rename, deactivate. Deactivate, never delete: an employee's history
  references these by id. `/hr/policies` links to it instead of listing dead ends.
- **R7 — `/hr/employees/{id}`** — the record behind the link: identity, contact, bank, current
  assignment, pay structure (gated on `hr.salary.read`), leave balances, recent attendance, and the
  employment-event history.
- **R8 — host-neutral chrome.** F-keys and the sidebar foot come from the host, not from a hospital
  constant. No clinical noun on any HRM screen.
- **R9 — 100 seeded employees** for the demo: believable Bangladeshi names, units, designations,
  grades, phone numbers and bank details; assignments and pay structures; three months of
  attendance; leave applications at every state of the §11 chain; and a completed payroll run.
  Deterministic — the same seed produces the same 100 people — and behind `Seed:DevUsers`, so it
  cannot reach a real install.

## Non-requirements

- Payslip PDF, punch-file upload UI and employee document upload stay in their existing follow-ups.
- **Statutory rates stay unseeded** (ADR-0027, P26). The demo seeds a *payroll policy* and *pay
  components* because those are the employer's own; it seeds no tax slab, PF rate or leave
  entitlement it would have to invent.
- No new module, no PRD scope change.

## Acceptance

- **AC1** Signed in as `admin` on the HRM host: eight HR nav entries plus Administration, and every
  screen's primary action visible.
- **AC2** `/admin/users` on both hosts: a new user can be created, given a role, and sign in.
- **AC3** Grepping every `class="icon"` ligature in `src/` against the subset's GSUB table yields
  nothing missing, asserted by a CI guard that fails on the pre-fix font.
- **AC4** No `.cshtml` in `src/` references a class absent from the stylesheets, asserted by a CI
  guard that fails on the pre-fix tree.
- **AC5** Each of the six masters can be created from `/hr/masters` and appears on `/hr/policies`.
- **AC6** Every employee-name link on `/hr/employees` resolves to a rendered record.
- **AC7** `admin` sees 100 employees, three months of attendance, leave at four states and one
  locked payroll run.
- **AC8** No clinical noun on any HRM screen (guard over the HRM host's rendered chrome).
