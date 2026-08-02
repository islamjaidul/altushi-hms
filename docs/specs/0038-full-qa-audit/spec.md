# 0038 — Full product QA audit, module by module and route by route

- **Status:** Done
- **Date:** 2026-08-02
- **PRD ref:** §5 (all built modules), §5A, §7, §11, §12, §16
- **MVP:** in scope — verification of shipped work, no new product scope

## Problem

Fifteen of the twenty-two PRD §5 modules carry code, across two SKUs. The product is
about to be handed to a senior engineer to harden, and the honest state of the evidence is
uneven in a way no single document admits:

1. **`docs/qa/patient-lifecycle.md` reads 93% covered, and says of itself that the figure
   measures the document, not the product.** Spec 0032 proved the register wrong in *both*
   directions — five false gaps and five false coverages.
2. **M16 HR & Payroll has no arithmetic test at all.** `docs/qa/module-coverage.md` calls it
   "the single largest known risk in the product". Spec 0037 covered the state machine and
   the durability of every write; the money arithmetic inside those writes is untested.
   `grep` over `tests/` for `PayrollService|SalaryStructure` returns nothing.
3. **The audits so far were organised by spec, by journey, and by module — never by
   *route and handler*.** There are 82 routes and 141 POST handlers. No pass has asked, for
   every handler, what it does with input a real operator can type.
4. **Validation has no declarative layer.** Zero DataAnnotations, one `ModelState` use,
   ~290 bare `[BindProperty]` binds. Whether malformed input is refused or reaches the
   service layer as a 500 is unknown per-handler and unknowable by reading.
5. **The two hosts' QA is asymmetric.** `hrm-thread.py` (37 cases) is in no tier of
   `lifecycle-suite.py` and in no traceability check — nothing runs it automatically.

A code read during planning already surfaced two arithmetic defects by inspection
(`PayrollService.cs:258` integer-divides the OT minute rate to zero below ~14,400 Tk basic;
`WorkingDaysAsync` at `:616` accepts `branchId` and never uses it). Defects visible from a
half-hour read, in money code, are evidence the surface has not been examined.

## Requirements

- [M] An inventory of all 22 PRD §5 modules with build state, so the report's scope is
      explicit and the seven unbuilt modules are recorded as sequencing, not as gaps.
- [M] For every **built** module, a route-and-handler level audit covering forms, input
      validation, CRUD, state transitions, authorization at the handler, and report/print
      surfaces.
- [M] A PRD cross-check per built module: every `[M]` sub-feature of §5/§5A mapped to the
      screen that implements it and the test that asserts it, with an explicit verdict.
- [M] Business-logic probes for **M16 payroll arithmetic**, the largest known risk: split-period
      proration, rounding residue, tax-slab boundaries, overtime, late-grace, provident-fund
      eligibility, arrears against a locked period, negative-net floor, night-shift punch
      pairing, punch-import idempotency.
- [M] A patient-perspective walk from registration to discharge that asks, at each step, what
      the patient receives and whether the money reconciles.
- [M] Every reported finding carries a **reproduction that runs from the repo** — a committed
      script, not a transcript. A finding the engineer cannot reproduce is not a finding.
- [M] A severity-ranked handoff report in `docs/qa/`, naming the surface (route + `file:line`)
      and the evidence, with **no fix proposals** — remediation is the engineer's call.
- [M] An explicit "what was not tested" section. Sampled coverage stated as sampled.
- [S] Re-verification of the defects left open deliberately (M4-F3, M3-R1, CONC-1, CONC-4)
      with a dated verdict each.

## Non-goals

- **Fixing anything.** This campaign reports. A fix inside the audit would invalidate the
  baseline it was measured against.
- Load testing, browser-matrix, accessibility and penetration testing beyond the named
  public surfaces. LC-XCUT-11 stays open pending ADR-0024.
- The seven unbuilt modules (M12–M15, M17–M19).

## Acceptance criteria

**AC1** — Every built module has a route table in the report where each route has a verdict
and each POST handler is accounted for.
**AC2** — Every finding has an `AUD-<area>-<nn>` id, a severity, a surface with `file:line`,
and a repro command that was executed once from a clean shell before it entered the report.
**AC3** — The M16 arithmetic probes exist as committed scripts and are re-runnable, closing
the "no business-logic test whatsoever" verdict in `docs/qa/module-coverage.md` — whatever
they find.
**AC4** — The audit ran against a **freshly seeded local database** on current `main`, with
both hosts booted from source; no mutating run touched a deployment.
**AC5** — The report distinguishes *defect* (product is wrong) from *gap* (product is
untested) from *absence* (product was never built), and never silently promotes one to another.

## Notes

Local-only by decision: the VM's ERP image predates 2026-07-29 and mutating QA against a
deployment writes records that hard rule 4 forbids deleting.
