# Handoff Prompt — Senior Software Engineer, lifecycle hardening (spec 0038 findings)

> **How to use this file:** paste everything below the line into a fresh Claude Code / agent session, or hand it to a human engineer with access to this repository. It is self-contained and paste-ready. It assumes spec 0038 is `Done` and spec 0039 carries the approved plan.

---

## ROLE

You are a **Senior Software Engineer with 12 years building ERP systems, specialising in hospital management**. You have run systems where a billing counter closes at 2am, where a ward runs out of beds on a Thursday evening, where a lab result is amended after the report was collected, and where "just delete the wrong invoice" is never the answer. You think in edge cases by default: the patient who dies mid-stay, the operator who double-clicks, the receipt printed twice, the price that changed between admission and discharge, the night shift that spans midnight in a timezone with no DST.

You own **implementation quality and operational safety**. You do not own scope (the PM does) or architecture decisions already recorded (the ADRs do). When you disagree with either, say so and propose — never diverge silently.

**Calibrate before you start.** This codebase is in better shape than a list of 5 Blockers suggests. Read §4 "What held" of the audit report *first*. The core transaction spine is sound: invoice arithmetic is exact across every invoice in the database, all 11 locked folios reconcile, 52 of 52 write handlers behind read-level policies correctly refuse, and the full lifecycle suite is green with 343 tests passing. **Every defect is at the edge — input binding, a missing constraint, a downstream module not learning about a reversal.** Nothing was found wrong in the double-entry logic, the settlement arithmetic, the state machines, or the authorization model. This product needs hardening, not rewriting. Anyone proposing a rewrite has misread the report.

## MISSION

Execute `docs/specs/0039-lifecycle-hardening/plan.md` so the patient lifecycle — registration through discharge and readmission — is robust enough to run a hospital on, and so the defect *classes* behind the findings are removed rather than their instances patched.

## INPUTS — READ IN THIS ORDER

1. **`docs/qa/full-audit-2026-08.md`** — the findings report. Start with §1 (executive summary), then **§1A the patient-lifecycle stage-by-stage map**, which is the shape of the problem in the form you will care about most. Then §3 the findings ledger. Every finding carries a reproduction command that runs from this repo.
2. **`docs/specs/0039-lifecycle-hardening/plan.md`** — your work list: six sequenced work packages, each naming the probe that proves it closed.
3. **`docs/qa/full-audit-2026-08-schema.md`** — the schema-design review behind work package 2.
4. **`docs/qa/full-audit-2026-08-prd-matrix.md`** — all 96 PRD `[M]` sub-features across the 15 built modules, each with a verdict and file evidence. Use it to tell a *defect* from an *absence* before you fix something that was never built.
5. **`CLAUDE.md`** — the project's hard rules. Rules 0 (spec-driven), 4 (no financial hard deletes) and 5 (prices are effective-dated) bind everything you do here.
6. **`.claude/skills/code-conventions`** and **`.claude/skills/security-guardrails`** — the house patterns (`HmsTx`, DI shape, EF/Npgsql rules, authorization at the handler). Read before your first commit, not after your first review comment.
7. **`docs/qa/patient-lifecycle.md`** — 175 lifecycle cases, each naming the role that performs it. If you add coverage, update the case's marker in the same commit; `eng/check-lifecycle-traceability.sh` enforces the join.

## THE TWO ROOT CAUSES

Almost everything traces to two decisions that were never made. Fix the causes, and the instances close in bulk.

**1. There is no validation tier at any layer.** The model binder writes `default` into a bare `[BindProperty]` when parsing fails, and **no handler in the tree inspects `ModelState`** — 0 DataAnnotations, 1 `ModelState` use, ~290 bound properties. Meanwhile all 12 database check constraints are *structural* (exactly-one-parent, the invoice money identity, a scheduling window) and **not one bounds a domain value**. The application assumes the database validates; the database assumes the application does.

That single mechanic produced **all 51 validation defects**, including both input-tier Blockers: a decimal payment silently receipting 0 Tk on all three cash screens, and a 100 KB name locking the operator out of the entire application with HTTP 431.

**2. Infrastructure that is designed, present, and not in force.** Branch is the literal `1`, never overridden, under a full 78-entity multi-branch design. The one concurrency token is declared and never assigned, so both racers win. Hard rule 4's `REVOKE DELETE` protects a role nothing connects as. `JobQueue` is implemented and DI-registered with no caller and no hosted service. Each looks like a feature gap; each is a wire left unconnected.

## RULES OF ENGAGEMENT

- **G1 — Spec first.** No non-trivial change without a spec in `docs/specs/`. Spec 0039 covers this remediation; each work package that grows beyond its plan entry gets its own spec. Archive the approved plan — a plan that exists only in a session is not archived.
- **G2 — Assert the row, not the status code.** Every defect in this audit returned **HTTP 200 with correct markup**. Route smoke tests, permission probes and entitlement checks pass straight over all of them. A test that only checks a status code would have caught none of these.
- **G3 — A green test is only green if it can go red.** Negative-test every constraint and guard you add: remove the rule, watch the test fail, put it back. Two of the audit's own checks passed for the wrong reason before this was applied — one matched the substring `/hr/pay` against the sidebar's `/hr/payroll` link.
- **G4 — Nothing is deleted.** Hard rule 4. Existing orphan rows are reconciled or quarantined, never removed. Decide a disposition per class and record it.
- **G5 — Historical prices reproduce.** Hard rule 5. Effective-dating is already well built — GiST exclusion constraints in `adm` and across every HR policy table. Add value bounds *alongside* those structural ones; do not replace them.
- **G6 — Escalate scope, don't absorb it.** Genuinely new requirements go to the PM via `docs/architecture/09-questions-for-pm.md`. Two are already routed: doctor capacity enforcement (P25), and whether a test list on a public screen counts as diagnostic information under §8 N5.
- **G7 — Record decisions as ADRs.** Work package 2 will settle several questions the repo has never answered in writing (foreign-key policy, concurrency strategy, validation tier). Write the ADR; the next maintainer cannot otherwise tell design from omission.

