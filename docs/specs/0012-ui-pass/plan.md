# Plan — 0012 UI pass

Approved 2026-07-26. Implements `spec.md`. The services from S2–S5 are test-proven; this plan
is presentation + wiring only, with the two module wirings that were never finished.

## Scope boundary (hard rule 2)

The design reference (`docs/architecture/assets/altushi-hms-demo.html`) carries ~50 nav items
across 13 groups — pharmacy, IPD, OT, blood bank, stores, HR, accounts. PRD **§9A.3 excludes
all of them from the MVP**. This plan therefore implements the reference's *visual and
interaction grammar* across the **14 MVP routes in `ModuleNav.Registry`** (§9A.2's 8 modules)
plus the counter-session screen the money path needs. No excluded module gains a screen, a
route, or a nav entry. Proposals to add one go to the PM (hard rule 2).

## Steps

### P0 — module wiring (blocks every screen below it)

1. `ApptDbContext` and `NotifDbContext` registered in `Program.cs`, attached in `HmsTx`, with
   `InitAppt` / `InitNotif` migrations. Both contexts exist but were never composed — the
   appointments and SMS screens cannot run without this.
2. DI for `AppointmentsService`, `DiagnosticsService`, `ChargePoster` (as `IChargePoster`),
   and a new `NotificationService` (queue + simulation stamp per §9A.2 module 8).
3. `Encounter` creation helper — the POS screens need an encounter before charges post.

### P1 — design system (05 §1)

4. `tokens.css` re-derived from the reference: the exact palette, type scale, spacing and
   component metrics. Every colour is a custom property — `eng/check-ui-tokens.sh` fails the
   build on a hardcoded hex outside this file.
5. `app.css` component kit: card, section header, button family (primary/secondary/tint/
   danger/icon), input/label/select, table (+ zebra, hover, totals row), KPI tile, status pill,
   pipeline column + card, POS grid, toast, print sheet, modal.
6. Material Symbols Outlined **vendored into `wwwroot/fonts`** as a subset of the icons the MVP
   screens use. No CDN — `eng/check-no-external-hosts.sh` is the gate (edge 1).

### P2 — shell (05 §2)

7. `_Layout.cshtml` rebuilt: dark sidebar (`#16283A`) with permission-composed groups and
   dept-accent dots, topbar (crumb › title, global patient search, notifications, user chip,
   sign out), status footer (server/DB, user+role, counter identity, F-key map, business day).
   The reference's "Viewing as" role switcher stays out — production switches users at the
   lock screen (05 §2, ADR-0019).
8. Shared partials: patient banner (§7 U9), toast host, print-preview modal, `?` micro-help slot.

### P3 — screens, in golden-thread order

| Route | Template | Service behind it |
|---|---|---|
| `/` role home | tiles | NavComposer |
| `/registration/new` | doc+preview | `RegistrationService` (+ dup warning, ID card) |
| `/registration` | list | `RegDbContext` search |
| `/appointments` | list+form | `AppointmentsService` |
| `/billing/session` *(new)* | doc | `BillingService.OpenSessionAsync` |
| `/billing/opd` | POS | `RateResolver` → `PostChargeAsync` → `CreateInvoiceAsync` → `CollectAsync` |
| `/diagnostics/order` | POS | `DiagnosticsService` + billing, TAT promise, labels |
| `/billing/dues` | list+pay | `CollectAsync` (row-locked) |
| `/lis/board` | pipeline | `LisService` collect/receive, due-hold rule |
| `/lis/verify` | list+grid | `EnterResultAsync` / `VerifyAsync`, H/L flags |
| `/billing/day-close` | doc | `DayCloseService` (expected/counted/variance) |
| `/dashboard` | tiles+charts | `bill.v_dashboard_day` |
| `/admin/approvals` | inbox | `ApprovalEngine` |
| `/admin/users`, `/admin/audit` | list | `AuthDbContext`, `kernel.audit_event` |
| `/notifications/tray` | list | `NotifDbContext` (simulation) |

Every page carries its `perm:module.action` policy — the nav and the endpoint read the same
claim, so the sidebar cannot drift from enforcement (05 §7).

### P4 — print (05 §6)

9. Letterhead partial + money receipt, test order & labels, lab report, day-close statement,
   ID card. Print / Preview / PDF are the same three affordances on each (§7 U10).

### P5 — verification

10. `DevSeed` extended: doctor schedules, a starter history so the dashboard is never empty
    (edge 4), counters already seeded.
11. Manual pass: every sidebar item as each role, then the golden thread end to end.

## Out of scope

Per `spec.md`: print golden-file completion, micro-help page content, the timed §9A.4 CI tests.
