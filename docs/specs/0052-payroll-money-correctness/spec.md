# 0052 — Payroll money correctness: unpaid leave, the carried shortfall, and an unguarded run

- **Status:** Done
- **Date:** 2026-08-06
- **Approved:** 2026-08-06 (plan archived in `plan.md`)
- **PRD ref:** §5 M16 (`[M]` leave without-pay handling, `[M]` payroll from attendance, US16.1 AC), §6.6, §11
- **Scope:** defect repair — three unmet `[M]` behaviours in a module already shipped as `Done`

## Problem

An architecture review of M16 found three defects on the money path. Two of them pay the wrong
amount today, silently, on every run. None is recorded in `docs/qa/full-audit-2026-08.md`; they sit
underneath the AUD-M16 findings that spec 0039 closed.

**1. Unpaid leave is paid in full.** §5 M16 requires without-pay handling and the module models it:
a leave type carries a `Paid` flag, the masters screen writes it, the demo seed ships a "Without
pay" type with `Paid = false`. Nothing reads it. Attendance marks the day `on_leave` and payable
regardless; the payroll line's leave-without-pay counter is never filled; the deduction rule's
per-unpaid-day rate is entered by the operator, validated, stored — and never applied. An employee
on a month of unpaid leave receives a full month's salary, and no deduction line appears, which
also fails US16.1's acceptance criterion that every deduction be traceable to attendance.

**2. The carried shortfall is booked as an asset that nothing backs.** When deductions would drive
net pay below the employer's floor, the shortfall is described as carried rather than forgiven, and
the payroll journal debits *Employee Advances* — a recoverable asset. No ledger row is ever written,
and no later run recovers it. The employer's books claim money owed by the employee that the system
has no record of and will never collect.

**3. The payroll state machine is guarded only by whichever unique index sits next to it.** Locking,
posting and reversing a run read the row without a lock and without a concurrency token, in a module
that contains no row locking at all. Two simultaneous locks both succeed and write two "locked"
audit entries for one lock — an audit trail that states something untrue, against hard rule 4. Two
simultaneous posts or reversals collide on an unrelated unique index and surface a database
exception to the operator instead of a sentence they can act on.

## Requirements

- [M] An unpaid leave day reduces pay, by the employer's own configured rate, and appears as its own
  named deduction line on the payslip with a basis an operator can read.
- [M] A paid leave day continues to pay exactly as it does now — the change must not touch it.
- [M] The employer's minimum-net-pay floor either records the shortfall as a recoverable debt the
  system can later collect, or stops describing it as one. The journal and the data must agree.
- [M] A payroll run cannot be locked, posted or reversed twice. A second attempt is refused with an
  operator sentence naming the run and its state, never a database error.
- [M] Every figure a locked run produced before this change still reproduces unchanged (hard rule 5).

## Acceptance criteria

1. An employee with an approved unpaid-leave application spanning N days of a period is paid the
   configured per-unpaid-day deduction below their otherwise-identical peer, and the payslip carries
   a leave-without-pay line naming the day count. Proven by an integration test.
2. The same employee on a *paid* leave type of the same length is paid identically to the peer, with
   no deduction line. Proven by the same test, as the control.
3. With no deduction rule configured, an unpaid-leave day deducts nothing and the run says so rather
   than assuming a rate (ADR-0027).
4. A run carrying a shortfall produces a journal whose debit to *Employee Advances* is matched by a
   persisted, queryable record of that debt against the employee. Proven by an integration test that
   reads the row back.
5. A second lock, post or reverse of the same run raises the module's own operator-facing failure,
   not `DbUpdateException`. Proven by an integration test per transition.
6. Re-generating a period that already has a locked run still reproduces its stored figures — the
   existing reproduction test stays green, and a new one covers a period containing unpaid leave.
7. `dotnet test` is green across all five test projects; the patient-lifecycle suite is green on the
   ERP host, since HR screens are shared.

## Out of scope

- The full `[S]` loan and advance module — request, approval, disbursement, installment schedule,
  PF and welfare ledgers. Criterion 4 requires only that the shortfall becomes a durable, recoverable
  record; the operator-facing loan lifecycle is a later spec.
- Comp-off, the OT bank, holiday-work pay, bonus and increment (§5A-16, `Should`).
- The other eight findings from the same review — missing foreign keys, unbounded `text` columns,
  the leave-year start month, the leave-overlap exclusion constraint, the trigram and composite
  indexes, and the payroll generation N+1. Those are additive or non-money and belong in their own
  spec, so this one stays reviewable.

## Risks / open questions

- **Does an unpaid-leave day reduce the earning, or add a deduction line?** They differ on the
  payslip and on the PF and tax bases. Recommended default: a **deduction line**, because §5 M16 and
  US16.1 both speak of deductions traceable to attendance, and because `DeductionRule` already
  carries `PerLeaveWithoutPayDayBp` for exactly this. To be confirmed at plan approval.
- **Where does the carried shortfall land?** Recommended default: an `EmployeeLedgerEntry` row, since
  the table exists, is append-only, and already carries the employee, period and run reference — as
  against building the `[S]` loan lifecycle to satisfy a `[M]` accounting invariant.
- **Single-branch assumption.** Confirmed with the product owner on 2026-08-06: one branch per
  install, today and planned. Two further review findings — globally unique document numbers over
  branch-scoped counters, and `payroll_component_line` escaping branch isolation — are latent only
  under that assumption and are deliberately not fixed here. If multi-branch is ever provisioned,
  both become live defects and the numbering one is product-wide, not HR's. Worth an ADR recording
  the assumption rather than leaving it in a spec's risk section.
