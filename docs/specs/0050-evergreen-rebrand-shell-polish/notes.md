# 0050 — Notes

- 2026-08-04 — Spec created with plan pre-approved (demo-day burst 0046–0050).
- Deviations from plan, all narrowing: no `Org__*` compose env was needed — the host default
  in `Program.cs` IS the product brand now, and config override capability remains for future
  customers. `number_series.display_format` was left as-is (written once, never re-read — the
  passed constant decides the format). The logo assets ship as `LogoMark`/`LogoLockup` on
  `OrgIdentity` (null = monogram), so the HRM SKU stays logo-free per P27; the mark doubles
  as the favicon (no third asset).
- The HRM SKU's login page inherits the new evergreen backdrop (shared stylesheet). Accepted
  for now — the palette is tokenized (`--ev-*`), so a future HRM identity is a token swap.
- Side effects fixed en route: the icon subset was missing three glyphs used by 0043–0045
  screens (rebuilt, 63 glyphs); `.btn-sm` (used by /admin/people since 0043) was undefined;
  `golden-thread` now acknowledges critical values at verification (0044 changed the release
  contract and the thread was never updated — its fixture "1" values sit in critical bands).
- Known cosmetic remainder: historical ALT- UHIDs stay valid alongside new SEH- ones (by
  design); `golden-thread`'s exact "৳ 550 income" assertion only holds on a fresh database
  (pre-existing limitation, unchanged).
