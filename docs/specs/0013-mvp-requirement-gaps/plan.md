# Plan — 0013 Close the MVP requirement gaps

Approved 2026-07-26. Sequenced in five waves; each wave ends green (build + tests + gates +
the HTTP flows + Playwright) before the next begins.

## The traceability matrix

Status: **✓** built · **→** this spec · **⊘** deferred with reason.

### M1 — Patient Registration & ID Card (§9A.2 "full core")

| PRD | Requirement | Screen | Status |
|---|---|---|---|
| §5 M1 [M] | Registration, UHID, duplicate detection, search | `/registration/new`, `/registration` | ✓ |
| §5 M1 [M] | Barcode ID card print; **re-issue with audit** | `/registration/{id}/card` | → audit event on reprint |
| §5 M1 [S] | Deactivation / **merge** (US1.3) | — | → merge screen (service exists) |
| §5 M1 [S] | Photo capture via webcam | placeholder only | ⊘ no camera on the demo laptop; placeholder stays |
| §5A-2 [S] | Book number, patient type, media, promotion officer | patient type only | → media/referrer via the new referrer master |
| §5A-1 [S] | Clinical intake at registration (vitals, chronic flags) | — | ⊘ Should, belongs with M5 EMR (§9A.3 defers M5) |

### M3 — Appointment & Queue (§9A.2 "lite: schedule, serial, today's queue")

| PRD | Requirement | Screen | Status |
|---|---|---|---|
| §5 M3 [M] | Serial generation, today's queue board | `/appointments` | ✓ |
| §5 M3 [M] | Doctor master schedule (days, hours, max serials) | seeded only | → `/admin/doctors` |
| §5 M3 [M] | Modify / postpone / **cancel** / transfer with reason | Call-in / Finish only | → cancel + no-show + reason (§11 states) |
| §5 M3 [M] | Department/doctor calendar views | — | ⊘ §9A.2 says "lite"; today's queue is the MVP depth |
| §5A-3 [S] | Token queue + public monitor | — | ⊘ Should; R3 is Phase-3 portal work |

### M4 — OPD & Emergency Billing (§9A.2 "full core")

| PRD | Requirement | Screen | Status |
|---|---|---|---|
| §5 M4 [M] | Counter session, invoice, due, day-close, money receipt | `/billing/*` | ✓ |
| §5 M4 [M] | Discount request → approval | `/billing/opd` + `/admin/approvals` | ✓ |
| **§5A-4 [Must]** | **Split multi-tender in one invoice** (cash + card/mobile) | one tender per save | → multi-row tender panel |
| §5 M4 [M] | Discount **with reason code** | reason auto-generated | → operator-entered reason |
| §5 M4 [M] | **Invoice refund** with approval + reason | — | → `/billing/refund` (05 §5 screen 7, §11 `Refunded⚿`) |
| §5 M4 [M] | Invoice **cancellation** (pre-payment) | — | → same screen (§11 `Cancelled⚿`) |
| §5 M4 [M] | **Emergency billing**, minimal data, 24/7 | encounter type hardcoded `"OPD"` | → ER encounter type + counter kind |
| §5 M4 [M] | Money receipt **thermal 58/80 mm** + A4 | one layout | → thermal print CSS variant |
| §5 M4 [M] | Daily collection by counter/operator/method; due list; **discount register** | day-close statement only | → `/billing/reports` |
| §5 M4 [S] | Health package billing | — | ⊘ Should; needs the package master (post-MVP) |

### M8 — Test Order Management (§9A.2 "full core")

| PRD | Requirement | Screen | Status |
|---|---|---|---|
| §5 M8 [M] | Multi-test invoicing, barcode labels, TAT promise, delivery log | `/diagnostics/*` | ✓ |
| §5 M8 [M] | **Test catalog setup** (name, dept, price, sample type, TAT, template) | read-only | → editable under `/admin/masters` |
| §5 M8 [M] | **Referrer capture on every order** | no master | → `adm.referrer` + capture |
| §5 M8 [M] | Order **cancellation with approval & refund** | — | → shares `/billing/refund` |
| §5 M8 [M] | Department-wise income; consultant/referrer-wise business report | dashboard split only | → `/billing/reports` |
| §5 M8 [M] | Corporate customer billing | — | ⊘ M18 is §9A.3-deferred |

