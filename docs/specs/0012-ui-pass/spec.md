# 0012 — UI pass: working screens per the Altushi design reference

- **Status:** Done
- **Date:** 2026-07-26
- **PRD ref:** §7 (binding UX), §9A.2, §9A.4
- **MVP:** in scope

## Problem

The deployed shell has nav links but no screens behind them — every menu item 404s, and the
layout only approximates `assets/altushi-hms-demo.html` instead of implementing it. User verdict:
"no single menu is working." The S2–S5 services and tests exist; the screen layer does not.

## Requirements

- [M] Every nav route renders a working screen wired to the existing services — no dead menu items.
- [M] Visual grammar per the design reference (05 §1–§4): its exact shell, tokens, tables, POS
      layout, pipeline board, tiles — extracted from the reference source, not invented.
- [M] Golden-thread flows operable end-to-end in the browser: register → serial → order+invoice
      → pay → collect/receive → result → verify → deliver → day-close → dashboard.
- [M] Counter-session open/close UI; starter catalog + counters seeded so billing works day one.
- [S] F-key map, type-ahead and barcode wiring on the screens that define them (05 §5).

## Acceptance criteria

1. Clicking every sidebar item as each role yields a functional page (no 404/500).
2. Golden thread executed in a real browser against the deployed site.
3. Screens visibly match the reference (spot-check against the extracted design spec).

## Out of scope

Print golden-file completion, micro-help pages, timed §9A.4 CI tests (remain spec 0010).

## Risks / open questions

None — services are test-proven; this is presentation + wiring.
