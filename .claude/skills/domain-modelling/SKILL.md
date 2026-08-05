---
name: domain-modelling
description: How to model a domain in this codebase's idiom — where an invariant belongs, what the aggregate boundary is, when a concept earns its own type, how commands and results are shaped, and which SOLID and DDD ideas apply here versus which are an ADR question. Use when starting a new module, when a service grows past comprehension, when the same rule appears in two places, and whenever tempted to introduce a repository, MediatR, CQRS or a per-module host.
---

# Domain modelling — HMS ERP

`code-conventions` states the architecture; `schema-and-indexing` states the data shape. This skill
is about where the *thinking* goes.

## The house position on DDD and SOLID

**DDD's tactical patterns are welcome. DDD's infrastructure patterns are not.**

| Idea | Here |
|---|---|
| Ubiquitous language — code names match what the hospital says | **Yes.** `Folio`, `Admission`, `Indent`, `Due`, `Realised`. Never `BillEntity2`. |
| Bounded context = module = schema | **Yes**, already true. Fifteen contexts, hard boundaries, `.Contracts` between them. |
| Aggregate = the consistency boundary | **Yes** — see below. |
| Value objects for concepts with rules | **Yes**, as `sealed record`s. |
| Domain services for logic spanning entities | **Yes** — that is what the module services are. |
| Repository over EF | **No.** `DbContext` is already a unit of work with a repository per set. |
| MediatR / in-process bus | **No.** A method call is a method call. |
| CQRS with separate read models | **No**, beyond the plain read projections the dashboard uses. |
| Per-module host or ports-and-adapters | **No.** §16 gives us 2 vCPU / 3 GB; process count and indirection both cost. |

If you believe a change genuinely needs one of the "No" rows, that is an **ADR** (`adr-write`) with
a measured argument — not a refactor you slip into a feature branch.

**SOLID here means:**

- **S** — a service has one reason to change because it owns one part of the domain. `RateResolver`
  resolves prices and does nothing else. Not "one class per method".
- **O** — extend by adding a new effective-dated row, a new catalogue entry, a new template. The
  business changes by data far more often than by code; model it so.
- **L** — barely applies; there is very little inheritance and that is correct. `HmsPageModel` is
  the one base class that matters and it adds behaviour every page must have.
- **I** — `.Contracts` interfaces expose the *minimum* another module needs. `IPatientLookup`
  returning a `PatientBanner` is the shape: not the entity, just what the caller needs.
- **D** — services take their `DbContext` as a **parameter**, not a constructor dependency. That is
  the inversion that makes them safe as stateless singletons. Do not "fix" it by injecting the
  context.

## Where an invariant belongs

Push it as far down as it will go. Each level up is a level someone can bypass.

| Level | For | Bypassable by |
|---|---|---|
| **Database CHECK / unique / FK** | anything true of a row in isolation | nothing — this is why 379 of them exist |
| **Row lock in the service** (`SELECT … FOR UPDATE`) | anything true across rows under contention — bed occupancy, stock, folio, counter session | nothing, if the lock is taken |
| **Domain service method** | multi-entity rules, state transitions, arithmetic | a caller that does not use the method |
| **Composition-root orchestrator** | rules spanning modules (`cross-module-flow`) | a page that writes directly |
| **Page model** | input shape and operator affordances only | any other entry point — import, job, API, a future screen |

**A rule that exists only in a page model is not a domain rule.** It is a UI convenience that will
be contradicted the first time a second path writes the same table. If it matters, it goes lower.

## Aggregates: the boundary is the transaction

**One aggregate = one consistency boundary = one `tx.RunAsync` call.** If two things must always be
true together, they are one aggregate and they commit together. If they can be eventually
consistent, they are separate and the seam is explicit (`cross-module-flow`).

Practical consequences:

- The invoice, its lines, its number and its audit row are **one** action.
- An admission and its folio are one boundary; the folio and the pharmacy batch it drew from are
  two, joined by an orchestrator.
- **Lock ordering is part of the aggregate design.** When a path takes more than one lock, fix a
  global order and document it — `IpdService.TransferAsync` orders two beds by id precisely to
  avoid an ABBA deadlock. Never catch `PostgresException` broadly to paper over a deadlock; fix the
  order.

Entities here are EF-mapped classes with public setters, not rich objects that guard themselves.
That is a deliberate trade: the guarding lives in the CHECK constraints and the service methods,
which is where it can be enforced against every path. **Do not add behaviour to entity classes** —
it will not be called by the paths that matter.

## When a concept earns its own type

Extract a type when the concept has **rules of its own** that keep being restated. Two live
examples worth copying:

- **`InvoiceValue`** (`src/Modules/Billing/Hms.Billing/InvoiceValue.cs`) — "what an invoice is
  actually worth after a reversal", extracted so the MD dashboard and the day-close statement
  cannot disagree about the same money. It carries the invariant
  `Σ receipts + due.balance = Realised(state, net, refunded)`, and the doc comment records the
  rejected alternative and *why*. That is a domain concept with a home.
- **`PatientSearch`** (`src/Hms.Web/PatientSearch.cs`) — the one patient-matching rule: name, UHID
  or phone digits, all understood by one query, plus `Searchable()` as the single definition of
  which patients may be offered anywhere.

Do **not** extract a type for a value with no rules. A `long` of taka is a `long`; the codebase is
whole-taka integers end to end and a `Money` wrapper would buy nothing that `[Money]` validation
and integer arithmetic do not already give.

**Commands and results are `sealed record`s** — `RegisterPatientCommand`, `UpdatePatientCommand`,
`ResolvedRate`, `BatchAllocation`, `ImportResult`. Positional records, named for the business act.
A service method taking nine parameters wants a command record; a method returning a tuple of five
wants a result record.

## Starting a new module

1. **Read the PRD module section first** (`prd-lookup`), and route the scope (`scope-routing`).
   Model what §5 describes, in its words.
2. **Name the bounded context** — schema, project pair `Hms.<Name>` + `Hms.<Name>.Contracts`.
3. **Find the aggregates.** Ask "what must be true at the same instant?" Each answer is a
   transaction boundary.
4. **Write the schema with its constraints** (`schema-and-indexing`) — the invariants that are true
   of a row, encoded where nothing can bypass them.
5. **Write the failing tests** (`tdd-loop`) — the state machine, the money, the refusals.
6. **Write the service**: stateless, singleton, `DbContext` as a parameter, one part of the domain.
7. **Publish the minimum through `.Contracts`** — only what another module genuinely needs.
8. **Put cross-module orchestration at the composition root** (`cross-module-flow`).
9. **Then the screens** (`crud-completeness`).

## Smells, and what they actually mean

| Smell | Usually means |
|---|---|
| The same rule appears in two page models | It is a domain rule sitting one level too high — push it into a service or a constraint |
| A service method takes a `bool` that switches behaviour | Two operations wearing one name |
| A service holds a field | A data race across every concurrent request — services are singletons |
| A page model injects a `DbContext` | The transaction seam was bypassed; use `HmsTx` |
| A module references another module's `Data` namespace | Boundary violation; `ModuleBoundaryTests` will fail the build |
| A record is fetched, checked in C#, then updated | A race. Take the row lock or add the unique constraint |
| "We should add a repository so this is testable" | It is already testable — the context is a parameter. Write the integration test |
