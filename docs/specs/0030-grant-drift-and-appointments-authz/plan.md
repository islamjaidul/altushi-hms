# 0030 — Plan

## Approved: 2026-07-28

## Approach

Two defects, deliberately not conflated: one is a code fix with a test, the other is a live-data
fix plus a decision about how drift is prevented.

### F2 — enforce `appointments.create`

Follow the pattern already in the codebase at `Pages/Lis/Board.cshtml.cs`: the page policy
carries the *read* grant because two roles legitimately open the screen; each **mutating**
handler carries the finer grant.

```csharp
/// §12 splits reading the queue from issuing against it.
private bool CanIssue => Can("appointments.create");
// in OnPostIssueAsync and OnPostAdvanceAsync
if (!CanIssue) return Forbid();
```

`OnPostAdvanceAsync` is included deliberately: advancing a serial to `Done`/`NoShow` consumes it
and frees it for reissue, which is a write to the queue whatever it is called.

Test-first (G5): a role-journeys case that POSTs both handlers as `chowdhury` — who holds
`appointments.read` via nothing at all today, so the test grants nothing and instead uses the
generic "a role without the permission is refused at the handler" probe already in
`role-journeys.py` — plus an xUnit page test. Written to fail against the current code first.

Then remove `appointments.create` from `KNOWN_UNENFORCED` in
`eng/check-lifecycle-traceability.sh`, which is what makes the fix permanent.

**The coarse-enforcement audit.** The guard finds permissions enforced *nowhere*. It cannot find
one enforced *too coarsely* — a mutating handler sitting behind a read policy. That set is
derived by hand for this spec: every page whose `[Authorize]` policy is a `*.read` permission and
which has an `OnPost*` handler. Findings recorded in `notes.md`.

### F1 — drift detection, then the live fix

**Detection.** `/admin/users` already renders the whole §12 matrix, one button per (role,
permission) pair, labelled `title="Revoke <perm> for <role>"` when held and `"Grant …"` when not.
So a read-only `admin` session can recover a deployment's entire grant matrix from one page.
`eng/verify/grant-drift.py`:

1. parses the code's matrix out of `DevSeed.cs`'s `Roles` dictionary — the source of truth;
2. reads the deployment's matrix from `/admin/users`;
3. reports, per role, grants **the deployment has and the code does not** (the F1 shape) and
   grants **the code has and the deployment does not** (a failed deploy, the opposite hazard);
4. exits non-zero on any difference.

It is read-only by default and runs in t0, so every environment is checked on every QA pass.

**The live fix.** `--fix` revokes the extra grants through the same
`/admin/users?handler=Permission` POST an administrator would use, so every revocation writes a
tier-2 `role.revoke` audit event and bumps the holders' security stamps. It is gated behind the
`_harness.guard()` interlock — a non-local target needs `HMS_QA_ENV` and `HMS_QA_CONFIRM`.
It only ever **revokes what the code does not grant**; it never grants, because inventing a grant
on a customer database is precisely the hazard the ADR is about.

**The ADR.** `ADR-0023` records reconcile-vs-report. Recommendation: **report, do not reconcile.**
`DevSeed` runs on startup with `Seed:DevUsers=true`; making it revoke would mean that a customer
who deliberately granted an extra permission at `/admin/users` — which §5 M21 explicitly invites —
loses it silently at the next restart, with no operator present and no message. Drift that is
*reported and then fixed deliberately* keeps the human in the loop that Rule 4's whole philosophy
depends on. Written with `adr-write`.

## Risks

- **`Forbid()` on a Razor Page redirects rather than 403s.** That is the app-wide convention and
  the harness's `denied()` already understands it; the test must assert the `/denied` redirect,
  not a 403 (spec 0028 notes, trap 2).
- **Revoking on production changes a live system.** It is a demo instance, the change is exactly
  the one the finding calls urgent, it is audited, and it is reversible from the same screen.
- **Parsing `DevSeed.cs` with a regex** is brittle if the file's shape changes. The traceability
  guard already parses C# this way; the parser fails loudly rather than silently reading an empty
  matrix.

## Verification

- xUnit: both handlers refuse without the grant; pass with it.
- `role-journeys.py` green on local, including the two new handler probes.
- `grant-drift.py` against local: no drift. Against production: reports the F1 drift, then
  reports none after `--fix`.
- `check-lifecycle-traceability.sh` green with `KNOWN_UNENFORCED` empty.
