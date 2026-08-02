# 0039 — Lifecycle hardening: close the audit's findings and the schema gaps behind them

- **Status:** Done
- **Date:** 2026-08-03
- **PRD ref:** §5 (M1–M11, M16, M20–M22), §5A, §7, §8, §11, §12, §16
- **MVP:** in scope — hardening shipped modules, no new product scope
- **Follows:** [0038-full-qa-audit](../0038-full-qa-audit/spec.md) (findings), which this closes

## Problem

Spec 0038 audited the fifteen built modules and produced a severity-ranked report:
**5 Blockers, 10 High, 9 Medium, 2 Low**, plus a PRD conformance matrix showing 37 of 96
`[M]` sub-features fully done. A follow-on schema review found that the defects are not
independent — they sit on top of **structural gaps in the data model and the request pipeline**,
and fixing the instances without the structure would leave the product equally fragile.

The organising question for this spec is the one the audit was commissioned to answer:
**is the patient lifecycle robust enough to run a hospital on?** Today it is not, in four
specific ways:

1. **Nothing validates operator input.** There is no declarative validation layer, no handler
   inspects `ModelState`, and the database carries no domain-value constraints — only structural
   ones. A mistyped payment is silently receipted as 0 Tk on all three cash screens; a 100 KB
   paste into a patient name locks the operator out of the application entirely.
2. **Payroll cannot complete a cycle**, and its attendance and statutory inputs are unreachable.
3. **The schema cannot defend its own invariants.** No foreign keys, no state-value constraints,
   no length bounds, one concurrency token in the whole product. Six classes of orphan reference
   already exist, and an appointment sits in the state `deleted` because any string is accepted.
4. **Branch is a hardcoded constant**, so the multi-branch design that 78 entities and ADR-0007
   describe is not actually in force, and nothing detects a cross-branch leak.

Each is a *class* of failure, not a bug. This spec closes them by class.

## Requirements

- [M] **A validation tier that fails closed.** Malformed input must be refused with an operator-
      readable message and must never reach a handler as a substituted `default`. Applies to all
      141 POST handlers, not only those the audit sampled.
- [M] **Every value the database stores must be one the domain permits** — money, quantity,
      percentage, clinical measurement, date and state — enforced at the schema, not only in code.
- [M] **Payroll must complete a cycle** — generate → review → approve → lock → post — with its
      attendance, deduction, overtime, provident-fund and tax inputs configurable through the
      product by an administrator.
- [M] **Referential integrity within each schema**, so a child row cannot reference a parent that
      does not exist. Cross-module references stay uncoupled by design (ADR boundary).
- [M] **Branch resolved from the signed-in user**, with isolation enforced structurally rather than
      by every query remembering, and a test that fails when a query forgets.
- [M] **No unhandled exception reaches an operator.** A domain rule violation renders its message;
      an unexpected fault renders a recoverable error page.
- [M] Every finding closed must be **proven closed by the probe that found it** — the spec 0038
      probes in `eng/verify/audit/` are the acceptance instrument and must go green.
- [S] Patient identity repair (merge and deactivation), so a duplicated patient's history can be
      reunited rather than permanently split.
- [S] A background worker, unblocking approval escalation, the end-of-day digest, due reminders
      and SMS delivery.

## Non-goals

- New product scope. Everything here implements PRD §5/§5A as already written; genuinely new
  requirements go to the PM (`09-questions-for-pm.md`) under hard rule 2.
- The seven unbuilt modules (M12–M15, M17–M19).
- Load testing and ADR-0024's concurrency decision, which remain open on their own track.
- Rewriting the modular-monolith boundary. Cross-module foreign keys stay absent deliberately.

## Acceptance criteria

**AC1** — `eng/verify/audit/probe-validation.py`, `probe-payroll-math.py`,
`probe-payroll-staged.py`, `probe-authz-seams.py` and `probe-public-phi.py` all report **zero
failed checks** against a freshly seeded database.
**AC2** — `lifecycle-suite.py --tier all` stays green, `dotnet test` stays green, and the new
guards added by this spec fail when their rule is removed (negative-tested, per spec 0032's
precedent).
**AC3** — A payroll run containing an employee at the minimum-net floor **locks and posts**.
**AC4** — An administrator can configure absence, overtime, grace, provident fund and tax through
the product, and set an employee's salary, without SQL.
**AC5** — Every state column rejects a value outside its legal set, proven by attempting one.
**AC6** — A query that omits its branch filter is caught by an architecture test.
**AC7** — No handler returns HTTP 5xx for any input in the validation corpus; domain violations
render their message.

## Notes

Sequencing rationale is in `plan.md`. The work is ordered by **what breaks a patient's journey
first**, not by module or by severity label alone: an operator locked out at registration stops
the lifecycle before it starts, so the input tier leads.
