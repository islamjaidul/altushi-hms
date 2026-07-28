# 0032 — Module-by-module QA sweep

- **Status:** Done
- **Date:** 2026-07-28
- **PRD ref:** §5 (all built modules), §5A, §7, §11, §12
- **MVP:** in scope — this is verification of shipped work, not new scope

## Problem

Verification has been organised by **spec** — each spec proved the thing it built. Nobody has
walked the product **module by module** asking the plainer question: *for this module, is every
business rule it enforces actually asserted somewhere?*

`docs/qa/patient-lifecycle.md` is organised by patient **journey**, which is the right axis for
finding cross-module defects (spec 0020 proved that) but the wrong axis for finding a module's
own unexercised rules. A rule that no journey happens to traverse is invisible to it. The
coverage summary reads 85%, and that number is computed from the document's own case list —
it measures how much of the document is covered, not how much of the product is.

Two concrete symptoms already found before this spec was written:

1. `lifecycle-suite.py --tier all` is red **by construction**. The runner executes t0 → t1 → t2,
   but t2 (`golden-thread.py`) asserts absolute money totals and therefore requires a fresh
   ledger. The twelve mutating t1 scripts run first and dirty it. `docs/qa/README.md` documents
   `--tier all` as a supported run needing only a fresh database — a precondition the runner
   itself destroys. Proven locally: t2 alone on a fresh database is green; inside `--tier all`
   the dashboard check fails.
2. The gap register carries 26 open gaps that were triaged once, in one sitting, against the
   journey document — not against each module's actual rule surface.

A permanently-red "full" suite is worse than no full suite: it teaches the team that red is
normal.

## Requirements

- [M] A module inventory covering all 22 PRD §5 modules, each marked built / not built, so QA
      scope is explicit and the eight unbuilt modules are recorded as out of scope, not missed.
- [M] For each **built** module, a gap analysis on three axes, derived from the module's own
      code rather than from the journey document:
      **UI smoke** (does every screen load and render for the roles that own it),
      **e2e** (does the operator's real path through the module work over HTTP), and
      **business logic** (is every enforced rule — money, state machine, permission, invariant —
      asserted by a test that fails when the rule is removed).
- [M] Every gap found is either closed by a test, or recorded with a severity and a reason for
      staying open. No gap is closed by asserting it is fine.
- [M] Defects surfaced by the new tests are fixed, and the fix is proven by the test that found it.
- [M] `--tier all` becomes a run that can pass.
- [S] The lifecycle document and its gap register stay the single source of truth — new cases are
      appended there under the existing `LC-` ids, never kept in a parallel list.

## Acceptance criteria

1. `docs/qa/module-coverage.md` exists and lists all 22 modules with build status, and for each
   built module its three-axis verdict and the gaps found.
2. Every new gap that this sweep closes has a named, runnable assertion — a `tests/` fact, an
   `eng/verify/` script step, or a Playwright spec — and the assertion is cited in the lifecycle
   document row it closes.
3. `python3 eng/verify/lifecycle-suite.py --tier all` exits 0 against a freshly seeded database.
4. `dotnet test hms-erp.slnx` is green, and the count has grown by the tests this sweep adds.
5. `bash eng/check-lifecycle-traceability.sh --stats` passes — no case cites a script that does
   not exist, and the counts in the document match the tables.
6. Nothing is pushed or deployed. All work is local until the user says otherwise.

## Out of scope

- Modules M12–M19 (Inventory, Blood Bank, Canteen, Accounts, HR, Consultant Payment, Corporate
  Billing, Marketing) — no code exists; they are recorded in the inventory and skipped.
- LC-XCUT-11 (load and concurrency at §8 N1 volumes) — open by decision, pending ADR-0024.
- New product scope. A missing *requirement* goes to the PM (`09-questions-for-pm.md`); this
  spec adds tests for rules the product already claims to enforce, and fixes defects those tests
  expose.

## Risks / open questions

- **A sweep that only adds tests is a sweep that found nothing.** The measure of this spec is the
  defects it surfaces, not the assertions it counts. Recommended default: every module's section
  in `module-coverage.md` states plainly what was found, including "nothing".
- **Test-suite runtime.** The .NET suite is ~7 s and the script suite a few minutes; adding
  materially to either is a cost. Recommended default: prefer an assertion in the cheapest layer
  that can hold it — xUnit for an invariant, a script step for a cross-module seam, Playwright
  only for something genuinely visual.

---

## Closure — 2026-07-28

Every acceptance criterion met; each states how it was verified.

| AC | How it was verified |
|---|---|
| 1. `module-coverage.md` lists all 22 modules with build status and a three-axis verdict | The document exists, 14 built modules swept, 8 recorded as unbuilt and out of scope |
| 2. Every closed gap has a named runnable assertion, cited in the lifecycle row | Applied centrally by QA from each engineer's reported rows; `check-lifecycle-traceability.sh` now resolves every `xunit` citation against `tests/` |
| 3. `--tier all` exits 0 on a freshly seeded database | **SUITE GREEN — 14 scripts, 0 failed**, roles 12/12, ward census unchanged. Verified four times across the sweep |
| 4. `dotnet test` green with a grown count | **156 → 306**, 0 failed, build 0 warnings |
| 5. `check-lifecycle-traceability.sh --stats` passes | OK — 175 cases, 162 covered, 13 gaps; counts machine-derived |
| 6. Nothing pushed or deployed | Confirmed — all work is in the working tree, no commits made |

**Defects found and fixed: 12**, each reproduced before and re-probed after. Two existed only
because of other work: **M3-D2** was created by the M3-D1 fix and caught solely by re-running the
original scenario, and the two harness defects in the final regression were exposed by QA-H1's
tier reordering.

**The finding that justifies the spec:** **LC-REG-20** — `/registration/new` returned HTTP 500 for
every unknown-identity registration, so the ER could not register an unconscious patient. The
service-level test for that rule passed and always had, because it constructed the command with a
real `""`. Only a case that named the *operator* and drove the *screen* could find it — which is
why the sweep read the code rather than the register, and why a row naming a performer was not
allowed to be discharged by a service-level fact.

**Two QA errors, both corrected in the record rather than quietly dropped:** M20-F1's premise was
wrong (the tray already gated its button), and the UCS-2 divergence bands I gave included 68–70,
which is not a divergence. A register that hides its own mistakes is the exact failure this sweep
was created to find in someone else's.

**Carried forward, not closed:** M4-F3 (`CollectAsync` unvalidated tender), LC-XCUT-08's e2e half,
LC-XCUT-11 (ADR-0024), M3-R1 → PM as **P25**, requiring every `auto` row to carry a matching
`case()` once the remaining scripts are audited, and the eleven `DbContext`s still outside the
additive-migration gate.
