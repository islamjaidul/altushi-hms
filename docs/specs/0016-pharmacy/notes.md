# 0016 — Notes

- **The upgrade gate paid for itself on its first module.** DevSeed's counter block was gated
  on "any counters exist", so a pre-0016 database never gained the Pharmacy Counter — every
  fresh-DB run was green while the upgraded path was broken. Caught by `eng/verify/upgrade/run.sh`
  (extended with the pharmacy smoke + thread), fixed by making that seed additive per kind.
  This is the reference-band failure shape again, one release later — the gate now guards it.
- **Walk-in retail** uses a standing `ALT-WALKIN` patient (`UnknownIdentity = true` — the reg
  `ck_identity` CHECK rejects an identity-less row otherwise, which the first live sale found).
  Credit and discounts require a named patient; anonymous dues are refused by design.
- **One charge line per batch allocation**, so a receipt prints the exact batch and price it
  sold from, and refunds restock precisely. A refund/cancel restocks *all* unrefunded
  allocations (goods-return semantics); partial-quantity returns are deferred until a real
  workflow needs them — recorded here, not silently absorbed.
- **Deferred with reasons (matrix rows 4, 11, 17-part):** indoor issue → folio and
  discharge-time return await M6 (Wave 2); the indent screen will consume
  `StockService.AllocateFefoAsync`/`IssueAsync` unchanged. Staff-pharmacy *pricing* awaits
  P17 — staff sales ship as tagged, approval-gated discounts meanwhile.
- MD sees pharmacy income via the money spine with zero new plumbing (dept split reads charge
  lines; `medicine` catalog kind groups under "Other" until a dept mapping is added — small
  follow-up for the M22 dept table).
- One Playwright U4 flake observed once under parallel workers (registration name collision
  suspected); passed in isolation and on the next two full runs.
