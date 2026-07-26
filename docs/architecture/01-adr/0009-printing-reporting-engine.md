# 0009 — Printing & reporting engine (Q5)

- **Status:** Accepted
- **Note:** Bangla-shaping spike is a named verification task
- **Spike outcome (2026-07-26, spec 0005 T10):** on-screen shaping **passed** with QuestPDF 2026.7.1 (Skia/HarfBuzz) + self-hosted Noto Sans Bengali — conjuncts/matras/reph verified on the emitted artifact (`eng/spike-artifacts/bangla-sample.pdf`); QuestPDF selected as the server-side engine; Chromium fallback not invoked. Printed-sample sign-off and licence-tier confirmation pending (spec 0005 notes).
- **Date:** 2026-07-26
- **Answers:** PRD §16 Q5 (§8 N8, §7 U10)

## Context

Every printable — thermal 58/80 mm receipts, A4 lab reports/statements, barcode labels, ID cards — must be **pixel-faithful** (N8: report appearance is hospital brand identity), carry per-hospital letterhead, work with **no printer** via an identical on-screen PDF (demo edge 2), and render customer-entered **Bangla** text correctly (edge 9, Q10). The design reference mandates one letterhead system across all documents.

## Options considered

| Option | Pros | Cons | RAM cost |
|---|---|---|---|
| **HTML/CSS print views + server-side PDF renderer (chosen)** | One layout source per document (Razor partial) drives screen preview, browser print, and archived PDF; letterhead is a shared partial; golden-file tests pin fidelity | Two render paths (browser print vs. PDF) must be kept visually equal — golden-file tests exist for exactly this | renderer in-process |
| Headless Chromium per request | HTML fidelity guaranteed | ~150–300 MB per instance (estimate) — unaffordable resident on 3 GB; cold-start latency at counters | prohibitive |
| Desktop report designer suite (Crystal-class) | Familiar to some markets | Licence cost, Windows coupling, conflicts with Linux containers | n/a |

## Decision

- **Layout source of truth:** one Razor view per document type (money receipt, invoice, lab report, delivery slip, ID card, day-close statement…), composed from the shared letterhead/identity partials (hospital settings applied everywhere — matches the design reference's letterhead system).
- **Counter printing:** browser print pipeline with per-document `@page` CSS (58/80 mm roll widths, A4), silent-print via kiosk/print-profile configuration on counter PCs; every print action has an on-screen preview that *is* the same view (edge 2, 10).
- **Archival + fallback PDF:** server-side renderer producing the PDF from the same HTML. Engine candidates: a maintained .NET HTML-to-PDF library, or a document-composition library (QuestPDF-class) with the layout partials mirrored. **Selection is gated on the spike:** correct **Bengali script shaping** (conjuncts, matras) and embedding of the bundled Bangla font in output PDFs, verified with printed samples — no engine is asserted capable until the spike passes (project rule: no unverified library claims). Licence terms verified at selection time.
- **Barcodes:** Code 128 generated server-side as SVG (self-hosted library, verified at selection), embedded in labels/cards; ID-card and sample-label layouts sized to the printers in §13 I2/I3.
- **Fidelity regression:** golden-file rendering tests (per layout, per paper size) run in CI; a layout change fails the build until the golden is intentionally updated.

## Consequences

- One place to edit a document; previews never diverge from prints (edge 10: hospital staff drive it without presenter knowledge).
- Cost accepted: browser-print silent-printing needs a documented per-counter setup step (printer profile), captured in the deployment runbook.

## Reversal trigger

The Bangla-shaping spike failing on all candidate .NET-side renderers → fall back to a **pooled single** headless-Chromium worker (one instance, queued jobs, hard memory cap) used *only* for PDF generation, with its RAM charged explicitly in the `06-deployment.md` budget (something else gives way).
