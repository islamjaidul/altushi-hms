# 0049 — Notes

- 2026-08-04 — Spec created with plan pre-approved (demo-day burst 0046–0050).
- The rewrite immediately proved its worth: the first `grant-drift` run against the local
  deployment showed the Admin role holding **50** permissions where the code grants 5 —
  months of ad-hoc per-cell clicks on the dev database. `grant-drift --fix` revoked the 45
  extras (audited), and the checkbox matrix + one-save diff makes that class of creep visible
  and reversible in one screen.
- `_harness.grant_cells` now parses the checkbox markup (one tag per cell); grant-drift and
  LC-ROLE-14 unchanged in behaviour. The single-cell `OnPostPermissionAsync` handler is kept
  verbatim as their API contract.
