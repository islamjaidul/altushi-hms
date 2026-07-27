# 0022 — Tasks

- [x] Audit spec 0016's 17 matrix rows against existing coverage; 8 found untested
- [x] `eng/verify/pharmacy-full.py` (41 checks across all 17 rows), wired into the upgrade gate
- [x] Staff-sale tag recorded as a tier-2 audit fact, discount or not
- [x] Audit-viewer search fixed (`after::text ILIKE`, predicate still in SQL)
- [x] `spec-0022.spec.ts` pins both fixes at the UI
- [x] Full verification + deploy + live re-run
