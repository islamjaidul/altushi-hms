# 0021 — Money stranded on terminal exits, and double-billed invoices

- **Status:** Done
- **Date:** 2026-07-27
- **PRD ref:** §11 (Admission: Death / Absconded exits, "due follow-up"), §5 M6 US6.2,
  §3.2 (dues are normal), §7 U6/U7 (non-technical operators), hard rule 4 (attributable money)
- **MVP:** post-MVP — corrective work inside the modules delivered by specs 0016–0020

## Problem

Advanced edge-case probing of the patient lifecycle found three defects that the happy-path
threads cannot reach.

1. **A patient who dies leaves their bill permanently unbillable.** `Death` is a terminal exit
   that bypasses `Clinically Cleared`, and settlement refuses anything else — so the folio
   stays Open with its charges forever. No invoice, no due, no statement: the hospital cannot
   bill the family even when it is entitled to, and the revenue is invisible to every report.
2. **An absconded patient leaves nothing to follow up.** §11 names the state
   "Absconded(due follow-up)", but with no settlement there is no invoice and therefore no due
   row — the dues screen shows nothing and the money silently disappears from the books.
3. **A re-submitted bill bills the patient twice.** A double-click, a slow network, or the
   browser's "resend" on refresh posts the save twice and the second post creates a **second
   invoice** with a second set of charge lines. Observed on OPD billing; the same shape exists
   on diagnostics ordering, the pharmacy POS and IPD settlement. §7's operators are
   explicitly non-technical and fast-clicking; this is their most likely mistake, and it
   produces a duplicate financial document that hard rule 4 then forbids anyone to delete.

## Requirements

- [M] A folio must be closable on **every** exit from §11's admission machine — discharge,
  death and absconding alike. Closing the bill of a patient who died or absconded must not
  claim they were "discharged"; the clinical exit stands, only the money is settled.
- [M] An absconded (or deceased) patient's unpaid balance must appear as a normal **due**, so
  the existing collection and follow-up path works on it — no new chase mechanism.
- [M] **One submission creates at most one invoice**, on every screen that issues one
  (OPD billing, diagnostics order, pharmacy POS, IPD settlement). A repeated submission of
  the same prepared bill must land the operator on the invoice that already exists, not make
  a new one. Enforcement must survive two concurrent requests, not merely a slow double-click.
- [M] No financial document is deleted to achieve any of this (hard rule 4).

## Acceptance criteria

1. A deceased patient's folio can be prepared and settled; the invoice and its due exist; the
   admission's state remains `Death` (never `Financially Settled`/`Discharged`), and the
   death certificate still issues.
2. An absconded patient's folio settles the same way, and the resulting balance is visible and
   collectable on `/billing/dues` like any other due.
3. Re-posting a saved bill (same page, same prepared cart) yields **the same invoice number**
   and no extra charge lines, on all four issuing screens. Two concurrent posts of one
   submission produce exactly one invoice — proven by a database constraint, not by timing.
4. The gate-pass/discharge path for a normally discharged patient is unchanged, including
   spec 0020's outstanding-due guard.
5. Tests at three levels + the end-to-end thread; the upgrade gate passes with the new
   migration.

## Out of scope

- Reversing an already-settled folio when its invoice is refunded (refund works; the folio
  stays locked). Whether a refund should reopen a folio is a business question — raised as
  **P21** with the recommended default "no: a refund is a reversal on the invoice, and a
  correcting charge is a new posting".
- A general idempotency layer for every POST in the product. The scope here is the four
  screens that create financial documents, where the damage is real and permanent.

## Risks / open questions

- **P21** (above).
- The submission token is a new column on `bill.invoice` with a unique partial index; existing
  rows carry NULL and are unaffected (additive, upgrade-gate verified).
