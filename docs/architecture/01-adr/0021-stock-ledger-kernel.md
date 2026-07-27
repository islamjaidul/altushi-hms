# 0021 — Stock ledger kernel: append-only moves, FEFO issue, bill-spine sales

- **Status:** Accepted
- **Date:** 2026-07-27
- **Answers:** PRD §5 M11/M12 stock discipline; §11 Stock Batch / Stock Audit / Purchase Order machines; 11-build-plan §4
- **Spec:** `docs/specs/0016-pharmacy/`

## Context

Four modules keep stock (M11 pharmacy, M12 three stores, M13 blood units, M14 canteen), and
the PRD demands the same discipline everywhere: batch/expiry identity, no silent quantity
edits, attributable movements, approval-gated adjustments, and counters that keep selling
through outages. M11 lands first (§9A.3) and therefore sets the pattern the other three
inherit. Money-side, a pharmacy sale must be exactly as safe as an OPD invoice — gap-free
numbered, due-tracked, refundable, day-closed, on the MD dashboard.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Quantity column mutated in place, history in audit | Simple reads | Silent-edit risk; §11 Stock Audit becomes unenforceable; variance reports need reconstruction |
| Event-sourced ledger only, quantity derived | Purest | Every sale pays a SUM over history; hot-path cost on a 2 vCPU box |
| **Append-only `stock_move` + materialised `qty_on_hand` under row lock (chosen)** | Ledger is the truth for history, the locked quantity is the truth for "can I sell this now"; both cheap | The two must move in one transaction — enforced by pattern and test |
| Separate pharmacy invoicing stack | "Pharmacy is different" | Rebuilds numbering/dues/refund/day-close; splinters the single billing spine (§2.3 D2 differentiator) |

## Decision

1. **Schema `pharm`, owned by `Hms.Pharmacy`.** M12 generalises the same table shapes to its
   stores when it lands (build plan Wave 4); blood (M13) and canteen (M14) reuse the pattern,
   not the tables — a blood unit is a batch of one.
2. **`stock_move` is append-only** (no UPDATE/DELETE grant): every receive, sale, return,
   transfer, damage, write-off and audit adjustment is a signed, attributed row referencing
   its document. The periodical stock ledger (§5 M11 [M]) is a query over it.
3. **`batch.qty_on_hand` is materialised**, mutated only under `SELECT … FOR UPDATE` with a
   `qty_on_hand >= 0` CHECK — the ADR-0015 due-row pattern applied to stock. Concurrent sales
   of the last strip serialize; the loser gets a comprehensible "batch just sold out, pick
   again" rather than negative stock.
4. **FEFO**: issue picks `state = InStock` batches ordered by earliest expiry; **expired and
   quarantined batches are excluded by predicate**, so US11.1's "physically block" is a query
   property, not a UI habit. Near-expiry is a flag window (config, default 90 days), warned
   at POS and reported.
5. **Sales ride the bill spine.** A pharmacy sale opens/reuses a same-day counter-sale
   encounter (`kind = "pharmacy"`) and posts charge lines (`CatalogKind = "medicine"`,
   `CatalogId = product`) through `BillingService` — the `ck_charge_parent` XOR is untouched,
   and numbering, multi-tender, dues, refund approval, day-close and the MD dashboard need no
   pharmacy-specific code. `sale_allocation` pins each line to its batches (qty, MRP, cost) so
   refunds restock the exact batches and profit is computable.
6. **Price source is batch MRP**, not the effective-dated service rate plan. Hard rule 5 holds
   by snapshot: the charge line records the charged price forever, and the allocation records
   which batch (and cost) backed it. MRP lives on the batch and is immutable after receipt;
   a re-price is a new receipt, mirroring the rate-version discipline.
7. **§11 machines**: PO Requested → Approved⚿ → Ordered → Partially Received → Received →
   Closed (Cancelled exit); Batch In Stock → Near-Expiry(flag) → Expired(quarantined) →
   Returned/Disposed; Stock Audit Count Started → Variance Listed → Adjustment Approved⚿ →
   Posted. ⚿ transitions go through the existing approval engine; adjustments post moves,
   never edit quantities directly.

## Consequences

- One stock discipline for four future modules; M6 ward indents become a consumer of
  `StockService.IssueAsync` rather than new stock code.
- Pharmacy money inherits every invariant the MVP proved, including the concurrency tests.
- Cost accepted: bill + pharm contexts commit in one `HmsTx` transaction (G19 already
  guarantees the mechanics); the move+quantity pairing is enforced by integration tests.

## Reversal trigger

If M12's three stores need semantics `pharm`'s shapes cannot host (serialised assets,
recipe conversion), M12 gets its own schema copying the pattern — the kernel is the
discipline, not a shared table.
