# 0030 — Notes (afterwards)

## The coarse-enforcement audit

The spec asked for the set the traceability guard cannot see: a **mutating handler sitting behind
a page policy that only carries a read permission**. The guard finds a permission enforced
*nowhere*; it cannot find one enforced *too coarsely*.

Derived by reading every page model whose `[Authorize(Policy = …)]` resolves to a `*.read`
permission and which has at least one `OnPost*Async`:

| Page | Page policy | POST handlers | Finer check in each handler |
|---|---|---|---|
| `Appointments/Index` | `appointments.read` | 2 | `appointments.create` — **added by this spec** |
| `Emr/Prescription` | `emr.read` | 1 | `emr.note.write` |
| `Ipd/Admissions` | `ipd.read` | 6 | `ipd.manage` |
| `Ipd/Board` | `ipd.read` | 3 | `ipd.manage` |
| `Ipd/Discharge` | `ipd.read` | 6 | `ipd.manage` / `ipd.settle` |
| `Ipd/Folio` | `ipd.read` | 9 | `ipd.manage` / `ipd.service.post` / `ipd.settle` |
| `Lis/Board` | `lis.worklist.read` | 3 | `lis.sample.collect` |
| `Radiology/Worklist` | `radiology.worklist.read` | 1 | `radiology.study.perform` |

Thirty-one handlers across eight screens; every one guarded in its first statements. **Appointments
was the only gap**, and the pattern the rest already follow is the one it now follows.

Worth stating plainly: this is a hand audit at a point in time, and nothing keeps it true. A
mechanical version — "every `OnPost*Async` on a page whose policy is a `*.read` permission must
guard" — would need source analysis rather than reflection, because `Can("…")` takes a string
literal. Left for whoever wants it; the value is real but so is the cost.

## Reading the deployment's matrix

`/admin/users` renders the §12 matrix as one form per (role, permission) cell, and the button's
`title` is `Revoke <perm> for <role>` when the role holds it, `Grant …` when it does not. That is
the whole detector: one authenticated GET.

Two things bit while writing it, both worth remembering:

**The four values of a cell live in four separate tags, and the cells are adjacent.** A regex that
matches `name="roleId"` and then scans forward for the button title picks up the *neighbouring*
role's id — lazily, so it silently finds the earliest id that fits the window. The first `--fix`
run revoked nothing and reported success. `grant_cells()` matches the whole tuple in one pass for
this reason.

**A permission appears in the matrix only while something carries it.** `AllPermissions` is the
nav registry's permissions ∪ whatever any role holds, so a permission that protects no *screen* —
`appointments.create` is one, because it guards handlers rather than a route — drops out of the
table entirely once the last role loses it. The LC-ROLE-14 assertion had to become "the
Receptionist does not hold it", not "the cell shows Grant". A test that looked for the Grant
button would have reported a successful revocation as a failure forever.

## Production, before and after

Read-only probe of `https://hms.specshipper.com` reproduced F1 exactly:

```
Admin has appointments.create, appointments.read, billing.invoice.create,
  billing.receipt.create, billing.session.close, billing.session.open, dashboard.read,
  diagnostics.order.create, lis.result.enter, lis.result.verify, lis.sample.collect,
  lis.worklist.read, pharmacy.purchase.manage, pharmacy.read, pharmacy.sale.create,
  pharmacy.stock.manage, registration.create, registration.read
Billing Operator has admin.approvals.decide
```

Nineteen grants the code does not carry — eighteen on Admin, and the one that mattered on Billing
Operator. Corrected with `--fix` after the deploy; see `docs/qa/findings-2026-07-28-round2.md`.

## Deviation

The plan named the ADR `0027`; the next free number was **`0023`**. No other change.
