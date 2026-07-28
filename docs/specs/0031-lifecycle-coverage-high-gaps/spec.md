# 0031 — Close the High-severity lifecycle gaps

- **Status:** Done
- **Date:** 2026-07-28
- **PRD ref:** §5 M4/M6/M8/M9, §3.2, §8 N1/N2, §11, §12
- **MVP:** in scope — no new product behaviour is specified. Every case below asserts behaviour
  the PRD already requires and specs 0006–0027 already shipped.

## Problem

`docs/qa/findings-2026-07-28.md` F5 and F7. `docs/qa/patient-lifecycle.md` carries 169 cases;
39 have no coverage and 13 of those are High. Two shapes of gap:

**F5 — eleven of sixty-four protected routes are never loaded by any UI test.** They are
disproportionately the consequential screens: `/ipd/folio/{id}`, `/ipd/discharge/{id}`,
`/emr/consult/{id}`, `/emr/prescription/{id}`, `/radiology/report/{id}`, `/radiology/print/{id}`,
`/ot/case/{id}`, `/lis/amend`, `/diagnostics/order/{id}`, `/admin/sms`, `/admin/templates` — the
folio, the discharge gate, the prescription, the signed report, the amend path.

**F7 — the High-severity register.** The ones that matter most:

| ID | Gap |
|---|---|
| LC-BIL-11 | A price change must never alter a historical invoice — G6 names this a permanent money invariant and it has **no end-to-end proof**, because repricing needs an `admin` session and no thread logs in as `admin` |
| LC-DX-03 | Partial payment must **not** release the lab — only the paid-in-full path is asserted, and releasing early gives away an unpaid test |
| LC-BIL-10 | Invoice cancellation as reversal, never deletion — Rule 4's central case |
| LC-DIS-04 | Discharge with an outstanding due — the typed reason and its tier-2 audit entry |
| LC-DIS-07 | Settlement reopen after approval |
| LC-LAB-08 | Amend after verification, and its approval |
| LC-BLK-03 | R4 block/release **approval gating** (the freeze itself is covered; the gate is not) |
| LC-ROLE-14 | Mid-shift permission revocation, via the five-minute security stamp |
| LC-XCUT-09 | Power cut mid-transaction — PRD §8 N2 requires tolerance and nothing tests it |
| LC-XCUT-10 | Two operators editing one folio concurrently — G7 territory, real Postgres |
| LC-XCUT-11 | **No load or concurrency test exists anywhere in the repo** |

## Requirements

- **[M]** All eleven routes in F5 added to `ROUTES` in `eng/verify/ui/helpers/users.ts`, with the
  permission each is actually protected by, so `check-lifecycle-traceability.sh`'s route-drift
  join stays green.
- **[M]** LC-BIL-11 proven end to end: an invoice raised at price A still reads A after `admin`
  reprices to B, and a new invoice reads B. This is the first thread to drive an `admin` session.
- **[M]** LC-DX-03 proven: a partly-paid diagnostic order does not reach the LIS board.
- **[M]** LC-BIL-10, LC-DIS-04, LC-DIS-07, LC-LAB-08, LC-BLK-03, LC-ROLE-14 each proven by an
  executed assertion, performed by the role the lifecycle document assigns to it.
- **[M]** LC-XCUT-09 and LC-XCUT-10 proven against **real Postgres**, not SQLite (G7).
- **[M]** Every case covered flips its marker in `docs/qa/patient-lifecycle.md` from `gap` to its
  coverage kind **in the same commit**, and leaves the gap register.
- **[S]** LC-XCUT-11 — a first-cut concurrency probe, and the architecture question raised rather
  than answered unilaterally.

## Out of scope

- **LC-XCUT-11 as a settled load test.** Forty concurrent operators on 2 vCPU / 3 GB is an
  architecture question as much as a test question. A stdlib probe ships; the budget, the shape
  of the workload and the pass criteria are raised for the architect, not decided here.
- Restructuring the Playwright suite or the xUnit projects. Both are added to.
- Any case whose gap turns out to be a permission-matrix defect. That is a finding, not a licence
  to run the step from a more privileged session.

## Acceptance

- The eleven routes load under their own role in the Playwright suite.
- `docs/qa/patient-lifecycle.md` shows **≤ 3 High gaps** remaining, each with a named reason.
- No marker is flipped for a test that was not observed passing.
