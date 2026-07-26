# 05 — UI Architecture

- **Status:** Draft for PM review · **Date:** 2026-07-26 · **Spec:** `docs/specs/0003-mvp-architecture/`
- **Design reference (binding):** `assets/altushi-hms-demo.html` — the user-approved "Altushi HMS" interactive demo. This document maps that design onto PRD §7's fifteen binding principles and states how each is **enforced by architecture**, not left to good intentions.
- Implementation: server-rendered Razor + a small self-hosted JS runtime (ADR-0001). No CDN, no external font/icon fetch (edge 1).

## 1. Design system (tokens extracted from the reference)

| Token group | Values |
|---|---|
| Type | **Public Sans** (400/500/700, self-hosted woff2) + **Material Symbols Outlined** icon font + bundled Bengali font (ADR-0014). Base ≥ 16px equivalent (§7 U3) |
| Neutrals | text `#17211F` / `#44524E` · muted `#5B6A66` `#7C8B87` `#9DACA7` · borders `#C4CDC9` `#E3E8E5` · surfaces `#E9EEEC` `#EDF1EF` |
| Primary | `#1B5E9C` (actions, active nav `#22456B`, dark `#134A7D`) |
| Semantic | danger `#B3403F` · success `#2F7D4F` · warning/amber `#A9711C` — statuses always **colour + word** (§7 U12) |
| Module accents | per-group dot colours (`DEPT_COLORS` in the reference): front-desk teal, diagnostics purple `#8E58B4`, blood red `#B3403F`, accounts amber, etc. — wayfinding, never sole meaning carrier |
| Metrics | 44px minimum targets (§7 U3), primary action bottom-right, 1366×768 first (§16.1 A2) |

Tokens ship as CSS custom properties in one stylesheet; screens may not hardcode colours (lint check).

## 2. Application shell (from the reference, verbatim in structure)

