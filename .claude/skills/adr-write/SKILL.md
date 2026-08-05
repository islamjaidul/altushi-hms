---
name: adr-write
description: Write or update an Architecture Decision Record for the HMS ERP project in docs/architecture/01-adr/. Use when making, documenting, revisiting, or superseding a technical decision — stack, database, offline/sync, concurrency, printing, auth, deployment, or any PRD §16 Q1–Q15 answer.
---

# ADR authoring

ADRs live in `docs/architecture/01-adr/` as `NNNN-kebab-title.md` (`0001-`, `0002-`, …). One decision per file. Keep each under ~150 lines — an ADR is a decision record, not a design manual.

## Template

```markdown
# NNNN — <Decision in a short noun phrase>

- **Status:** Proposed | Accepted | Superseded by [NNNN](./NNNN-….md)
- **Date:** YYYY-MM-DD
- **Answers:** PRD §16 Q<n> (omit if not applicable)

## Context
The forces at play: the requirement (cite `§`), the constraint, what makes this non-obvious.

## Options considered
| Option | Pros | Cons | RAM cost |
|---|---|---|---|
Include the option you rejected and why — that's the value of an ADR.

## Decision
What we are doing, stated plainly.

## Consequences
What becomes easy, what becomes hard, what we accept as a cost.

## Reversal trigger
The specific signal that would make us revisit this.
```

## Project-specific requirements

- **Budget every decision against 2 vCPU / 3 GB RAM.** Any component that adds resident memory needs its cost stated and something given up to afford it. "It's standard practice" is not a justification on this box.
- **Cite the PRD** for the requirement being served (`§8 N2`, `§12`, `§9A.2`) rather than restating it.
- **State measurement honestly.** Mark estimates as estimates; if you benchmarked, say how. Never assert a library capability or version you haven't verified.
- **`Reversal trigger` is mandatory.** A decision with no reversal condition hasn't been thought through.
- Business/scope questions do **not** belong in ADRs — route them to the PM (see `scope-routing`).

## Required coverage

PRD §16 Q1–Q15 each need an ADR, plus: stack, database, offline/sync strategy, concurrency control, printing/reporting engine (thermal + A4 + PDF fallback), auth/session/2FA, multi-branch & tenancy readiness, module entitlement toggles, backup/DR.

Q13 (BEFTN batch export) and Q14 (TDS/VAT) are **not** MVP builds — write them as forward-looking ADRs so the money model stays future-proof.
