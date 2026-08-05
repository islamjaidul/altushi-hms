---
name: code-conventions
description: The HMS ERP codebase's architecture and coding conventions — modular monolith layout, the HmsTx transaction rule, service and DI shape, EF/Npgsql rules, concurrency and locking discipline, Razor Page patterns, and what the automated guards enforce. Use before writing or reviewing any C#, .cshtml, migration, or verify script in this repo, when adding a module or screen, and when a build guard fails and the reason is not obvious.
---

# Code conventions — HMS ERP

The architecture is a **modular monolith**: one deployable, one database, hard module boundaries
enforced by tests. This is a mainstream .NET pattern and a deliberate fit for §16's single VM at
**2 vCPU / 3 GB** — microservices would multiply processes this box cannot afford, and Clean
Architecture's per-module ports/adapters would multiply indirection this team does not need.

Do not "modernise" toward MediatR/CQRS, a repository layer over EF, or per-module hosts. If you
think a change needs one, that is an ADR (`adr-write`), not a refactor.

**Sibling skills, each owning a concern this one does not restate:** `domain-modelling` (where an
invariant belongs, aggregates, SOLID/DDD here) · `schema-and-indexing` (table shape, constraints,
which columns are indexed) · `crud-completeness` (when a screen is finished) · `cross-module-flow`
(what a module owes another) · `tdd-loop` (test-first order and tier choice) ·
`security-guardrails` (authz, PHI, audit).

## Layout

```
src/Hms.Kernel/                     cross-cutting: Auth, Audit, Numbering, Jobs, Approvals,
                                    Entitlements, Time, Printing, Data
src/Modules/<Name>/Hms.<Name>/           the module: Service(s) + Data/<Name>DbContext + Migrations
src/Modules/<Name>/Hms.<Name>.Contracts/ ONLY what another module may reference
src/Hms.Web/                        composition root: Program.cs, HmsTx, Pages/, Perm, ModuleNav
tests/                              Kernel · Architecture · Integration · Web · PrintGolden
eng/                                guards (check-*.sh) and verify/ (HTTP-level thread scripts)
```

**One schema per module** (`reg`, `bill`, `diag`, `lis`, `appt`, `adm`/`adm_data`, `notif`,
`pharm`, `ipd`, `emr`, `ot`, `radiology`, `kernel`). A module owns its schema and nothing else
writes to it.

**Cross-module reference goes through `.Contracts` only** — `ModuleBoundaryTests` fails the build
otherwise (ADR-0003). Cross-module orchestration lives at the composition root
(`src/Hms.Web/IpdBilling.cs`, `OtBilling.cs`), never inside a module.

## The four rules that are not obvious from reading one file

### 1. One business action = one transaction — `HmsTx`

```csharp
await tx.RunAsync(async s =>
{
    var p = await registration.RegisterAsync(s.Reg, s.Kernel, cmd);
    await SmsSender.SendAsync(s, sms, ...);
    return p;
});
```

`HmsTx.RunAsync` opens **one** `NpgsqlConnection`, begins **one** transaction, and attaches every
module `DbContext` to it (G19). Invoice + number + audit + outbox commit together or not at all.

- **Page models never inject a `DbContext`.** They take `HmsTx` and use `s.Reg`, `s.Bill`, …
- **Services never inject a `DbContext` either** — they take it as a *parameter*. That is what
  makes them safe as singletons (below).
- **EF cannot join across two `DbContext` instances**, even inside one scope — it throws
  *"Cannot use multiple context instances within a single query execution"*. Cross-schema reads
  are one query per context, joined in memory. A method-chain join guard (arch test) catches it.

### 2. Services are stateless singletons

Every domain service is `AddSingleton` in `Program.cs` and holds **no mutable instance state**.
This is only safe because the `DbContext` arrives as a method parameter. If you add a field to a
service, you have introduced a data race across every concurrent request — don't.

The one existing exception is `EntitlementProvider._current`, written once at startup. If you make
it writable at runtime, it needs `volatile` or `Interlocked.Exchange` first.

### 3. Concurrency is PostgreSQL's job, not the CLR's

There is deliberately **no** `lock`, `Interlocked`, `Concurrent*`, `SemaphoreSlim`, or
`BackgroundService` in `src/`. Contention is resolved in the database:

| Mechanism | Where |
|---|---|
| `SELECT … FOR UPDATE` on the contended row | due, counter session, folio, bed, theatre, stock batch, job (17 sites) |
| Unique constraint + bounded retry | appointment serials, number series |
| Optimistic `IsConcurrencyToken` | `bill` version columns, auth security stamp |
| `pg_advisory_lock(422026)` | startup migration, so two instances cannot migrate at once |

