# 0039 — tasks

Status legend: [x] done · [~] in flight · [ ] open. Evidence column cites the probe/test that
proves the row; the Verification section of `plan.md` is the acceptance gate for the whole spec.

## WP1 — input tier
- [x] 1.1 Base-class gate on `HmsPageModel` (binding/annotation failure on a posted field never
      reaches a handler; operator sentence via layout banner) — `InputGateCoverageTests` (2)
- [x] 1.2 `Bounds` constants + `[StringLength]`/`[Money]`/`[Qty]`/`[Percent]`/`[PlausibleDate]`
      across all probed pages (two sweep agents; Opd is the reference)
- [x] 1.3 Registration lockout: session-backed TempData in both hosts, toast clipping, name
      bounds (AUD-VAL-05)
- [x] 1.4 Claimed-payment ⇒ receipt assertion inside the save tx (Opd, Pos, Diagnostics/Order)
- [x] 1.5 Whole-taka behaviour decided: reject with a message, uniformly (ADR-0028)

## WP2 — schema
- [x] 2.1 Domain CHECKs (money, qty, pct, clinical, dates) — 13 `Hardening0039` migrations,
      every constraint on a pre-existing table `NOT VALID` (ADR-0028)
- [x] 2.2 All state columns constrained + `AdvanceAsync` validates its target state
- [x] 2.3 Intra-schema FKs (`ON DELETE RESTRICT`, `NOT VALID`); cross-module refs validated at
      the service edge; orphan disposition = retain + NOT VALID (recorded in ADR-0028)
- [x] 2.4 Hot-path indexes (charge_line, encounter, diag.*, appointment on_date, rate_version
      btree, receipt invoice_id, sample_test, bed_stay partial)
- [x] 2.5 `xmin` tokens (Invoice, Admission, Folio, Batch, StockAuditLine, Result); doctor
      master `appt.doctor` + identity ids + FKs + seed/People screen rework
- [x] 2.6 Hard-rule-4 grants applied idempotently at boot (`ApplyHardRule4GrantsAsync`);
      compose.yml documents the hms_app cut-over; `eng/check-no-hard-deletes.sh` guard
- [x] 2.7 ADR-0028 written; 23514/23505/23503/40P01/53300 fault boundary shipped WITH the
      constraints
- [ ] `VALIDATE CONSTRAINT` follow-ups once legacy orphan classes are reconciled (deliberate,
      see ADR-0028 reversal triggers)

## WP3 — payroll (agent-executed; see agent report in notes.md)
- [~] 3.1–3.6 journal algebra, policy screens, attendance import, pay screen, arithmetic,
      SoD split — acceptance: probe-payroll-math/staged → 0, hrm-thread 37/37

## WP4 — reversal propagation
- [x] Proportional refund restock (amount-budgeted, batch-locked) — AUD-M11-01
- [x] Lab worklist refund filter matches radiology — AUD-M9-01
- [x] Amendment carries narrative; narrative-only reports amendable; radiology sign fires
      ReportReady SMS — AUD-M10-01

## WP5 — branch and identity
- [x] Branch resolved from the signed-in user (`AppUser.BranchId` → claim → `HmsPageModel`),
      mid-shift effect via security stamp; `/admin/users` branch binding (§5 M21)
- [x] Global branch query filters on every BranchId entity in 13 contexts (generic applier);
      `BranchIsolationTests` (13) fails when a context forgets — watched red/green
- [x] HrDbContext isolation (after WP3's model settles) — filter + arch test +
      `HrBranchIsolationTests` SQL-level proof
- [ ] 5.2 Patient merge repair (`[S]`, LC-REG-16) — **deliberately deferred**: PRD marks it
      Should; no probe covers it; the write path (repoint + approval + audit) is its own spec

## WP6 — platform (partly agent-executed)
- [x] Global fault boundary (no blank 500s; SQLSTATE → sentences) — AUD-VAL-01/07
- [x] Pool ceiling ≤15/host vs max_connections=40 — AUD-XCUT-01
- [x] /denied bounce after sign-in — AUD-PHI-02
- [x] Background worker + escalation/digest/reminders/SMS drain — AUD-XCUT-02 (`JobWorkerTests`)
- [x] SMS gateway (live mode delivers) — AUD-M20-01 (`SmsGatewayTests`, `SmsDispatchJobTests`)
- [x] Public report-status token + test-list removal — AUD-PHI-01 (`PublicReportLookupTests`)
- [x] Real Code 128 barcode — AUD-M1-01 (`Barcode128Tests`)

## Verification
- [x] probe-validation fixture (AUD-VAL-13) reworked to post valid fixtures and probe the bad
      payloads explicitly — assertions unchanged
- [x] Full DoD run on fresh DB: lifecycle-suite --tier all GREEN (14 scripts, 12/12 roles) ·
      probe-validation 144/0 · probe-authz 16/0 · probe-public-phi 5/0 · payroll-math 10/0 ·
      payroll-staged 4/0 · hrm-thread 37/37 · traceability OK (175/162/13)
- [x] `dotnet test hms-erp.slnx` — 407 passed, 0 failed (baseline 343); 29 fixtures repaired
      for the new FKs (parents seeded, never constraints weakened)
- [x] Second consecutive run on the used database — t0+t1 GREEN twice; hrm-thread 38/0 twice;
      probe-validation 144/0 twice. Caught two real classes first: demo-stock exhaustion
      (fixed with `ensure_demo_stock`) and the non-deferrable HR policy EXCLUDEs
      (`HrPolicyExcludeDeferrable0039`)
- [x] probe-validation corpus extended beyond the 26 sampled handlers (AUD-VAL-27..31; found
      and fixed 3 real defects: transfers ragged-zip + ghost outlet, discharge reshow NRE)
- [x] Doc sweep: qa-lifecycle SKILL counts, qa/README t1 count, 0034 plan note,
      module-coverage M16/M20 rows (verified + ordering note superseded by HRM-EMP-08)
