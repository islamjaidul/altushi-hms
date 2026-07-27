# 0019 — Plan

## Approved: 2026-07-27

| # | Requirement | Surface | Proof |
|---|---|---|---|
| 1 | Queue monitor [S] | `/public/queue` — `[AllowAnonymous]`, meta-refresh 15 s, doctors × (room, in-chamber serial, waiting) | Playwright anonymous context |
| 2 | Report status [S] | `/public/report-status` — GET form, order no (`LB-00042` or bare digits) → per-test pills | Playwright + probe-safety check |
| 3 | Privacy | `Ui.MaskName` helper (first name + surname initial); no amounts/phones/values rendered | Playwright asserts absence |

Technical: two Razor pages under `Pages/Public/`, `[AllowAnonymous]` overriding the fallback
policy; read-only queries over Appt (queue) and Diag+Reg (order → tests → states). A shared
minimal look (big type for the TV screen) using existing tokens. No nav entries (public
surface is not part of the operator sidebar). No new tables, permissions or migrations.
