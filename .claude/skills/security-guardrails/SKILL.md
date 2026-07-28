---
name: security-guardrails
description: Security rules for the HMS ERP codebase — authorization at the handler not the button, the permission/policy trap, tenant and branch scoping, audit and no-hard-delete obligations, secrets and connection handling, SQL and injection rules, PHI exposure on public surfaces, and the interlocks that stop a test run mutating a real deployment. Use before writing or reviewing any endpoint, handler, query, migration, verify script, or anything touching money, permissions, patient data, or a deployed environment.
---

# Security guardrails — HMS ERP

This product holds patient identity, clinical records and money for a Bangladeshi private
hospital. The threat model is not a sophisticated attacker — it is a **mis-granted permission, a
mis-scoped query, and a test script pointed at production**. All three have already happened here
or come within one step of it.

Every rule below is followed by the way it has actually been broken.

## 1. Authorization

**Deny by default, server-side, at the handler.**

- Every page carries `[Authorize(Policy = Perm.X)]`, and `ModuleNav.cs` declares the same string.
- **Hiding a button is not a control.** A mutating handler must check the permission itself.
  `HandlerPermissionTests` enforces this.

> **How it broke (spec 0030):** `appointments.create` was declared in `Perm.cs`, granted to the
> Receptionist, and *enforced nowhere*. No seeded role could reach it — the Receptionist held
> both — but §12 is editable data at `/admin/users`, so a view-only grant silently conferred
> write. Seeded roles proving nothing is the point: **grants drift, code must not.**

**The `Can` / `Perm` trap.** `HmsPageModel.Can(p)` tests the **bare claim**; `Perm.*` constants
are **policy** names carrying a `perm:` prefix that `PermissionPolicy.TryParse` strips. So:

```csharp
@if (Model.Can("admin.masters.manage"))   // correct
@if (Model.Can(Perm.AdminMastersManage))  // compiles, ALWAYS FALSE, reviews as correct
```

`ViewGuardPermissionTests` forbids the second form. If you add a guard helper, extend that test.

**A view must not offer an action its own policy cannot perform** — and a screen posting to
another page's handler inherits *that* handler's policy, not its own.

**Separation of duties is real.** The technologist who entered a result cannot verify it; the
supervisor who approves cannot settle; the technician who performs a study cannot report it. When
adding a role or permission, add the `role-journeys.py` case proving the *adjacent* job is
refused — not just that the granted one works.

**Permission changes must take effect within 5 minutes** (ADR-0019): bump the security stamp on
any permission or role change, or a revoked operator keeps working until their cookie expires.

## 2. Scoping — the quiet class of bug

Every query over patient, money or clinical data must be scoped to the **branch** and must
exclude soft-deleted/merged rows. `PatientSearch.Searchable` filters `Active && MergedInto == null`;
a bespoke query that forgets it resurrects merged patients into a picker.

Never trust an id from the request as authorisation. `?patientId=`, `?folioId=`, `?invoiceId=`
identify a row — they do not prove the caller may see it. Load, then check.

> **How it nearly broke:** `IssueSerialAsync` takes `doctorId` on trust with no lookup against
> `Schedules` and no FK. The dropdown constrains the browser; the handler constrains nothing
> (open, raised as **P25**).

## 3. Money, audit, and the no-delete rule

- **No hard deletes of financial or clinical rows, ever** (hard rule 4). A correction is a
  reversal: a negative receipt pointing at the original, a cancelled-not-deleted invoice, a
  superseding note. The app's database role has **no DELETE grant** on financial tables and
  `MoneySpineTests` proves it (`42501 insufficient_privilege`).
- **Audit is append-only and attributable** — user + time on every financial and clinical write,
  committed in the *same* transaction as the change (`AuditWriterTests` proves both directions).
- **Tier-2 audit** for the sensitive ones: refunds, cancellations, discharge-with-dues, settlement
  reopen, bill block/release.
- **Approval-gate the reversals.** Refund, cancel, block/release, stock write-off and post-lock
  posting all route through `ApprovalEngine`; the gate is on the server, not the screen.