- **Left sidebar:** hospital monogram + name (from Settings — identity applied everywhere, ref's letterhead system), grouped nav with module-accent dots. **Composed from permissions ∩ entitlements** (ADR-0016/0019): a role sees only its groups — Rina the receptionist sees 4 items, not 22 modules (§7 U1). The reference's "Viewing as" role switcher ships in **demo builds only** (it is a sales device; production switches users via the lock screen, ADR-0019).
- **Top bar:** breadcrumb (group › page), global patient search (`/` focuses it anywhere), notifications, user chip.
- **Status footer:** server/DB state, logged-in user + role, counter identity, active F-key map for the current screen (§7 U4 — shortcuts are *shown*, never hidden knowledge; edge 10), business date.
- **Patient banner:** one shared component (name, UHID, age/sex, phone, due flag) pinned on every patient-context screen (§7 U9).
- **Toasts** for non-blocking confirmations; blocking modals only for consequence previews (§7 U8).

## 3. Interaction grammar (kernel-level JS modules, reused everywhere)

| Capability | Contract |
|---|---|
| **Type-ahead** (§7 U5) | Every master reference field: 2–3 chars → ranked results (trigram endpoint, ≤ 300 ms target); arrow/Enter selection; never a free-text foreign key. One JS module, one Razor tag helper |
| **Keyboard-first** (§7 U4) | Form tab order declared per screen; Enter advances; F-keys per the reference (F2 new patient, F3 item search, F9 hold/recall, F10 payment) reserved product-wide; Esc = cancel with confirm-if-dirty |
| **Barcode wedge** (§7 U6) | Global listener detects scanner-speed input bursts + terminator; routes by prefix (UHID card → patient context, sample → LIS stage advance, delivery slip → delivery screen). Typing remains the fallback path on the same fields |
| **Forgiving inputs** (§7 U13) | Date fields accept `d/m/yy`, `dd-mm-yyyy`, age⇄DOB dual field (02 §2.2); phone auto-format `01XXX-XXXXXX`; money fields integer-only with thousand grouping |
| **Consequence preview** (§7 U8) | Destructive/financial confirms render a plain-English summary partial ("Refund ৳500 of INV-…; requires Supervisor approval; original receipt stays on record") |
| **Micro-help** (§7 U14) | Every screen registers a `?` panel: one-page visual guide served from the app (authored per screen in the build plan; the trainer that never resigns) |
| **Error-proofing** (§7 U7) | Illegal options are *absent, disabled, or unselectable* — expired rate versions unselectable, unverified results have no "print final" control, closed sessions expose no edit affordances. Server re-validates identically (UI is a convenience layer over the same policy checks) |

## 4. Screen templates (the reference's reuse pattern, made explicit)

Four templates cover ~90% of screens; each is one Razor layout + partial kit:

1. **Register/list** — KPI tiles + filter bar + table + row actions (reference: "same list template reused across registers, ledgers and setup screens"). Used by: patient directory, due list, delivery log, audit views, masters.
2. **POS/billing** — catalogue search left, cart right, totals block, tender row, big bottom-right action (reference's OPD/diagnostic billing). Used by: OPD billing, diagnostic order invoice; later pharmacy POS lands on it unchanged.
3. **Pipeline board** — columns = §11 states, cards advance by barcode scan or click (reference's LIS sample pipeline). Used by: LIS work board; later OT schedule.
4. **Document + preview** — form left, live print-preview right (reference's registration/ID-card and letterhead screens). Used by: registration, report verify, print previews (edge 2's PDF fallback *is* this pane).

## 5. The high-frequency screens (§7.3 — the ~25 that get the deep design)

MVP builds 16 of the ~25 (the rest belong to post-MVP modules; listed for template fit):

| # | Screen | Template | Interaction contract (the parts that are binding) |
|---|---|---|---|
| 1 | Role home | tiles | 3–6 big actions per role (§7 U1); ≤ 3 clicks to any daily task (U11) |
| 2 | Patient registration | doc+preview | ≤ 60 s keyboard-only (§9A.4); duplicate warning modal non-blocking; ID card preview live; Register-&-print primary |
| 3 | Patient search/directory | list | `/` from anywhere; 2-char type-ahead; barcode card scan jumps straight to patient |
| 4 | OPD invoice (POS) | POS | ≤ 90 s median (§15); unbilled charges auto-appear via SSE (the demo seam); discount field triggers approval flow inline at threshold |
| 5 | Diagnostic order invoice | POS | multi-test entry ≤ 2 min; TAT promise shown per test and totalled; referrer type-ahead; pay → labels print |
| 6 | Due collection | list+pay | row-locked collect (ADR-0015); partial payments; receipt prints |
| 7 | Refund/cancel request | doc | reason mandatory; consequence preview; lands in approver inbox |
| 8 | Approvals inbox | list | per-role queue, approve/reject with note, SSE-live (reference's voucher-approval pattern) |
| 9 | Serial issue / today's queue | list | serial constraint surfaced as "next free is N" (ADR-0015); doctor cards with today counts (reference) |
| 10 | Sample collection | pipeline | scan → advance; rejection requires reason → child sample + labels |
| 11 | Sample receive | pipeline | scan-driven; exception queue visible |
| 12 | Result entry | form grid | keyboard grid; H/L auto-flags from ref ranges (02 §2.2); save ≠ verify |
| 13 | Verification queue | list+doc | pathologist/reporting-consultant e-sign (edge 34); verify → report-ready SMS |
| 14 | Report delivery | list | delivery slip barcode scan; version-aware (edge 22); log collector |
| 15 | Counter day-close | doc | expected vs counted, variance highlighted not blocked (edge 18); print statement; lock |
| 16 | MD dashboard | tiles+charts | today's income/collection/due/discount, dept split, counter variance; seeded history keeps it non-empty (edge 4); every number drills to its register |
| — | *Post-MVP on same templates:* pharmacy sale (POS), indoor issue, ward service posting, folio view, discharge bill, PO/GRN screens | | |

Masters + import (module 7) use the register/list + doc templates with the ADR-0010 wizard.

## 6. Print pipeline in the UI

Every printable action = same three affordances (§7 U10, edges 2/10): **Print** (browser silent-print profile per counter), **Preview** (the identical layout on screen), **PDF** (server-rendered, ADR-0009). Document layouts are Razor partials composed with the letterhead identity block (reference's "one letterhead system"); thermal (58/80 mm) vs A4 selected per document type. Bangla content renders through the bundled font end-to-end (ADR-0014).

## 7. Enforcement summary (how §7 stays true after handoffs)

- Tokens/lint: no hardcoded colours/fonts; base-size floor checked.
- Templates: new screens must use a template or justify an exception in review; the four templates implement U2/U3/U9 once.
- Keyboard map registry: F-key/shortcut assignments are declared data — collisions fail CI; the footer renders from the registry (U4).
- AuthZ-composed nav (U1) and error-proofing-by-absence (U7) come from the same server policy — UI cannot drift from enforcement.
- The §9A.4 timed tasks (60 s registration, 2 min diagnostic invoice, keyboard-only) are **automated UI tests** run against seeded data — regression on operator speed fails the build, which is what "binding requirement" means in practice.