### M9 — LIS-lite (§9A.2 "collect → receive → manual entry → verify/e-sign → print")

| PRD | Requirement | Screen | Status |
|---|---|---|---|
| §5 M9 [M] | Collection, receive, rejection → recollection, result entry, verify/e-sign, print | `/lis/*` | ✓ |
| §5 M9 [M] | Reference ranges **by age/sex** | flat adult ranges | → banded ranges in the template |
| §5 M9 [M] | **Amendment after verification** (v2, both retained) | service only | → `/lis/amend` (approval-gated) |
| §5 M9 [M] | Worklists **by department / status** | one board | → board filters |
| §5 M9 [M] | Per-sample TAT tracking (US9.4) | — | → TAT column + breach flag |
| **R1 [Must]** | **Reporting consultant**: master, assignment, stored signature on report | pathologist only | → consultant master + signature |
| **5A-10 [Must]** | Per-modality **report template engine** | 7 hardcoded templates | → template master, editable |
| §5 M9 [M] | Analyzer-integrated capture | — | ⊘ §9A.2 explicit: manual only |

### M20 — SMS / Notification (§9A.2 "registration + report-ready, simulation mode")

| PRD | Requirement | Screen | Status |
|---|---|---|---|
| §5 M20 [M] | Event triggers (registration, appointment, report-ready) | `SmsQueue` | ✓ |
| §5 M20 [M] | Send log with status | `/notifications/tray` | ✓ |
| §5 M20 [M] | **Template management with variables** | hardcoded strings | → `/admin/sms-templates` |
| §5 M20 [M] | Gateway config; **per-module on/off**; **resend from log** | — | → settings + resend action |

### M21 — Admin, Masters, Approvals, Audit (§9A.2 "the F1 answer")

| PRD | Requirement | Screen | Status |
|---|---|---|---|
| §5 M21 [M] | Approval engine; audit trail | `/admin/approvals`, `/admin/audit` | ✓ |
| §5 M21 [M] | **User management** (create/edit, counter binding) | read-only list | → editable |
| §5 M21 [M] | **Role-based access** — role/permission editing | read-only | → permission matrix editor |
| §5 M21 [M] | **Master data**: service & test catalogs, **versioned rate plans** (US21.1) | read-only | → editable, effective-dated |
| §9A.2 mod 7 | **Bulk price-list import** (§9A.1 F1) | `ImportService`, no screen | → `/admin/import` wizard |
| **§5A-21 [Must]** | **Request Center**: Edit / Reset / Refund / Special-Discount queues | discount only | → generalised request raise + inbox |

### M22 — Dashboards (§9A.2 "today's income, collection, due, discount, dept split, variance")

| PRD | Requirement | Screen | Status |
|---|---|---|---|
| §5 M22 [M] | Today's money, dept split, counter variance | `/dashboard` | ✓ |
| §5 M22 [M] | **Date-range on every statement** | today only | → range filter |
| §5 M22 [M] | Consultant/surgeon ranking by income | — | → ranking panel |
| §5 M22 [M] | End-of-day digest | — | → digest view on `/billing/reports` |
| §5A-20 [S] | Revenue/analytics/master dashboards as distinct views | — | ⊘ Should; one MD dashboard is §9A.2's depth |

## Waves

1. **Masters & admin** — referrer + reporting-consultant + doctor masters; catalog/rate editing
   with effective dates; user & role editing; import wizard. *(F1, the biggest commercial gap.)*
2. **Money** — refund/cancel, Request Center generalisation, split tender, discount reason,
   emergency billing, thermal receipt, `/billing/reports`.
3. **Lab** — age/sex banded ranges, amendment, reporting-consultant signature, report template
   master, board filters, TAT tracking.
4. **Notifications & dashboard** — SMS templates/settings/resend; date range, ranking, digest.
5. **Registration & appointments** — reprint audit, merge, cancel/no-show with reason.

Each wave: build → 80 unit/integration tests → 3 CI gates → HTTP flows → Playwright → commit.

## Verification

The matrix above is the acceptance artifact. A row moves to ✓ only when its screen exists and
is exercised by a test in `eng/verify/`.
