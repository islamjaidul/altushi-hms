# Full QA Audit — every built module, route-by-route, PRD-cross-checked (spec 0038)

## Context

The user wants a **bullet-proofing audit** of everything claimed as built (15 of 22 PRD §5 modules across two SKUs), producing a comprehensive, severity-ranked **handoff report for a senior engineer** to fix. Requirements: list all modules; QA every module's routes, forms, validation, CRUD and reports; cross-check against the PRD and industry standardness; walk the product from a patient's perspective admission→discharge; assume nothing. Find-and-report only — **nothing is fixed in this campaign**.

User decisions (confirmed): **local, fresh DB** (the VM's ERP image is stale and mutating QA there is permanent) · new probes become **permanent scripts** in `eng/verify/` · report = **repo doc in `docs/qa/` + private web artifact**.

Exploration findings that shape the plan:
- 82 routes / 141 POST handlers; **zero declarative validation** (0 DataAnnotations, 1 ModelState use, ~290 bare `[BindProperty]`) — all validation is service-layer, so field tests must be black-box garbage-posting.
- Handler-in-body authz seams: `/emr/prescription` Correct, `/ipd/folio` (9 handlers), `/ipd/discharge` (6), `/radiology/worklist` Done, `/lis/board`, `/hr/payroll` (7), `/hr/leave`; `hr.salary.read` is no page's policy (masking is view logic).
- `hrm-thread.py` (37 cases) is an **orphan** — no tier runs it. `_harness.CAST`=12 vs `DevSeed.Cast`=14 (`farid`, `shirin` never exercised).
- **M16 payroll arithmetic has zero tests**, and a code read already surfaced six probable defects — two spot-verified in source:
  - `PayrollService.cs:258` — integer division: OT minute-rate = 0 for basic &lt; ~14,400 Tk.
  - `PayrollService.cs:616-622` — `WorkingDaysAsync(branchId, …)` ignores `branchId`: any branch's holiday changes every branch's proration denominator.
  - Plus (probe to confirm): journal unbalanced when shortfall &gt; 0 → **Lock throws** (`:588-596` + `LockAsync`); `PercentOf` components leak to unconfigured employees (`:198`); post-Generate corrections flagged settled-but-unpaid (`:466-469`); arrears valued at current-period day-rate (`:279`).
- Open known defects to re-verify: M4-F3 unvalidated tender, M3-R1 capacity unenforced, CONC-1 ABBA deadlock (`40P01` caught nowhere), CONC-4 pool 100 vs max_connections 40, M20 e2e GAP, M22 THIN, M2 doctors-today panel, `/hr/roster` 676 KB.
- Public PHI surfaces: `/public/queue`, `/public/report-status`, `/api/typeahead/patients`.

## Governance

1. **Spec-flow**: create `docs/specs/0038-full-qa-audit/` (`spec.md` + this plan archived as `plan.md`); update `docs/specs/README.md` index; close when the report ships.
2. All mutating runs against `localhost:5199` (ERP) / `localhost:5299` (HRM) on a freshly recreated `hms`/`hrm` in `hms-dev-db` (port 5455). Port hygiene first: `lsof -nP -iTCP:5199 -sTCP:LISTEN` (and :5299) — kill leftovers or results are invalid.
3. A t1 run that didn't drive all 12 harness roles is INCOMPLETE, not green.
4. Every **reported** finding's repro is a committed script under `eng/verify/audit/` (`probe-*.py`, reusing `_harness.py`'s Session/case/check/report). Scratchpad exploration allowed, but nothing scratchpad-only may be cited. DB assertions via `docker exec hms-dev-db psql -U postgres -d hms -c "SELECT …"`; SQL staging only where no screen exists, flagged in the finding.

## Phases

**Phase 0 — environment + baseline** (`~/.dotnet` on PATH, `docker start hms-dev-db`):
fresh-DB reset (`DROP DATABASE IF EXISTS hms WITH (FORCE); CREATE DATABASE hms;`, same for `hrm`), boot both hosts (`ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5199 dotnet run --no-launch-profile` in `src/Hms.Web`; `:5299` for `src/Hms.Hr.Web`), `dotnet test hms-erp.slnx` (expect 306), `bash eng/check-lifecycle-traceability.sh --stats`. Record all baselines.

**Phase 1 — existing machinery, full sweep**: `python3 eng/verify/lifecycle-suite.py --tier all` (fresh DB; t0→t2→t1, 14 scripts, 12/12 roles, ward census) · `BASE_URL=http://localhost:5299 python3 eng/verify/hrm-thread.py` (run the orphan explicitly) · Playwright suite in `eng/verify/ui/` (245 tests). Any red = regression finding.

**Phase 2 — PRD cross-check (desk audit)**: per built module, extract every `[M]`/`[S]` bullet from PRD §5/§5A (sed by heading line, never whole-file) → table *Requirement | tag | route | asserted-by | verdict*. Seed from `docs/specs/0013-mvp-requirement-gaps/plan.md` (extend its matrix from 8 modules to 15) and `docs/qa/module-coverage.md`. Unbuilt M12–M15/M17–M19: one confirmation line each. Industry-standardness = per-module checklist column (duplicate-patient detection, allergy surfacing, discharge summary, payslip PDF…), tagged Info unless PRD marks [M].

