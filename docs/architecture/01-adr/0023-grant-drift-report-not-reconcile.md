# 0023 — Grant drift is reported, not silently reconciled

- **Status:** Accepted
- **Date:** 2026-07-28
- **Answers:** QA finding F1 (`docs/qa/findings-2026-07-28.md`), lifecycle case `LC-XCUT-14`
- **Spec:** `docs/specs/0030-grant-drift-and-appointments-authz/`

## Context

`DevSeed.cs` seeds §12's role/permission matrix **additively**: for each permission the code
lists, it inserts a grant if one is absent, and it never removes one. Nothing anywhere compares
a running deployment's grants against the code's.

Two facts make that a real problem rather than a tidy one:

1. **§12 is deliberately editable data.** §5 M21 [M] gives an administrator a permission matrix
   at `/admin/users`, and `UsersModel.OnPostPermissionAsync` writes the grant, audits it at
   tier 2, and bumps every holder's security stamp. A hospital *is expected* to hold grants the
   code did not seed.
2. **Drift has already broken separation of duties in production.** On
   `hms.specshipper.com`, `rasel` (Billing Operator) held `admin.approvals.decide` — the
   operator who requests a discount could approve it — and the Admin role carried eighteen
   grants beyond its template, reaching thirty routes it should not. Locally, the same build
   refuses both. The grants came from an older build or a hand edit; the seed preserved them
   through every subsequent deploy.

So the question is not *whether* to notice drift. It is what the product should do when it
notices, on a customer's database, at startup, with nobody watching.

## Options considered

| Option | Pros | Cons |
|---|---|---|
| Leave it additive (status quo) | Never removes anything an operator relied on | Drift accumulates invisibly and silently defeats separation of duties — the defect above |
| **`DevSeed` reconciles**: revoke any grant not in `Roles` | Deployment always equals the code; nothing to run, nothing to remember | A deliberate grant made at `/admin/users` disappears at the next restart, with no operator present, no message, and no way to tell it from a bug. It also makes §5 M21 a lie: the screen invites an edit the next deploy erases |
| **Detect and report; fix deliberately (chosen)** | Drift becomes visible on every QA pass, in both directions; the correction is a human act with an audit row | Needs the check to actually be run — a report nobody reads is not a control |
| Sign the matrix / make it code-only | Truly tamper-evident | Removes §5 M21 from the product; the PM specified an editable matrix |

## Decision

**Report, do not reconcile.** `DevSeed` stays additive and gains no revoke path.

Detection lives in the verification suite as `eng/verify/grant-drift.py`, in **tier t0**, so it
runs read-only against every environment on every QA pass:

- the code's matrix is parsed from `DevSeed.cs`'s `Roles` dictionary — the source of truth;
- the deployment's matrix is read from `/admin/users`, which already renders every
  (role, permission) pair as a labelled button, so no new endpoint is needed;
- differences are reported **in both directions**: a grant the deployment has and the code does
  not (the F1 shape), and a grant the code has and the deployment does not (a failed or partial
  deploy, the opposite hazard, previously just as invisible);
- a role that exists only on the deployment is named, not judged — the code has nothing to say
  about a role a hospital created.

Correction is a separate, explicit act: `grant-drift.py --fix` **revokes only what the code
does not grant**, through the same `/admin/users?handler=Permission` POST an administrator
would use, so every revocation writes a tier-2 `role.revoke` audit event and bumps the holders'
security stamps. It **never grants** — inventing a permission on a customer's database is
exactly the hazard this ADR refuses. Against a non-local target it is behind the `_harness.py`
interlock: an explicit `HMS_QA_ENV` and a typed `HMS_QA_CONFIRM`.

## Consequences

- The mechanism that produced F1 is now *visible* on every environment, which is the property
  that outlives the demo instance. RUNBOOK §9's go-live switch should run it before and after.
- A hospital keeps the editable matrix §5 M21 promises, and keeps whatever it deliberately
  granted, through every deploy.
- Cost accepted: drift is not prevented, only caught. A deployment can still be wrong between
  QA passes. That is a smaller failure than a restart quietly stripping a permission somebody's
  shift depends on — and unlike that one, it is detectable.
- The check needs an `admin.users.manage` session, so it is one of the few t0 scripts that
  cannot run anonymously. On a hardened deployment where the demo cast is gone (RUNBOOK §9),
  it needs a real administrator credential or it does not run at all.

## Reversal trigger

If a customer deployment is found to have drifted **between** two QA passes in a way that
mattered — a grant added by hand that defeated separation of duties and survived long enough to
be used — then reporting is too weak, and the next step is not silent reconciliation but a
startup check that **refuses to boot** on unexpected drift and names it, which keeps the human
in the loop while removing the window.
