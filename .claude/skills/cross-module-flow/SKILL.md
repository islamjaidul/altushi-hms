---
name: cross-module-flow
description: What a module owes another when a business event crosses a schema boundary — where orchestration lives, the standing obligations (order to charge, dispense to stock, event to notification, terminal state to dependents), and how to prove the effect landed. Use when a change in one module must show up in another, when adding an orchestrator at the composition root, when a charge, stock move or notification is missing, and when testing anything that spans modules.
---

# Cross-module flow — HMS ERP

`code-conventions` owns the *mechanism*: `.Contracts` only, one `HmsTx` per business action, no EF
join across two `DbContext`s. This skill owns the *obligation* — what has to happen on the other
side, and how you prove it did.

**This is where the product's correctness actually lives.** Any single module can be perfect and the
hospital still loses money, because a test the doctor ordered was never billed, or a drug the ward
issued never left stock. Spec 0042 found exactly that: every test until then drove module services
directly, so `IpdBilling.OrderTestsAsync`, `IssueIndentAsync` and `PostServiceAsync` — *the three
paths that turn ward events into folio money* — had zero coverage. Each module was tested. The
seams between them were not.

## Where orchestration lives

**At the composition root, never inside a module.** A module that reaches into another module's
schema has broken the boundary; `ModuleBoundaryTests` fails the build on the reference.

The orchestrators are the static classes in `src/Hms.Web/`:

| Orchestrator | The seam it owns |
|---|---|
| `IpdBilling.cs` | ward event → folio money: `PostServiceAsync`, `IssueIndentAsync`, `OrderTestsAsync`, `CatchUpBedDaysAsync`, `EnsureNotBlockedAsync` |
| `OtBilling.cs` | theatre completion and consumables → charges |
| `PharmacySale.cs` | counter sale → invoice + stock |
| `EmrOrdering.cs` | prescription/order → diagnostics order |
| `DiagnosticsRelease.cs` | payment → sample creation and release |
| `RadiologyReporting.cs` | worklist → report → sign |
| `SmsSender.cs` | any event → notification outbox |

**Adding a seam means adding to this list, not to a module.** If the orchestration seems to belong
inside a module, that is a sign the boundary is drawn wrong — an ADR question (`adr-write`), not a
quiet cross-schema reference.

## The standing obligations

These are not new requirements; they are what §5's modules mean together. Check every one that
applies before calling a cross-module change done.

| When this happens | This must happen, in the same transaction |
|---|---|
| A clinical order is placed (test, imaging, procedure, consultation) | A charge is posted, or an explicit non-billable decision is recorded. An order with no charge and no reason is lost revenue. |
| A drug or consumable is issued or dispensed | Stock decrements against a specific batch, **and** the money lands — on the folio for indoor, on the invoice for counter. |
| An indoor patient consumes a bed-day, service or item | It reaches the folio. `CatchUpBedDaysAsync` exists because accrual that only happens on a screen visit is accrual that gets missed. |
| An event the operator or patient must know about occurs | An outbox row is written **in the same transaction** — never a direct send. A send that happens outside the transaction fires for a business action that then rolls back (§8 N2: the link is unreliable; the outbox is what makes delivery survive it). |
| A record reaches a terminal state (discharge, cancel, close, merge) | Every dependent is resolved — no orphan order, no unsettled folio, no queue entry pointing at a discharged patient, no MAR dose scheduled past discharge. |
| A price is needed | It is resolved through `RateResolver` by **service date**, never read from a form field. A price posted by the browser is ignored. |
| Money moves | An audit event is appended on the same `s.Kernel` context (ADR-0011), and nothing is hard-deleted. |

**The transaction is the boundary of truth.** If the charge and the order can commit separately, one
day they will. Put both inside a single `tx.RunAsync`; that is what `HmsTx` attaching every module
context to one connection is for.

## Reads across a boundary

- **One query per context, joined in memory.** EF cannot join two `DbContext` instances, even in
  one scope — it throws *"Cannot use multiple context instances within a single query execution"*,
  and a method-chain guard in `Hms.Architecture.Tests` catches the attempt.
- **Joining in memory means bounding both sides first.** Two `ToListAsync()` calls and a LINQ join
  is a cross join in the CLR on a 3 GB box. Filter each side in SQL, take the ids from the first,
  and pass them as a `Contains` into the second.
- **No foreign key crosses a schema.** A cross-module reference is a plain id plus a `.Contracts`
  lookup — which means referential integrity is *your* obligation, not the database's. A deleted or
  merged parent must be handled by the reader.

## Proving it landed

A cross-module change is unproven until a test drives **the orchestrator the page actually calls**.
Testing the two module services separately proves nothing about the seam — that is the exact 0042
finding.

1. **Integration test through the orchestrator.** `WardMoneySeamTests` is the pattern: construct a
   real `TxScope` over the fixture's connection — which is what `HmsTx` does — and call
   `IpdBilling.PostServiceAsync` rather than `BillingService` and `IpdService` in turn. `[Collection("postgres")]`.
2. **Assert both sides, read back fresh.** The order exists *and* the charge exists *and* the amount
   is right *and* the audit row is there. Asserting only the initiating side is how a seam passes
   while doing half its job.
3. **Assert the negative.** Roll the transaction back and prove *neither* side persisted. A seam
   that commits the order when the charge fails is worse than one that fails cleanly.
4. **Then the operator's path.** An `eng/verify/*.py` thread using `_harness`, driving the real
   HTTP screens across modules with the right role. A private `Session` loses the environment
   interlock, the role tracking and the `LC-` traceability ids — always use `_harness`.

## Before you call a cross-module change done

- [ ] Orchestration is at the composition root, not inside a module
- [ ] Both sides commit in one `tx.RunAsync`, or the split is a documented decision
- [ ] Every applicable obligation in the table above is satisfied or explicitly waived in the spec
- [ ] Notifications go to the outbox, never sent inline
- [ ] Terminal states leave no orphan
- [ ] Prices resolved server-side by service date
- [ ] Audit appended on `s.Kernel` in the same transaction
- [ ] Cross-context reads are one query per context, both sides bounded in SQL
- [ ] An integration test drives the **orchestrator**, asserts both sides from a fresh read, and
      covers the rollback
- [ ] A verify thread walks the operator's real path across the modules
