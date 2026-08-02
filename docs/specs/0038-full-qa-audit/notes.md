# 0038 — notes

## Outcome

Report: [`docs/qa/full-audit-2026-08.md`](../../qa/full-audit-2026-08.md).
PRD matrix appendix: [`docs/qa/full-audit-2026-08-prd-matrix.md`](../../qa/full-audit-2026-08-prd-matrix.md).
Probes: `eng/verify/audit/probe-{payroll-math,payroll-staged,authz-seams,public-phi,validation}.py`.

Nothing in `src/` was changed. Every finding carries a repro that runs from the repo.

## What the audit changed its mind about

Recorded because the report is only trustworthy if the corrections are visible too.

1. **The authorization seams were the wrong suspicion.** The plan ranked the handler-guarded-
   in-body pattern as the top risk after M16 — 38 route+handler pairs, a page policy of `read`
   fronting money handlers. All 52 POSTs refused and the database was unmoved after every one.
   The risk was real to *look* for and absent in fact.
2. **Two probe checks passed for the wrong reason and were rewritten.**
   `AUD-M16-03`'s first form matched the substring `/hr/pay` and passed on the sidebar's
   `/hr/payroll` link; it now requires a POST form. `AUD-M16-07` asserted `ot_paid > 0`, which
   passed on a 29% underpayment; it now asserts the amount against the rate.
3. **`eng/check-fkeys.sh` is about function keys, not foreign keys.** Checked before citing it.
4. **`module-coverage.md`'s "no test referenced `SmsQueue`" is stale** — `SmsQueueTests` exists.
   Reported as a documentation correction, not a gap.
5. **Radiology *is* refund-aware** (`RadiologyReporting.cs:48` filters `!t.Refunded`); an earlier
   draft claimed both worklists were not. Only the lab board is. The finding narrowed.
6. **AUD-PHI-01 was rated down from High to Medium** once the masking was verified against real
   names, with the escalation condition stated and routed to the PM rather than decided here.
7. **AUD-VAL-04 was rated *up* from High to Blocker** when the deeper sweep showed the dropped
   payment affects all three cash screens (`/billing/opd`, `/pharmacy/pos`, `/diagnostics/order`),
   not only diagnostics. Severity should follow the blast radius, not the order of discovery.
8. **My first attempt to reproduce AUD-VAL-05 failed and the finding was nearly discarded.** I
   posted `AgeText` where the registration form binds `AgeOrDob`, so the long name never reached
   the database and the session stayed healthy. Re-run with the field names parsed from the form,
   it reproduced exactly: 37 cookies, 136,172 bytes, HTTP 431 on every subsequent request
   including `/logout`. **A failed reproduction is evidence about the probe until the probe is
   proven right.**

## The finding that changed the shape of the report

The validation sweep's value was not the 51 individual defects — it was that all 51 have **one**
cause (`ModelState` is inspected nowhere; the binder's `default` is accepted as operator input).
Reported as one root cause with instances, because fifty tickets and one ticket lead to very
different remediation.

## Process

- `probe-payroll-staged.py` initially rewrote `hr.payroll_policy` with no `WHERE`, changing both
  branches' floors, and did not restore the pay structure it altered — which then made
  `probe-payroll-math.py` count every historical HRA line as a leak. Both fixed; the staged probe
  now scopes its writes and restores them, and the `hrm` database was reseeded to re-establish an
  honest baseline. **A probe that mutates shared fixtures has to put them back or the next probe
  lies.**
- Subagents committed probe scripts to `main` unasked **twice** (`e3ae5f2`, `4ad023f`). Both
  reverted with `git reset --soft`; the files are kept and uncommitted, nothing was pushed.
  Committing is the user's call, and a delegated task needs to say so explicitly.
- The validation sweep ran while other probes were writing to the same `hms` database, so row
  *deltas* were unreliable. It compensated by backing every defect with an attributing query
  against the specific row rather than a count difference — the right call, and the reason its
  findings survived review.
- `dotnet test` rewrites `eng/spike-artifacts/bangla-sample.pdf` on every run, dirtying the tree.
  Restored; recorded in the report's drift table.

## Deliberately not done

No fixes. A fix inside the audit would invalidate the baseline it was measured against, and the
severity ordering is the senior engineer's input, not a queue this spec should have consumed.
