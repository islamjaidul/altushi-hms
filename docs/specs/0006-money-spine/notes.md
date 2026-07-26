# 0006 — Notes
- 2026-07-26 — Domain core + all [M] money requirements shipped and test-proven. The [S] screens
  (registration doc+preview, OPD POS) and UHID card PDF are deferred to the cross-sprint UI pass
  recorded in spec 0005 notes — the S1 shell, templates and JS contracts exist; screens are
  pattern-fill. Golden-thread AC 2 is therefore met at service level (register → encounter →
  invoice → pay → due asserted in MoneySpineTests), not yet at keyboard level.
- 2026-07-26 — S2 catalog shortcut: charge lines carry `catalog_kind='service', catalog_id=0` with
  caller-supplied price until S3's rate-version resolution lands (03 §5). RateVersionId is already
  stored on both charge and invoice lines.
