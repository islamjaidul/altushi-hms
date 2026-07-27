# 0016 — M11 Pharmacy (outdoor core + multi-outlet stock spine)

- **Status:** Done
- **Date:** 2026-07-27
- **PRD ref:** §5 M11, §5A-11, §11 (Purchase Order / Stock Batch / Stock Audit), §12 (Pharmacist), §7
- **MVP:** post-MVP — Wave 1 of `11-build-plan-phase2.md`; §9A.3 names Pharmacy first after the MVP

## Problem

Pharmacy is the cash engine §9A.3 deferred only because no stock existed yet. The hospital
cannot sell a single tablet: no product master, no batch/expiry stock, no pharmacy POS, no
supplier ledger. Every taka of medicine revenue is invisible to the money spine the MVP built.

## Requirements

The PRD-to-screen traceability matrix is in `plan.md` (DoD rule 1: matrix first). Summary:

- [M] Company & product registration (brand + generic + strength + form + unit; MRP & cost per batch)
- [M] Purchase order → stock receive (batch, expiry, qty, cost) → purchase return
- [M] Outdoor retail sale: brand/generic search, **earliest-expiry-first auto-pick, expired
  batches blocked** (US11.1), receipt through the money spine
- [M] Sales return & company (supplier) return with ledger effects
- [M] Expiry management: near-expiry report, quarantine, sale-block
- [M] Auto reorder shortlist (reorder level / stock / sales velocity — US11.3)
- [M] Supplier ledger & payment; customer dues ride the existing bill spine
- [M] Sales & purchase statements; periodical stock ledger; **stock audit with approval-gated
  adjustment** (§11)
- [S] Pharmacy dashboard (earnings, stock value, short items — US11.4)
- 5A-11: outlets master, transfer indent → transfer → outlet ledger, damage management,
  expired-medicine management, supplier replacement, staff-pharmacy sale tagging

## Acceptance criteria

1. **US11.1 AC:** an expired batch cannot be added to a sale (server-refused, control absent);
   near-expiry batches warn visibly at the POS.
2. A sale allocates FEFO across batches, decrements stock and writes the invoice + receipt +
   audit in one transaction; concurrent sales of the last strip serialize — stock never goes
   negative (DB CHECK + row lock, ADR-0015 pattern).
3. PO walks §11: Requested → Approved⚿ → Ordered → Partially Received → Received → Closed,
   with Cancelled reachable; receiving creates batches with expiry and cost.
4. Stock audit walks §11: Count Started → Variance Listed → Adjustment Approved⚿ → Posted;
   the adjustment writes an attributable stock move, never edits a batch in place.
5. Batch lifecycle per §11: In Stock → Near-Expiry(flag) → Expired(quarantined) → Returned to
   supplier / Disposed(logged) — every state reachable and leavable in the UI.
6. Pharmacy income lands on the MD dashboard and in day-close through the bill spine with no
   pharmacy-specific plumbing; refunds of pharmacy invoices restock via the sale's batch
   allocations.
7. §12: a Pharmacist can sell, receive and manage stock but cannot reach admin or LIS; every
   pharmacy route is permission-gated server-side with the nav composed from the same grants.
8. Tests at three levels: stock-service integration tests (FEFO, expiry block, concurrency,
   audit), Playwright screen tests, and an end-to-end `pharmacy-thread.py`; the upgrade gate
   passes with the new migrations.

## Out of scope (explicit deferrals — reasons in the matrix)

- **Indoor issue to folio + ward requisition (US11.2)** and **discharge-time return** — the
  M6 folio does not exist yet (Wave 2). The stock spine is built so M6's indent screen is a
  consumer, not a rework.
- **Barcode sale entry** — the barcode-wedge capability of 05 §3 is tracked MVP debt; typing
  2–3 letters (§7 U5) is the built path today.

## Risks / open questions

- Medicine prices are **batch MRP**, not the effective-dated service rate plan; historical
  reproduction holds because the charge line snapshots price and the sale allocation pins the
  batch (hard rule 5 satisfied differently — recorded in ADR-0021).
- Staff-pharmacy pricing policy is a PM question (added as P17); until answered, staff sales
  are tagged and discounted through the existing approval-gated discount flow.
