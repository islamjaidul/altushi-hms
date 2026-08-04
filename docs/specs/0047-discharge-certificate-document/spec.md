# 0047 — The discharge certificate that was never a document

- **Status:** Done
- **Date:** 2026-08-04
- **PRD ref:** §5 M6 (certificates `[M]`), §7 (print/preview pattern)
- **MVP:** in scope — completes a shipped `[M]` capability
- **Found by:** the owner, preparing tonight's demo ("not previewing and not editable")

## Problem

Certificates got a numbering-and-audit spine (sequential series, frozen body, counted
reprints) but never the document half: no page renders the frozen body, so **"preview" does
not exist** — Issue and Reprint both dump the operator back on the list with only a counter
changed. Worse, the issue form offers admissions the service will refuse (a discharge
certificate needs a settled admission), so the default demo path ends in a red error. And the
only operator-editable content is one "extra" line; the clinical summary is whatever the
discharge screen happened to record, uneditable and often blank.

## Requirements

- [M] Every issued certificate opens as a **printable document** (letterhead, patient and
  admission details, summary, signature block, certificate number) — the screen is the
  preview, per the product's established print pattern.
- [M] Issue and Reprint land on that document; reprints stay counted and audited.
- [M] Pre-issue, the operator can review and **edit the clinical summary** (and add a
  follow-up date); what is issued is exactly what was on screen, then frozen.
- [M] The issue form only offers admissions valid for the chosen certificate kind — the
  operator can no longer be led into a guaranteed error.

## Acceptance criteria

1. Issuing a discharge certificate for a settled admission lands on
   `/ipd/certificates/{id}` showing a complete printable sheet; the browser print preview
   matches other product documents (no sidebar/topbar).
2. Reprint from the list opens the same document and the audited print count increments.
3. The admission dropdown never offers a combination the service refuses (discharge →
   settled/discharged only; death certificate → death exits only).
4. An edited summary appears verbatim on the issued document; re-opening the document later
   shows the frozen content even if the admission's summary changes afterwards.

## Out of scope

- A certificate template designer (competitor feature, not in the PRD build).
- Post-issue amendment/supersede flow — a certificate stays frozen; corrections remain a
  reissue conversation for the PM.

## Risks / open questions

- Historical certificates issued before this spec have bodies with possibly-null summary;
  the document renders an explicit "not recorded" line rather than a blank.