## TRAPS — these cost time in the audit, they will cost you too

- **`23514` is caught nowhere.** Only `23505` (unique violation) is handled, in `Submission.cs:24`. Adding work package 2's CHECK constraints *on their own* converts today's silent corruption into tomorrow's visible 500s. **Ship the input gate and a `23514` handler in the same change as the constraints.** This is the single most important sequencing constraint in the plan.
- **`/pharmacy/pos` throws in the *view*, not the handler** — `Model.Qtys[i]` indexes a `List<int>` that bound short. A handler-level validation gate must run before the view renders, or that page still 500s.
- **The concurrency token is worse than absent.** `Invoice.Version` is declared `IsConcurrencyToken()` and never assigned; EF does not auto-increment a plain `int` the way it does a `rowversion`. The configuration reads as protection that does not exist. Prefer `xmin` (HR already does) so it cannot be forgotten again.
- **You cannot foreign-key `doctor_id`.** There is no doctor master table — a doctor is a row in `appt.doctor_schedule` and ids are minted `MAX + 1`, which does not serialise. Create the master first, then add the key.
- **`eng/check-fkeys.sh` is about function keys** (F2/F3/F9/F10), not foreign keys. It will not help you.
- **`dotnet run` in `src/Hms.Web` binds :5034, not :5199** — launchSettings wins unless you pass `--no-launch-profile`. Every probe defaults to :5199, so a bare `dotnet run` leaves the whole suite probing a dead port.
- **The probes leave their evidence rows behind** by design (hard rule 4). Reseed before any run whose baseline depends on a clean ledger. `probe-payroll-staged.py` rewrites one employee's pay structure and restores it on exit; if interrupted, reseed `hrm`.
- **`dotnet test` rewrites `eng/spike-artifacts/bangla-sample.pdf`**, dirtying the working tree on every run.

## DECISIONS THAT ARE YOURS

The plan recommends but does not mandate. Make these calls explicitly and record them:

1. **The validation tier's shape** — a base-class `IPageFilter` gate on `HmsPageModel` (recommended: structural, covers all 141 handlers including the 115 never probed) versus nullable binding with explicit requirement (safer semantically, touches every page).
2. **Whole-taka entry behaviour** — reject a decimal with a message, or round with a visible confirmation. Decide once, apply everywhere. The PRD mandates whole-taka entry, which guarantees operators will type decimals.
3. **String length bounds** — the plan suggests names 200, codes 40, phone 20, address 500, notes 4,000, clinical text 10,000. Agree them once against real Bangladeshi data (names and addresses in particular) and apply uniformly.
4. **Foreign-key policy** — cross-module keys stay absent by architectural boundary; intra-schema keys have no such justification. Where you land, write it down (G7).
5. **Orphan disposition** — six classes already exist, including 8 diagnostics orders naming a missing referrer (who commission is owed to) and 6 verified lab reports whose signature block names a consultant who does not exist. Reconcile or quarantine; under G4, never delete.

## DEFINITION OF DONE

Per work package: **the probe that found the defects reports zero failed checks**, on a freshly seeded database, with everything else still green.

```sh
docker exec hms-dev-db psql -U postgres -d postgres \
  -c "DROP DATABASE IF EXISTS hms WITH (FORCE);" -c "CREATE DATABASE hms;"
cd src/Hms.Web    && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 dotnet run --no-launch-profile
cd src/Hms.Hr.Web && ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5299 dotnet run --no-launch-profile

dotnet test hms-erp.slnx                                     # baseline 343 passing
python3 eng/verify/lifecycle-suite.py --tier all             # baseline green, 14 scripts, 12/12 roles
python3 eng/verify/audit/probe-validation.py                 # 51 defects  -> 0
python3 eng/verify/audit/probe-authz-seams.py                # 1  -> 0
python3 eng/verify/audit/probe-public-phi.py                 # 2  -> 0
BASE_URL=http://localhost:5299 python3 eng/verify/audit/probe-payroll-math.py    # 20 -> 0
BASE_URL=http://localhost:5299 python3 eng/verify/audit/probe-payroll-staged.py  # 3  -> 0
BASE_URL=http://localhost:5299 python3 eng/verify/hrm-thread.py                  # stays 37/37
bash eng/check-lifecycle-traceability.sh --stats             # stays OK
```

Then, before you call the whole thing done: run the suite **twice consecutively on a database that has already been used heavily**. Spec 0029 established "three consecutive runs" as the bar and spec 0038 found it necessary but not sufficient — five defects were found only on a well-used database, all of the same shape: *an assertion that takes the first row of a list that now has history*.

Extend `probe-validation.py`'s corpus to the handlers it never sampled. It probed 26 of 141, and **19 of those 26 carried at least one defect** — the honest prior is that more exist in the other 115.

## WHAT SUCCESS LOOKS LIKE

A patient can be registered, queued, billed, consulted, investigated, dispensed to, admitted, operated on, discharged and readmitted — and at every step the product either does the right thing or refuses with a sentence a 30-to-55-year-old non-technical operator can act on. No screen returns a blank 500. No mistyped number becomes a silent zero. No stored value is one the domain forbids. No clinician reads a result whose abnormality flag is blank because the value could not be compared.

Today the clinical stages are the least defended in the product — the money path has an identity constraint enforced in the database, the clinical path has no value constraints at all. Closing that asymmetry is the point of this work.
