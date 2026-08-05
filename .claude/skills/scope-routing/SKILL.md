---
name: scope-routing
description: Decide whether a proposed feature is ours to build, which PRD requirement it serves, and where it goes if it serves none. Use before building, designing, or estimating any feature, when someone proposes a module, screen or capability, and whenever a request arrives in operator language rather than as a requirement.
---

# Scope routing

**The product is the full 22-module HMS ERP of PRD §5/§5A — production-grade, not a pilot.**

The §9A.2 MVP freeze was lifted by the PM on **2026-07-27** (`docs/architect_review_prompt.md`).
Nothing is "deferred to after the MVP" any more. Anything in §5 is ours to build; the only
questions are *which requirement does this serve* and *when in the sequence*.

Sequencing lives in `docs/architecture/11-build-plan-phase2.md`, not in this skill — read it for
wave order, migration risk and what is buildable versus merely validatable.

## The routing question is not "is it in scope" — it is "which of these four is it"

| The request… | Route |
|---|---|
| **maps to a §5/§5A requirement** | Build it. Cite the module and user story (`§5 M6 US6.3`). `prd-lookup` finds it. |
| **is a defect in something shipped** | Build it. A defect needs no new requirement — but it still needs a spec (`spec-flow`), and the spec states which AC was violated. |
| **is genuinely new scope** — no §5 requirement covers it | Do **not** build it. Append to `docs/architecture/09-questions-for-pm.md` with your reasoning and a recommended default, then continue with requirement-backed work. |
| **is a technology choice** dressed as a feature | Not a scope question. `adr-write`. |

The third row is the one that matters. **Never invent a requirement.** "Obviously the hospital
needs X" is how a product acquires features nobody asked for and everybody maintains. If §5 does
not say it, the PM decides — and the PM answers, so asking is cheap.

## What "production-grade" changes about the decision

Under the old freeze the bar was *does the demo survive*. It is now *does the hospital run on this
on a Tuesday when the internet is down and the operator is tired*. That raises the floor on work
already in scope, and the raise is not scope creep:

- A screen that creates a record must be able to **read, correct and retire** it
  (`crud-completeness`). Spec 0045 is the precedent — US1.4 shipped a create path with no edit
  path, so an unconscious patient registered without a name could never be given one.
- A write that reports success must actually **be durable** (spec 0037 — six HR screens showed a
  success toast and wrote nothing).
- A business event crossing a module boundary must **land on the other side**
  (`cross-module-flow`).
- A list or search a real operator uses must stay fast at §14 volumes on the §16 box
  (`schema-and-indexing`).

None of these are new requirements. They are what §5's existing requirements mean once the product
is real. Do not route them to the PM.

## Two override rules that still bind

- **Design-for rule (PRD §9, binding).** Structures a later module needs must be accommodated in
  the data model *now*, even when that module is not this wave's work. Retrofitting a folio under
  live billing is the known competitor failure mode. Designing for something ≠ building it.
- **Money-model rule.** BEFTN payouts, TDS and VAT (§3.4) are not this wave's features, but the
  money model must not make them painful later (§16 Q13–Q14).

## Anti-patterns

- **Silently expanding scope** because a feature is "small" or "standard". Small features carry
  full maintenance cost.
- **Cutting scope unilaterally.** Recommending a cut to the PM is welcome; making one silently is
  not.
- **Building a demo-only path.** Anything that works only when a presenter drives the keyboard is a
  defect (§9A.4) — and there is no demo deadline left to excuse it.
- **Treating "the MVP didn't have this" as an argument.** There is no MVP. Check §5.
- **Quoting spec 0002 or any pre-2026-07-27 spec as current scope.** Specs are append-only history;
  they record what was true when written.
