# 0000 — PRD authoring & competitor analysis (retroactive baseline)

- **Status:** Done
- **Date:** 2026-07-26
- **PRD ref:** whole document
- **MVP:** n/a — this produced the requirements, not a build

> **Retroactive record.** This spec was written *after* the work, when the spec archive was
> introduced under [0001](../0001-handoff-readiness/spec.md). It documents what was already
> delivered so the archive has no silent gap. It is not a pre-work specification and does not
> pretend to be one.

## Problem

No product requirements existed for the HMS ERP. Two competitor proposals to Sylhet Evergreen
Hospital were available, and later live demo access to both competitor products.

## What was delivered

1. **PRD v1.0** — `docs/project_manager.md`: 22 modules, 15 personas, ~70 user stories, module
   data flows, data dictionary, state machines, permission matrix, volumetrics, and 12 open
   questions for the architect. Grounded in the two written proposals plus Bangladesh industry
   research (DGHS licensing, Safe Blood Transfusion Act 2002, DHIS2, payment rails).
2. **PRD v1.1** — enriched by live analysis of both competitor systems: MEDISpa (authenticated,
   266-link menu crawl + form field extraction) and PrimeMIS (login gated by reCAPTCHA/Firebase;
   structure extracted from its compiled Angular bundle — 198 routes). Added §2.4 walkthrough,
   §3.4 Bangladesh financial rails (BEFTN/TDS/VAT), §5A with 21 module enrichments and 4 new
   sub-modules, extended state machines, and Q13–Q15.
3. **§9A MVP scope** — the frozen 8-module "Golden Thread" for the customer-locking demo.
4. **`docs/architect_prompt.md`** — the Principal Architect brief: constraints, the
   2 vCPU / 3 GB budget, 10 deliverables, 34 edge cases, definition of done.

## Acceptance criteria (verified at the time)

1. Every §5A addition carries an `[obs: MEDISpa]` / `[obs: PrimeMIS]` / `[obs: both]` source tag
   traceable to a captured artifact — **verified**: 37 tags, all backed by saved crawl output.
2. No technology decisions present in the PRD — **verified** by review; tech questions are
   deferred to §16 Q1–Q15.
3. Bangladesh regulatory claims cite published sources — **verified**, §17 references.

## Known gaps in this record

- The v1.0 planning file was overwritten by the v1.1 plan and is unrecoverable; only the
  v1.1 plan survives in `plan.md`.
- Raw crawl artifacts (competitor HTML/JS captures) lived in a session scratchpad outside the
  repo and were not committed — they contain third-party page content. The evidence *claims*
  in the PRD stand; the raw captures are not reproducible from this repo.

## Follow-ups

Tracked in [0001](../0001-handoff-readiness/spec.md) — git, enforcement, architecture scaffold.
