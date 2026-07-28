# 0030 — Grant drift on the deployment, and a permission enforced nowhere

- **Status:** Done
- **Date:** 2026-07-28
- **PRD ref:** §12 (role/permission matrix), §3.2, §5 M3
- **MVP:** in scope — no new product scope. Both items are defects against §12 as already
  specified.

## Problem

`docs/qa/findings-2026-07-28.md` F1 and F2. Two separate defects that share a root: the §12
matrix is enforced in one place in code and stored as editable data in another, and nothing
compares the two.

### F1 — the deployment's grants have drifted from the code

On `https://hms.specshipper.com`, `rasel` (Billing Operator) reaches `/admin/approvals`, which
carries `[Authorize(Policy = Perm.AdminApprovalsDecide)]`. **The operator who requests a
discount can approve it** — separation of duties on the money path, gone. `admin` reaches
thirty routes beyond its granted set and is effectively a superuser. Locally both are refused,
so this is the deployment, not the build.

Cause: `src/Hms.Web/DevSeed.cs` seeds permissions **additively** — it inserts a grant when absent
and never removes one. A grant from an older build, or one added by hand at `/admin/users`,
survives every subsequent deploy. Nothing anywhere compares a deployment's grants to the code's
matrix.

### F2 — `appointments.create` is declared, granted, and enforced nowhere

| Where | What |
|---|---|
| `Perm.cs` | declares `appointments.create` |
| `DevSeed.cs` | grants it to the Receptionist |
| everywhere else | no reference |

`/appointments` carries only `[Authorize(Policy = Perm.AppointmentsRead)]`, and neither
`OnPostIssueAsync` nor `OnPostAdvanceAsync` adds a check. **`appointments.read` alone can issue
and advance serials.** Not exploitable by a seeded role today — the Receptionist holds both —
but F1 proves grants drift in practice, and `/admin/users` lets an administrator create exactly
the read-only grant that would silently confer write.

The codebase already has the right pattern for a screen two roles share, at
`Pages/Lis/Board.cshtml.cs`: the page policy carries the read grant, and each mutating handler
carries the finer one.

## Requirements

- **[M]** `OnPostIssueAsync` and `OnPostAdvanceAsync` refuse without `appointments.create`,
  at the handler, not by hiding a button (G10).
- **[M]** A test that fails without the check and passes with it (G5).
- **[M]** `appointments.create` removed from `KNOWN_UNENFORCED` in
  `eng/check-lifecycle-traceability.sh`.
- **[M]** The audit of every other permission enforced only by a page policy, recorded — the
  traceability guard catches a permission enforced *nowhere*; it cannot catch one enforced
  *too coarsely*.
- **[M]** Grant drift is **detected**. A check that compares a running deployment's role grants
  against the code's matrix and reports the difference, in both directions, runnable read-only
  against any environment.
- **[M]** The drift on production is corrected: `admin.approvals.decide` revoked from Billing
  Operator, the Admin role audited, the change attributable in the audit trail.
- **[M]** An ADR for the reconcile-vs-report decision. Making `DevSeed` revoke grants absent
  from `Roles` would silently strip permissions from a customer database on the next deploy —
  plausibly a worse hazard than the drift itself.
- **[S]** `LC-XCUT-14` and `LC-QUE-08` move from `gap` to covered in
  `docs/qa/patient-lifecycle.md`, in the same commit as the code.

## Out of scope

- Whether the Admin role on production *should* be a superuser. That is a §12 question for the
  PM; it is raised in `docs/architecture/09-questions-for-pm.md`, not decided here.
- Any change to the permission set itself.

## Acceptance

- `chowdhury`-shaped session (read without create) is refused by both appointment handlers,
  proven by an executed test.
- `role-journeys.py` reports zero grant drift against local, and reports the production drift
  before the fix and none after.
- `check-lifecycle-traceability.sh` passes with an empty `KNOWN_UNENFORCED`.
