---
name: tdd-loop
description: The test-first discipline for this codebase — writing the failing test before the production line, choosing the cheapest tier that can hold the real invariant, running one test without a full Docker round-trip, and what is exempt. Use before writing any C# that carries domain logic, money, a state transition, a permission check or a cross-module effect, when fixing a defect, and when deciding which of the five test projects a new test belongs in.
---

# TDD loop — HMS ERP

**Test first. Not test eventually.** The order is not a style preference here: this product's two
worst shipped defects were both invisible to inspection and both would have been caught by a test
written before the code.

- Spec 0037 — six HR screens returned HTTP 200, rendered a success toast, and wrote nothing.
  `HrTx.RunAsync` committed a transaction it had never flushed. Every screen *looked* correct.
- Spec 0045 — US1.4's create path shipped without an edit path, so a patient registered
  unconscious and unnamed could never be named. The code that existed was all correct.

A test written afterwards is written by someone who already believes the code works. That is the
failure mode.

## The loop

1. **Red.** Write one test that states the invariant in business terms and fails *for the right
   reason*. Run it. **Read the failure message.** A test that fails with `NullReferenceException`
   when you expected `Assert.Equal(500, actual)` is not yet red — it is broken.
2. **Green.** The least production code that makes it pass. Not the design you intend to end at.
3. **Refactor.** Now shape it — against `code-conventions` and `domain-modelling`. The test is your
   safety net; it stays green through every step.
4. **State it.** When you report the change, say you saw the test fail and what the failure said.
   "Added a test" without that sentence is unverifiable — and it is the standing rule in
   `code-conventions`.

For a defect, red means **reproducing the defect**, not testing the fix. Write the test that fails
on today's `main`. If it passes before you touch anything, you have not found the bug.

## Which tier takes the first test

Put the first failing test at **the cheapest tier that can hold the real invariant** — not the
cheapest tier that can hold *an* assertion. A parser test standing in for a locking guarantee is
worse than no test, because it reports green.

| Tier | Cost | Takes the first test when the invariant is… |
|---|---|---|
| `tests/Hms.Web.Tests` | ms, no DB | a pure function on a page model — parsing, formatting, phone normalisation, an age string, an SMS segment count |
| `tests/Hms.Kernel.Tests` | ms, no DB | a kernel primitive — business day, fiscal year, entitlement resolution, nav composition |
| `tests/Hms.Integration.Tests` | Docker, real Postgres | **anything touching SQL** — money arithmetic that must round once, a state machine, a row lock, a unique constraint, a cross-module effect, a check constraint |
| `tests/Hms.Architecture.Tests` | reflection, no DB | a rule that must hold for *all future code* — boundary, permission coverage, base-class coverage |
| `eng/verify/*.py` | HTTP, running app | the operator's real path across modules, end to end |
| `eng/verify/ui/tests` | browser | genuinely visual or keyboard behaviour |

**The honest default for domain work is Integration.** SQLite-in-memory is banned for it (G7) —
the guarantees under test live in Postgres row locks and constraints, and an in-memory provider
will happily pass a test that production fails. `PostgresFixture` starts one real
`postgres:17.5-alpine` per collection; join it with `[Collection("postgres")]`.

A single invariant often deserves tests at two tiers — the arithmetic in `Web.Tests` for a
millisecond loop, the persisted consequence in `Integration.Tests` for truth. Write the fast one
first if it speeds your loop; ship both.

## Keeping the loop fast when the tier is Integration

The container start dominates. Do not pay it per assertion:

```bash
# one class — the loop you live in
dotnet test tests/Hms.Integration.Tests --filter "FullyQualifiedName~WardMoneySeamTests"

# one test
dotnet test tests/Hms.Integration.Tests --filter "FullyQualifiedName~Discharge_without_settlement_is_refused"

# the no-Docker tiers, for a sub-second red-green cycle on pure logic
dotnet test tests/Hms.Web.Tests tests/Hms.Kernel.Tests tests/Hms.Architecture.Tests
```

Everything in a collection shares one container, so **add your test to an existing class in the
right family** rather than opening a new one — a new class in a new collection is a new container.
Before claiming done, run the full suite: `dotnet test hms-erp.slnx -c Debug`.

## Mandatory, and exempt

**Mandatory — no production line before a failing test:**

- Domain logic and any calculation
- Money, in any form — amount, discount, rounding, tender, refund, reversal
- State transitions and their guards (admission, leave, payroll run, invoice, sample, order)
- Permissions and authorization decisions
- Concurrency: anything taking a row lock, a unique constraint, or an `xmin` token
- Cross-module effects (`cross-module-flow`)
- Schema constraints — the CHECK belongs in a migration *and* in a test that proves the database
  refuses the bad row (`schema-and-indexing`)
- Every defect fix

**Exempt — covered by guards and verify scripts instead:**

- Razor markup, CSS, layout, icon and token work — `eng/check-ui-tokens.sh`,
  `eng/check-no-external-hosts.sh`, `eng/check-no-native-date.sh` and `eng/verify/ui/tests` hold
  this ground
- Pure wiring with no branch: a DI registration, a nav entry, a constant
- Seed and demo data

Exempt means *this tier of ceremony is not required*. It does not mean unverified — say how you
checked.

## What a good test looks like here

- **Name it as the business sentence it defends.** `The_database_refuses_a_closed_task_with_no_closing_stamp`,
  `Doses_already_past_today_are_not_generated_overdue`, `App_role_cannot_delete_financial_rows`.
  A reader who never opens the body should learn the rule.
- **One invariant per test.** A test asserting five things reports one failure and hides four.
- **Assert the persisted state, not the return value.** Spec 0037's screens all returned correctly.
  Read the row back through a fresh context — a passing assertion against the change tracker proves
  nothing about what committed.
- **Prove the negative too.** The row that must be refused matters as much as the row that must be
  accepted; a constraint with only a happy-path test is untested.
- **A new structural rule needs an Architecture test**, and structural rules police the future.
  Follow `InputGateCoverageTests` for the shape: a reflective sweep, plus an explicit allowlist
  whose entries are documented decisions rather than accumulated exceptions.