> **How it broke (spec 0032):** a refund did not maintain `bill.due.balance`, so
> `Σ receipts + due = net` was false for every refunded invoice, and the MD dashboard and the
> printed day-close statement both counted reversed invoices as income. **A reversal that is not
> propagated is a silent misstatement of revenue.**

## 4. SQL

Use EF LINQ, or `SqlQuery<T>($"…")` / `ExecuteSqlAsync($"…")` **interpolated** — these are
parameterised by the compiler. `ExecuteSqlRawAsync` is raw: it appears exactly twice, both for the
constant advisory-lock id at startup. **Never build SQL by string concatenation with a value from
a request.**

Generated columns and CHECK constraints are security controls too: `phone_digits` is maintained by
the database precisely so a second write path cannot drift from it, and `ck_identity` /
`qty >= 0` stop invalid state at the row rather than trusting every caller.

## 5. PHI on public surfaces

`/public/queue` and `/public/report-status` are the **only** `[AllowAnonymous]` surfaces besides
login and health. They are lobby-TV and self-service screens:

- Mask names (`Ui.MaskName`) — a full name on a waiting-room screen is a disclosure.
- Never show money, diagnosis, or contact details.
- Answer probes **neutrally**: report-status must not let an attacker enumerate valid order
  numbers by the difference between "not found" and "not ready".
- `role-journeys.py` asserts the anonymous surface is exactly these routes. Adding an
  `[AllowAnonymous]` without updating that assertion is how the surface grows unnoticed.

## 6. Secrets and configuration

- **No secrets in the repo.** Connection strings and keys come from configuration/environment;
  production values live in `deploy/.env` on the VM only.
- **No external hosts at runtime** — fonts and icons are vendored;
  `eng/check-no-external-hosts.sh` fails the build on a CDN reference. The app must render with
  the network unplugged (§8 N2), and a CDN is also a third party watching a hospital's traffic.
- Entitlement files are **signature-verified offline** (ADR-0016) — never a network call.
- Don't log PHI or secrets. Development logs SQL parameters; that configuration must not follow
  to production.

## 7. Running anything against a real environment

This is the rule most likely to cause actual harm, because it is the one that looks like testing.

**Every mutating verify script must call `_harness.guard(tier)`.** Anything that is not
demonstrably localhost is treated as a deployment and refuses to be written to without an explicit
`HMS_QA_ENV` *and* the host typed back in `HMS_QA_CONFIRM`. Tier t2 against `prod` is refused
unconditionally.

The reason is rule 4: a mutating production run leaves rows that can never be removed, only
reversed. Audit events, bed-days, stock ledger entries, consumed number-series values and sent SMS
are **permanent**.

> **How it nearly broke (spec 0032):** `frontdesk-check.py` had **no `guard()` at all**. It
> registers a patient, admits them, takes ৳700 and absconds the admission. Pointed at a
> deployment it would have done all four with no interlock; only its hardcoded `localhost`
> default stood in the way — and making `BASE_URL` configurable was about to remove that.
> **A script outside the shared harness is outside every safety rail the harness carries.**

Before believing any result, check what is actually on the port and which database it serves
(`lsof -nP -iTCP:5199 -sTCP:LISTEN`). A stale instance silently serving a different database has
cost hours here more than once.

## Review checklist

- [ ] Handler enforces the permission, not just the view
- [ ] `Can("bare.claim")`, never `Can(Perm.X)`
- [ ] Query scoped to branch; merged/inactive rows excluded; request ids not trusted as authority
- [ ] No delete of financial/clinical rows; reversal instead; audit in the same transaction
- [ ] Reversals propagated to every figure that reads them (due, dashboards, printed statements)
- [ ] SQL parameterised; no raw concatenation of request values
- [ ] Nothing new is `[AllowAnonymous]`; no PHI or money on public surfaces
- [ ] No secrets, no external hosts, no PHI in logs
- [ ] Any mutating script calls `guard()` and uses `_harness`
- [ ] A test exists that fails when the control is removed
