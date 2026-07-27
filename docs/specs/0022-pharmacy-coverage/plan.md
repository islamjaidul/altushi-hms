# 0022 — Plan

## Approved: 2026-07-27

| # | Gap | Fix | Proof |
|---|---|---|---|
| 1 | 8 of 17 matrix rows never exercised | `eng/verify/pharmacy-full.py` — one section per row, self-provisioning, repeat-run safe, wired into the upgrade gate | the script itself (41 checks) |
| 2 | Staff tag lost when no discount | `PharmacySale.SaveAsync` takes the flag and writes a tier-2 `pharmacy.sale.staff` audit event inside the sale's own transaction | script row 17 asserts the tag after a full-price staff sale |
| 3 | Audit search 500s on jsonb | the search becomes a `FromSql` predicate with `after::text ILIKE`, composable with the action filter; still SQL-side | `spec-0022.spec.ts` searches four terms and asserts narrowing |

## Note on the tag's home

The tag belongs on the audit stream rather than a new column: it is a fact about an event,
the stream is already append-only and attributable (hard rule 4), and the audit viewer is
where an accountant would look. A `staff_sale` column would need a migration and a second
write path to keep in step — and P17 may yet change what a staff sale *means*.
