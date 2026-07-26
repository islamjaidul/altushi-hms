# Tasks — 0012 UI pass

## P0 — module wiring

- [x] Register `ApptDbContext` / `NotifDbContext` in `Program.cs` + `HmsTx`
- [x] `InitAppt` / `InitNotif` migrations
- [x] `SmsQueue` (+ `SmsOptions`, `SmsState`, `SmsEvent`) — simulation default (edge 3)
- [x] DI: `AppointmentsService`, `SmsQueue`, `HospitalIdentity`
- [x] `CounterContext` — open-session lookup + get-or-create encounter

## P1 — design system

- [x] `tokens.css` re-derived from the reference (palette, type, metrics)
- [x] `app.css` component kit (card, buttons, forms, tables, tiles, pills, POS, board, sheet, toast)
- [x] Material Symbols subset vendored to `wwwroot/fonts` (8 KB, 64 glyphs) — no CDN
- [x] `app.js` — toast, `/` search focus, Enter-advances, consequence confirm, live POS totals

## P2 — shell

- [x] `_Layout.cshtml`: dark rail with permission-composed groups, topbar, status footer, F-key map
- [x] `_Letterhead` / `_SheetFooter` / `_PrintTools` partials
- [x] `Ui` helpers (lakh/crore grouping, amount in words, age display, status word+colour)
- [x] `Perm` policy constants; every page carries `[Authorize(Policy = …)]`

## P3 — screens

- [x] `/` role home (3–6 large actions)
- [x] `/registration/new` (+ duplicate warning, forgiving age/DOB, live ID-card preview)
- [x] `/registration` directory · `/registration/{id}/card`
- [x] `/appointments` serials & queue
- [x] `/billing/session` open counter
- [x] `/billing/opd` POS (+ discount approval round trip)
- [x] `/billing/dues` collection · `/billing/invoice/{id}` money receipt
- [x] `/billing/day-close` (+ `/billing/statement/{id}`)
- [x] `/diagnostics/order` test POS · `/diagnostics/order/{id}` slip + labels
- [x] `/diagnostics/delivery` report handover
- [x] `/lis/board` pipeline · `/lis/results` entry · `/lis/verify` e-sign · `/lis/report/{id}`
- [x] `/dashboard` MD view
- [x] `/admin/approvals` · `/admin/users` · `/admin/masters` · `/admin/audit`
- [x] `/notifications/tray` SMS tray

## P4 — print

- [x] ID card, money receipt, test order + tube labels, investigation report, day-close statement
- [x] Provisional watermark on unverified reports (§7 U7)

## P5 — verification

- [x] `DevSeed`: doctor schedules, result templates with reference ranges
- [x] Every nav route renders 200 for every seeded role (`eng/verify/nav-smoke.sh`)
- [x] Golden thread green end to end (`eng/verify/golden-thread.py`)
- [x] Discount approval + due collection green (`eng/verify/discount-and-dues.py`)
- [x] `check-ui-tokens` / `check-fkeys` / `check-no-external-hosts` pass
- [x] 80 existing tests still pass (kernel 22, architecture 17, integration 40, print 1)
- [x] Cross-verified against §9A.2 / §9A.4 / §7 / §11 / §12 and 05 §5 — findings in `notes.md`

## Follow-ups this pass surfaced (not in 0012's scope — need their own spec)

- [ ] Refund / cancel request screen (05 §5 screen 7; §11 `Cancelled⚿` / `Refunded⚿`)
- [ ] Masters editing + bulk price-list import UI over the existing `ImportService` (§9A.1 F1)
- [ ] Counter-session reopen with approval (US4.2 AC)
- [ ] `adm.referrer` master, then referrer capture on orders (§9A.2 module 4)
- [ ] Server-rendered PDF affordance on documents (05 §6)
- [ ] Patient type-ahead endpoint to replace the recent-patients `<select>` (§7 U5)
