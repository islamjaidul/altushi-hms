# 0014 — Bangla text path for customer-entered content (Q10)

- **Status:** Accepted
- **Note:** rendering-proof spike shared with ADR-0009
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q10 (edge case 9; C3)

## Context

Operator UI is English-only (C3), but customer-entered text — SMS bodies, report footers/headers, hospital identity strings — may be Bangla, and must survive end-to-end: entry → storage → screen → printed page → PDF (edge 9: "Can it show Bangla?" is a live demo question). Q10 asks us to confirm this costs ~nothing architecturally.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| **UTF-8 everywhere + self-hosted Bengali font (chosen)** | Storage/transport are free (UTF-8 is already the default in Postgres/.NET/HTML); rendering cost is one bundled font + shaping verification | PDF-side complex-script shaping must be *proven*, not assumed |
| Restrict to ASCII in MVP | Zero work | Fails edge 9 in the demo room; retrofit is invasive |

## Decision

- **Storage/transport:** UTF-8 end-to-end (Postgres `UTF8` encoding, .NET strings, `charset=utf-8` responses). No transliteration, no legacy encodings.
- **Screen:** ship a self-hosted Bengali font (Noto Sans Bengali-class, licence verified at selection; the design reference already bundles a Bengali-capable woff2) in the app image — no external font fetch (edge 1). Browsers handle Bengali shaping natively.
- **Print/PDF:** the same font is embedded in PDFs; conjunct/matra shaping in the PDF renderer is **the acceptance test of the ADR-0009 spike** — printed samples of a Bangla report footer and SMS preview are the pass artifact.
- **SMS:** Bangla SMS is Unicode (UCS-2) class — 70 chars/segment vs 160 for GSM-7. The composer shows a **live segment counter and cost hint** per body (template screen), because segment costs are a real customer concern; gateway charset passthrough verified against the chosen aggregator (I7) during integration.
- **Inputs:** free-text fields accept any Unicode; validation never assumes Latin (name fields, footers, addresses).

## Consequences

- Confirmed: architecturally near-zero cost — one bundled font, one spike, one segment counter.
- Cost accepted: the PDF spike is on the critical path for the demo (edge 9 is a scripted demo moment).

## Reversal trigger

None foreseeable for UTF-8 itself; only the PDF engine choice can be forced to change by the spike (see ADR-0009's reversal path).
