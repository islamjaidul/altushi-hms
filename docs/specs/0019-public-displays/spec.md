# 0019 — R3 Public Queue Display & Report-Status Self-Service

- **Status:** Done
- **Date:** 2026-07-27
- **PRD ref:** §5A.2 R3 `[obs: MEDISpa]` (Queue Monitor, Investigation Report Tracker) +
  `[obs: PrimeMIS]` (consultant-status, Image-Report-Status), §8 N5 (privacy), M3 queue
- **MVP:** post-MVP — Wave 2 of `11-build-plan-phase2.md`

## Problem

The waiting area asks the counter two questions all day: "which serial is the doctor on?"
and "is my report ready?". Both answers exist in the system but only behind a login — so a
queue forms to ask about the queue. R3 is the unauthenticated read-only surface for exactly
these two answers and nothing else.

## Requirements

- [S] **Public queue monitor** (`/public/queue`): today's doctors with room, current
  in-chamber serial and waiting count; auto-refreshing; suitable for a lobby TV.
- [S] **Report-status self-lookup** (`/public/report-status`): patient party types the order
  number from their money receipt → per-test progress (in progress / ready for delivery /
  delivered), no login.
- **Privacy (§8 N5, P15 default):** no full names on the public surface — masked name
  (`Rahim U.`) at most; no amounts, no diagnoses, no phone numbers. Content is the P15
  recommended default until the PM answers.

## Acceptance criteria

1. Both pages render **without authentication** while every other route still redirects to
   login (the anonymous surface is these two routes only).
2. The queue page shows each doctor's current serial and waiting count and refreshes itself
   (meta refresh — works on a dumb lobby TV browser with JS off).
3. Report lookup by order number shows only masked identity + per-test status; an unknown
   number gets a comprehensible "not found" (no information leak on probing: same message
   for wrong vs. missing).
4. No money figure, full name, phone, or clinical value appears in either page's HTML.
5. Playwright: anonymous context reaches both routes, is redirected elsewhere, and the
   masked-name/no-PHI properties hold.

## Out of scope

- Token-queue engine (5A-3) — a different M3 enrichment (its own spec later).
- SMS "report ready" notification (M20 — already flows); kiosk hardware.
- Bangla UI (product constant: English operator UI; public display copy stays English).

## Risks / open questions

- P15 (already raised): what may appear on a public screen. Built to its recommended
  default (serial + doctor + masked name); one place to change.