**Phase 3 — `probe-validation.py`** (sampled ~30 of 141 handlers, money/PHI first: billing collect, folio, POS, payroll, registration, EMR): per field post (1) omitted required, (2) type garbage, (3) negative/overflow, (4) 100 KB string, (5) impossible dates. Assert: no 500, readable error, psql row-count unmoved.

**Phase 4 — `probe-authz-seams.py` + `probe-public-phi.py`**: for each seam, log in as a read-only-role holder (explicitly casting `farid`/`shirin`), lift the antiforgery token, POST each named handler → assert 403 **and** no state change. Salary masking as `shirin`. Public: anonymous queue/report-status (identity leak, ±50 ID enumeration), typeahead (401 anonymous; field minimality + branch scope authed). Also `/denied` anonymous redirect-loop check.

**Phase 5 — M16 payroll-arithmetic attack** (`probe-payroll-math.py`, `probe-attendance.py` against :5299): confirm PAY-01…06 above at runtime, plus enumerated cases with concrete whole-taka numbers — Fixed30 vs calendar proration (Feb = 27,999?), rounding residue (`Taka.AbsorbResidue` has zero callers), tax-slab boundaries + annual-vs-monthly semantics, late-grace boundary, OT threshold/cap, PF eligibility `MonthsBetween` edges, separation edges, duplicate-run guard; night-shift punch pairing, import idempotency + cross-source dedupe hole (`DeviceId` in key), window overlap, degenerate punches, absent-days-cost-nothing when no DeductionRule. Assert `payroll_lines`/component rows via psql. Fixtures via `/hr/policies`, `/hr/masters`, `/hr/employees/new`; SQL staging only for what has no screen (tax slabs, second branch), flagged.

**Phase 6 — `probe-known-defects.py`**: M4-F3 (tender ≠ total, negative, 10×), M3-R1 (book past `MaxSerials`, record where it never refuses), CONC-1 (two threads, folio→stock vs stock→folio, catch `40P01` 500), CONC-4 (config check only — deployment risk, not locally reproducible), M20 (first test ever to count `SmsQueue` rows per triggering event), M2 doctors-today panel vs psql, M22 dashboard KPIs vs SQL recomputation.

**Phase 7 — patient-perspective walk** (manual, admission→discharge): register → front desk → appointment → OPD bill → consult → order → LIS → radiology → pharmacy → IPD admit → OT → folio → discharge → dues. At each step: what does the patient hold in hand, and does money reconcile (final psql: folio = charges − payments − discounts). Log missing standard-of-care artifacts (no PDF anywhere, no payslip route, no patient merge) as Info/Standardness unless PRD-[M].

**Phase 8 — drift/meta audit**: CAST 12 vs 14 · orphan hrm-thread · stale doc counts (README "twelve scripts" = 10; skill "169 cases" = 175) · `/hr/roster` page weight · spec 0034 still In Progress in the index.

**Phase 9 — report + artifact + close spec 0038.**

## Report skeleton — `docs/qa/full-audit-2026-08.md` (+ artifact)

1. Executive summary + top-10 findings box · 2. 22-row module scoreboard (extends module-coverage.md with *PRD [M] coverage* and *findings this audit*) · 3. Findings ledger — ID `AUD-<area>-<nn>`, severity **Blocker / High / Medium / Low / Info** (Blocker = money corruption, unlockable payroll, PHI leak, data loss), each with surface (route + file:line), exact repro (script + command), expected vs actual, PRD ref, evidence; "owner hint" names the file only, no fix proposals · 4. Regression baseline numbers · 5. PRD traceability appendix · 6. Drift & process appendix · 7. **What was NOT tested** (honesty section): validation sampled not exhaustive, no load/browser-matrix/accessibility/pen-testing, 7 unbuilt modules out of scope.

## Critical files

- `eng/verify/_harness.py` (every new probe builds on it) · `eng/verify/lifecycle-suite.py`
- `src/Modules/Hr/Hms.Hr/PayrollService.cs`, `AttendanceService.cs`, `PolicyResolver.cs` (probe targets, not edit targets)
- `docs/specs/0013-mvp-requirement-gaps/plan.md`, `docs/qa/module-coverage.md`, `docs/qa/patient-lifecycle.md`
- New: `docs/specs/0038-full-qa-audit/{spec,plan}.md`, `eng/verify/audit/probe-*.py`, `docs/qa/full-audit-2026-08.md`

## Verification

The campaign is itself verification; its own checks: every probe script re-runs green/red deterministically twice in a row on the same DB; every reported finding's repro command is executed once from a clean shell before it enters the report; traceability guard still passes after new scripts land; spec 0038 closed with plan archived and index bumped.

## Effort honesty

≈4.5–5 focused days equivalent. Exhaustive: route×role authz, listed authz seams, enumerated M16 arithmetic, 6 known defects, PRD [M] traceability for 15 modules. Sampled: garbage-posting (~30/141 handlers). Not attempted: load, browser matrix, accessibility, penetration beyond listed surfaces.
