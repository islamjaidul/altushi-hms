# 0050 — Sylhet Evergreen Hospital: rebrand, logo, login, and a sidebar that folds

- **Status:** Done
- **Date:** 2026-08-04
- **PRD ref:** §7 (operator UX; visual identity is deployment configuration, not PRD scope)
- **MVP:** in scope — presentation readiness of shipped surfaces; no new product scope
- **Requested by:** the owner, presenting tonight

## Problem

The product still wears its development identity: "Altushi General Hospital" on the login
page, sidebar, browser title and every printed letterhead; patient IDs carry the ALT- series
prefix; there is no logo, no favicon, and the login page is a flat gradient. The sidebar is a
fixed rail with no way to collapse module groups, which at 14+ modules means constant
scrolling. Tonight the software is presented as Sylhet Evergreen Hospital and must look the
part.

## Requirements

- [M] Rename every rendered surface to **Sylhet Evergreen Hospital**: login, sidebar brand,
  browser titles, printed letterheads and footers, registration-card banner, SMS hospital
  placeholder, seeded branch name.
- [M] The **Evergreen logo** appears on the login page, sidebar, printed letterhead, and as
  the browser favicon (from the supplied artwork; no external hosting).
- [M] New patient IDs use the **SEH-** series; the pharmacy walk-in account keeps working on
  existing databases.
- [M] Login page gets a **professional blurred background** treatment; existing login
  behaviour and validation untouched.
- [M] Sidebar module groups **expand and collapse**, remember their state per browser, and
  degrade to fully expanded without JavaScript.

## Acceptance criteria

1. No rendered surface (screen, print, SMS text, title bar) shows "Altushi"; the logo and
   name appear on login, sidebar, letterhead, favicon.
2. A new registration issues `SEH-......`; a pharmacy walk-in sale succeeds on a database
   that predates the rename.
3. Login E2E (spec-0040) passes; the login card sits on a blurred brand background.
4. Sidebar groups toggle with mouse and keyboard, persist per browser across reloads, and
   the group containing the active page is always open; with JS disabled all groups render
   expanded.
5. UI guards pass: token/class/icon/no-external-host checks.

## Out of scope

- Renumbering existing ALT- patients (historical IDs remain valid).
- The signed entitlement customer string (not user-visible; regenerating dev keys is a
  separate chore).
- SMS templates already overridden in a deployment's database (they keep their stored text).
- Whole-rail icon-only collapse (stretch item; group accordion is the requirement).

## Risks / open questions

- Two UHID series (ALT- historical, SEH- new) coexist by design; search/dedup work on full
  UHID strings so no collision is possible.
