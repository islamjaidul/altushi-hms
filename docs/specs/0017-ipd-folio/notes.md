# 0017 — Notes (afterwards)

## What went to plan

The seam held exactly as the review predicted (10-mvp-review.md §5): folio-parented charge
lines needed **zero** change to `ck_charge_parent` or `PostChargeAsync`'s callers. The first
test posted a folio line through the spine and passed on first run — the MVP's structural bet
paid off in full.

## Deviations & decisions made in flight

- **Invoice and receipt gained folio parents** (`ck_invoice_parent`, `ck_receipt_parent` XOR,
  mirroring the charge-line pattern) instead of inventing a parallel settlement document.
  A folio-parented receipt *is* an advance — it rides counter sessions, so tender totals and
  expected cash reconciled with no day-close special-casing beyond the new `advances_taken`
  figure. Proven by the excess-advance day-close test (variance 0).
- **Advance excess at settlement returns as a negative folio receipt**, approval-free by
  design (it is settlement's change-giving, and it is audited). Refunds of *invoice* money
  remain approval-gated as before.
- **Investigation indents are direct orders**, not a two-step requisition: `diag.test_order`
  gained a folio parent (XOR) and indoor orders are born `In-Progress` (§11's indoor branch —
  no invoice gate), raising samples in the same transaction. LIS flow untouched.
- **Late-post approvals are per-folio, not per-line**: one approved `folio-late-post` request
  unlocks posting on that folio until settled again. Good enough for the §12 requirement;
  a per-line consume model can supersede later if audit demands it.
- **Block state restores its origin**: `blocked_from` on the admission remembers whether the
  R4 hold interrupted `admitted` or the discharge flow, so Released⚿ returns the patient to
  the right §11 position.

## Surprises / lessons

- **The icon-font subset bit us.** Spec 0012 vendored a 64-glyph Material subset with no
  archived generation script; the new `bed`/`build`/`hourglass_top` etc. glyph names rendered
  as fallback text and failed 24 smoke tests ("looks like spelled-out fallback"). Fixed by
  reusing proven glyphs only. **Follow-up:** archive a subset-regeneration script (fonttools)
  so a future module can add glyphs instead of scavenging.
- Npgsql writes only UTC `DateTimeOffset` — Dhaka day boundaries must `.ToUniversalTime()`
  before hitting a `timestamptz` parameter (bit the IPD reports page once).
- Playwright counts `<option>` elements in a closed `<select>` as *hidden* — assert presence
  (`count() > 0`), never visibility, for dropdown contents.
- A "failed" golden-thread dashboard check turned out to be **pre-dirtied state** (an aborted
  Playwright global-setup had touched the day's figures), not a regression — re-ran on a
  virgin database and it passed. Same lesson as 0016's stale-app incident: check the harness
  before suspecting the code.

## Verification record

91→103 .NET tests (10 new IPD) · ipd-thread.py green on fresh and dirty DBs ·
golden/discount/pharmacy threads green · Playwright 166 green (7 new routes, 3 new denied
pairs, 8 spec tests, nasrin in the cast) · upgrade gate green over the pre-pharmacy fixture
with the bill/diag/ipd migrations and the ipd-thread now part of the gate · 4 CI greps OK.

## Follow-ups

- Icon-subset regeneration script (above).
- P18 answer may adjust the bed-day rule; the charging code is isolated in
  `FolioService.ComputeUnpostedBedDaysAsync` + `IpdBilling.CatchUpBedDaysAsync`.
- M5 nursing charts, M7 OT postings and M14 canteen all consume `PostFolioChargeAsync` —
  no further spine work expected.
- Wave-2 remainder: M2 Front Desk (spec 0018) and R3 public displays (spec 0019).
