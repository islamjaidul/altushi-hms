---
name: crud-completeness
description: What makes a data-management screen actually finished in this codebase — the read path, the write path, authorization, audit, concurrency, the retire path, and the test at each tier. Use when building or reviewing any screen that creates, lists, edits or retires records, when a master or setup screen is requested, and when deciding whether a CRUD screen can be called done.
---

# CRUD completeness — HMS ERP

Read `code-conventions` for page shape and the `HmsTx` rule; this skill is the definition of done.

**"CRUD" is not one requirement — it is five, and this product has shipped screens missing three
of them.** Two precedents, both of which passed every review at the time:

- **Spec 0045** — registration could create a patient but nothing could edit one. An unconscious
  patient registered without a name could never be named. Every line of code was correct; the
  update path simply did not exist.
- **Spec 0037** — six HR screens returned 302, showed the success toast, and wrote nothing.
  `HrTx.RunAsync` committed a transaction it never flushed. The screens were indistinguishable from
  working ones without reading the database back.

The lesson from both: **a CRUD screen is done when the database says so, not when the screen does.**

## The five paths, all of them required

| Path | Missing means |
|---|---|
| **Create** | — |
| **Read (one)** | the record exists but nobody can see what it says |
| **Read (many)** — list, filter, search | it exists but nobody can find it |
| **Update** | a mistake at creation is permanent (spec 0045) |
| **Retire** | dead masters accumulate in every dropdown forever |

If a path is deliberately absent, that is a decision: say so in the spec with the reason. Silence
means it was forgotten.

**Retire is never a delete.** Follow `schema-and-indexing`: `Active=false` for a master, a reversal
for a financial fact, `SupersedesId` for a clinical record. Hard rule 4 has no exceptions.

## Read path

- **`AsNoTracking()` on every read-only query.** Tracking a list you will not save is pure cost on a
  3 GB box.
- **Filter soft-deleted rows through the one named helper**, not a copy-pasted predicate —
  `PatientSearch.Searchable()` is the pattern. Forgetting it once offers a merged patient for
  billing.
- **The predicate goes to SQL** (ADR-0020 §2). `ToListAsync()` then `.Where()` in C# reads the whole
  table.
- **Bound the result.** Every list caps its rows — `.Take(200)` on the pharmacy catalogue,
  `.Take(50)` on indents, `.Take(25)` on transfers. An unbounded list is a page that eventually
  times out. When the cap can hide a row the operator needs, the screen needs a filter that narrows
  before the cap applies, not a bigger cap.
- **Every filter and search column is indexed** — and substring search needs a trigram index.
  `schema-and-indexing` has the rule and the list of surfaces currently missing one.
- **An empty list says why** — "no results for that search" is different from "nothing configured
  yet", and the operator (§7: 30–55, non-technical) needs to know which.

## Write path

- **The handler is inside `tx.RunAsync`**, one business action, one transaction. The invoice, its
  number, its audit row and its outbox entry commit together or not at all.
- **The write must flush.** This is the 0037 defect. `HmsTx.RunAsync` calls `SaveChangesAsync` on
  each attached context before commit, so on the ERP host a staged change becomes durable — but
  never assume a transaction seam flushes for you. If you add or touch one, prove it with a test
  that reads the row back through a fresh context.
- **Validation is declared, not hand-rolled.** `[Required]`, `[StringLength(Bounds.Name)]`,
  `[Money]` on the bound property. Widths come from `Bounds` in `src/Hms.Shell/Validation.cs`
  (`Name` 200, `Code` 40, `Phone` 20, `Address` 500) — never a magic number.
- **The page derives from `HmsPageModel`.** That is what puts it behind the input gate: a binding or
  annotation failure fails closed before the handler runs, so a mistyped payment cannot become a
  silently receipted 0 Tk. `InputGateCoverageTests` fails the build if a page skips it; adding a
  name to that allowlist is a documented decision, not a formality.