**Lock ordering is a real obligation.** `IpdService.TransferAsync` locks two beds
`.OrderBy(id)` precisely to avoid an ABBA deadlock. When a path takes **more than one** lock,
document the order and keep it globally consistent — see the open finding on folio-vs-stock
ordering in `docs/qa/module-coverage.md`.

Never catch `PostgresException` broadly to paper over a deadlock; fix the order.

### 4. Money and time

- **Integers only.** Whole taka end to end; `Amount = Qty * UnitPrice` is exact. Percentage
  discounts round half-up **once, at the total** (`BillingService.RoundHalfUp`).
- **Never delete financial or clinical rows** (hard rule 4). A correction is a reversal: a negative
  receipt pointing at what it reverses, a cancelled-not-deleted invoice, a superseding note.
- **Prices are effective-dated.** Resolve through `RateResolver` by *service date*; a historical
  invoice must reproduce its historical price.
- **Npgsql binds UTC only.** Store `DateTimeOffset` in UTC; convert at the edge with `Ui.Local` /
  `Ui.DhakaMidnightUtc`. A business day is not a UTC date — use `BusinessDayCalendar` (spec 0027).

## Screens (Razor Pages)

- Every page carries `[Authorize(Policy = Perm.X)]`, and `ModuleNav.cs` declares the same string —
  the sidebar and the endpoint read one source, so the menu cannot show what the server refuses.
- **A finer-grained action inside a page gates on `Model.Can("permission.string")`** — note this
  takes the **bare claim**, while `Perm.*` are **policy** names carrying a `perm:` prefix.
  `Can(Perm.X)` compiles and is *silently always false*; `ViewGuardPermissionTests` forbids it.
- Copy the shape of an existing page in the same family — list (`Registration/Index`), POS
  (`Billing/Opd`), pipeline (`Lis/Board`), document (`Billing/Invoice`) — rather than starting fresh.
- **The POS cart lives in the form** as repeated hidden inputs; nothing is written until save, and
  prices are always re-resolved server-side. A price posted by the browser is ignored.
- Dates use the `hms-date` tag helper. Native `<input type="date">` is banned.
- Patient pickers use `/api/typeahead/patients`, never a `<select>` of recent patients.

## Migrations

Additive only (03 §12): no `DROP TABLE`, `DROP COLUMN`, or `TRUNCATE`. Widening a CHECK is
additive; dropping and re-adding it to widen is fine. Add the context to the CI gate loop in
`.github/workflows/ci.yml` — it currently scripts only a subset, and a context outside it is
unguarded.

## The guards, and what each actually checks

| Guard | Fails when |
|---|---|
| `eng/check-ui-tokens.sh` | a literal hex colour appears outside `tokens.css` (including inline `style=`) |
| `eng/check-no-external-hosts.sh` | a CDN or external host is referenced — fonts/icons are vendored |
| `eng/check-no-native-date.sh` | a native `type="date"` appears |
| `eng/check-additive-migrations.sh` | a destructive migration op is generated |
| `eng/check-fkeys.sh` | a screen rebinds a reserved **function** key (F2 New Patient, F3 Item Search, F9 Hold/Recall, F10 Payment) to another purpose. Despite the name this is nothing to do with foreign keys — **no guard enforces the no-cross-schema-FK rule**; that one is on review (`schema-and-indexing`) |
| `eng/check-lifecycle-traceability.sh` | a lifecycle case cites a script or **xUnit class** that does not exist, a user absent from the seeded cast, or the route table drifts from `[Authorize]` |
| `ModuleBoundaryTests` / `CrossContextQueryTests` / `HandlerPermissionTests` / `ViewGuardPermissionTests` | boundary, cross-context join, unguarded handler, guard/policy mismatch |

Run `dotnet test hms-erp.slnx -c Debug` (needs Docker for Testcontainers) plus the relevant
`eng/check-*.sh` before claiming a change is done.

## Tests: pick the cheapest layer that can hold the assertion

| Layer | For |
|---|---|
| `tests/Hms.Web.Tests` | pure functions on page models (parsers, formatters) — no DB, milliseconds |
| `tests/Hms.Kernel.Tests` | kernel primitives (business day, fiscal year, entitlement, nav) |
| `tests/Hms.Integration.Tests` | anything touching SQL: invariants, state machines, locking, constraints |
| `tests/Hms.Architecture.Tests` | structural rules that must hold for all future code |
| `eng/verify/*.py` | the operator's real path over HTTP, across modules — use `_harness` |
| `eng/verify/ui/tests` | genuinely visual or keyboard behaviour |

**Verify scripts must use `_harness`** (`Session`, `check`, `case`, `guard`, `report`). A private
`Session` loses the environment interlock that stops a mutating run against a deployment, the
role tracking behind "roles exercised 12/12", and the `LC-` ids traceability joins on.

**A new rule needs a test that fails without it.** State that you saw it fail.
