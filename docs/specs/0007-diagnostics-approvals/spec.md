# 0007 — S3: Diagnostics + approval engine

- **Status:** Done
- **Date:** 2026-07-26
- **PRD ref:** §9A.2 modules 5 & 6, §11 (TestOrder, Bill-request machines), §12 (approval workflow)
- **MVP:** in scope

## Problem

The demo's signature beat — discount above threshold → approval → supervisor decides → billing
continues — needs the single approval engine (C7) plus effective-dated rates (C6) and the
diagnostic order that creates unbilled charges for billing to invoice (the §9A.2 seam).

## Requirements

- [M] Approval engine (kernel, 04 §2.1): Raise → auto-approve under requester threshold (policy
      data) or Pending; Decide with audit; delegation windows (P4); one machine for all types.
- [M] Rate versions: `[valid_from, valid_to)` + GiST exclusion constraint (no overlap); resolution
      picks the version effective on the business day; resolved id stored on lines (C6).
- [M] Edge 13 test: changing a price never alters any stored invoice line.
- [M] Diagnostic order: tests + referrer + TAT promise → unbilled charge lines via IChargePoster;
      payment flips order to InProgress (label + worklist events via outbox).
- [S] SSE unbilled-charge channel; approvals inbox screen.

## Acceptance criteria

1. Discount above threshold produces Pending + inbox row; under threshold auto-approves — tests.
2. Exclusion constraint rejects overlapping rate versions; resolution honours effective dates.
3. Historical invoice lines byte-stable across a price change (stored resolution test).
4. TestOrderPaid outbox event emitted in the payment transaction.

## Out of scope
LIS processing (S4); refund flows beyond the approval plumbing (S4).

## Risks / open questions
None new — P4/P5 defaults from 09-questions apply.