- **The row-level rule is also a CHECK constraint.** Annotations guard this screen; the constraint
  guards every path, including import and future code.
- **Feedback is a `Toast`** on success and `Fail(...)` — a plain sentence, never a stack trace — on
  failure. But a toast is a claim about the database; make sure it is a true one.

## Authorization

Owned by `security-guardrails`; the two rules that decide a CRUD screen:

- **`[Authorize(Policy = Perm.X)]` on the page**, with the same string declared in `ModuleNav.cs`,
  so the sidebar cannot offer what the server refuses.
- **A finer-grained action inside the page gates on `Model.Can("bare.claim.string")`** — the bare
  claim, *not* `Perm.X`, which carries a `perm:` prefix. `Can(Perm.X)` compiles and is silently
  always false. `ViewGuardPermissionTests` forbids it.

Hiding a button is not authorization. The handler enforces; the button is a courtesy.

## Attribution, audit, concurrency

- **Every financial and clinical write is attributed** (§8 N5, ADR-0011) — `ActorId` and
  `ActorName` come from `HmsPageModel`, never from a form field.
- **Tier-1/2 writes append an audit event in the same transaction**:
  `AuditWriter.Append(s.Kernel, BranchId, ActorId, ActorName, action, entity, id, before, after)`.
  Pass `before` on an update — an audit row that records only the new value cannot answer "what did
  it used to say", which is the question an audit is for. Audit is append-only in the database
  (no UPDATE/DELETE grants for the app role, G11).
- **`BranchId` comes from the claim**, via `HmsPageModel.BranchId`. Never a constant, never a
  posted value.
- **If two users can edit the row, it carries an `xmin` row version** — otherwise the second save
  silently discards the first.
- **If the write takes money or creates an order, it carries a `SubmissionToken`** — a double-submit
  or a browser retry over a flaky link (§8 N2) must return the first result, not bill twice.

## Tests — write them first (`tdd-loop`)

| What | Tier |
|---|---|
| Parsers, formatters, pure page-model logic | `tests/Hms.Web.Tests` |
| **The row is actually there after the handler runs** — read back through a fresh context | `tests/Hms.Integration.Tests` |
| The database refuses each bad row the CHECK constraints forbid | `tests/Hms.Integration.Tests` |
| Update preserves what it should and audits `before`→`after` | `tests/Hms.Integration.Tests` |
| Retire hides the row from every list and picker, and no row was deleted | `tests/Hms.Integration.Tests` |
| Concurrent edit: second save is refused, not silently lost | `tests/Hms.Integration.Tests` |
| The screen is behind a permission and the handler enforces it | `tests/Hms.Architecture.Tests` (structural) |
| The operator's real path across screens | `eng/verify/*.py`, using `_harness` |

The one that catches the 0037 class of defect is the read-back. **Assert against the database, not
the handler's return value.**

## Definition of done

- [ ] All five paths exist, or the absence is stated in the spec with a reason
- [ ] Retire is soft — `Active` / reversal / supersede, never a delete
- [ ] Reads are `AsNoTracking`, SQL-side, bounded, and exclude retired rows via the shared helper
- [ ] Every filter and search column is indexed (`schema-and-indexing`)
- [ ] Empty state distinguishes "no matches" from "nothing configured"
- [ ] The page derives from `HmsPageModel` and carries `[Authorize(Policy = Perm.X)]` matching `ModuleNav`
- [ ] In-page action guards use the bare claim, not `Perm.*`
- [ ] Validation is declared with `Bounds`; every row-level rule is also a CHECK constraint
- [ ] The write runs inside one `tx.RunAsync` and its durability is proven by a read-back test
- [ ] Tier-1/2 writes audit with `before` and `after`
- [ ] `xmin` where two users can collide; `SubmissionToken` where money or an order is created
- [ ] Cross-module effects land — see `cross-module-flow`
- [ ] Tests written before the code, at the tiers above, and you saw them fail
