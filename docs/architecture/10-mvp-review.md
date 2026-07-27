# 10 — MVP Architectural Review

- **Status:** Done
- **Date:** 2026-07-27
- **Author:** Inheriting Principal Architect (Phase-2)
- **Spec:** `docs/specs/0014-phase2-review-and-plan/`
- **Brief:** `docs/architect_review_prompt.md` — deliverable A

Every claim below was verified against the code or a fresh verification run on 2026-07-27,
not taken from the handoff summary. Verification baseline: solution builds clean (0 warnings),
**81 .NET tests green** (22 kernel, 18 architecture, 40 integration via Testcontainers, 1 print
golden), **golden-thread and discount-and-dues pass** on a freshly created database, and the
**full 104-test Playwright suite passes**. The system state the handoff describes is real.

---

## 1. Ruling: the shared input layer — build it **now**, before any Phase-2 module

`05-ui-architecture.md` §3 specifies a kernel-level interaction grammar — "one JS module, one
Razor tag helper" per capability (type-ahead, forgiving dates, keyboard grammar, consequence
preview). **It was never built.** Verified:

- `src/Hms.Web/wwwroot/js/typeahead.js` exists and is referenced only by
  `Pages/Shared/_Layout.cshtml`; no page binds it. Patient pickers are `<select>` lists of the
  ~60 most recent patients.
- `Pages/Billing/Dues.cshtml.cs` fetches the newest dues then filters **in memory**
  (`.Where(...)` over materialised rows, `Dues.cshtml.cs:61–66`); `Refund.cshtml.cs:74–79`
  same pattern. `Pages/Registration/Index` and `Pages/Admin/Audit` push the predicate into
  SQL — four screens, two contracts.
- `Pages/Billing/Reports.cshtml.cs:52` reads `From`/`To` only in the `"custom"` branch of the
  period switch — dates chosen under any other period are silently ignored. A wrong money
  number that looks right.
- Registration parses forgiving free-text age/DOB (`New.cshtml.cs`, §7 U13-compliant);
  `/admin/masters` and `/billing/reports` use native `<input type="date">` in browser locale.
  Two date paradigms in one product; U9 broken by construction.

These are four symptoms of one cause. Fourteen modules are about to be built by the same
method that produced the inconsistency at one-third the surface area. Retrofitting a shared
layer across 22 modules later is strictly more expensive than building it before the next
fourteen. **Decision: the input layer is Phase-2 work item zero.** Scope and sequencing in
`11-build-plan-phase2.md` §2; the decision record is ADR-0020.

The specific defects (dues/refund search, report date range) are folded into that work — they
are re-implementations onto the shared contract, not spot fixes.

## 2. ADR compliance — where the implementation drifted

| ADR | Verdict | Evidence |
|---|---|---|
| 0003 modular monolith | **Honoured** | One `DbContext` per schema; `Hms.Architecture.Tests` enforces reference rules; cross-module reads go through contracts projects. |
| 0004 numbering | **Honoured** | `NumberSeriesService` allocates under row lock; gap-free invoice numbers asserted by integration tests. |
| 0007 multi-branch | **Accommodated, unexercised** | `BranchId` on every row, but `Pages/HmsPageModel.cs:14` is `public const long BranchId = 1`. Acceptable for now; noted as debt item 10 below. |
| 0009 printing (QuestPDF) | **Drifted** | `Hms.Kernel/Printing/PdfRenderer.cs` (QuestPDF) has **no consumers** outside the Bangla shaping spike. Every document is browser print-to-PDF. The ADR's server-rendered path exists only as a proof. Consequence: no headless/batch document generation (needed by M18 corporate statements, M17 payout advices). Carry until M18/M17; then wire documents through the renderer rather than re-deciding. |
| 0011 audit | **Honoured** | Append-only `kernel.audit`; no UPDATE/DELETE grant to the app role; writes attributable (user + time). |
| 0015 concurrency | **Honoured** | Verified in `BillingService.cs`: due row `SELECT … FOR UPDATE` (:176, :243), session state `FOR UPDATE` (:186, :255), `one_open_session` uniqueness surfaced as a plain operator message (:46–52). |
| 0019 auth hardening | **Partial** | Cookie + 15-min idle honoured; but permissions are stamped at sign-in via `PermissionClaimsFactory` (`Program.cs:49`) with **no security-stamp revalidation**, so a revoked grant lives until next sign-in. See ruling in §4. |
| 05 §3 interaction grammar | **Not built** | §1 above. This is the largest drift and the one with compounding cost. |

## 3. The money spine under concurrency — would I run a hospital's cash on it?

**Yes.** The load-bearing invariants are in the database, where they belong:

- Invoice identity CHECK and `ck_charge_parent num_nonnulls(encounter_id, folio_id) = 1`
  (`20260726125007_InitBill.cs:46`) — a charge line has exactly one parent, enforced by
  Postgres, not by discipline.
- Gap-free numbering allocates under row lock inside the same transaction as the invoice
  (`G19`: one connection, one ambient transaction spanning `BillDbContext` + `KernelDbContext`
  — `BillingService` header comment, enforced by `HmsTx.RunAsync`).
- Over-collection is impossible under parallelism: the due row is locked `FOR UPDATE` before
  balance arithmetic; the discount-and-dues script proves the rejection path end-to-end.
- Money is integer taka throughout (`Amount = Qty * UnitPrice`, `BillingService.cs:77`) — no
  floating point anywhere in the money path.
- No DELETE grant for the app role; corrections are reversals.

Caveats that keep this a qualified yes: (a) **no upgrade-path test** — the invariants are
proven on freshly created schemas only, and a schema-upgrade defect already reached production
once (spec 0013 notes); (b) the cross-context query guard is a regex over LINQ *query syntax*
(`CrossContextQueryTests.cs:30`) and will not catch method-chain `Join(...)` — the failure mode
it guards is a runtime 500 in a money screen. Both are ranked in §5 and scheduled ahead of any
new module.

## 4. Rulings on the deliberate decisions the handoff asked me to challenge

1. **Refund executes at the counter, not in the approver's inbox — upheld.** Approval is
   authorisation; execution is cash custody. The person with the open drawer must be the one
   the cash movement is attributed to, or day-close variance stops meaning anything. The audit
   trail binds both actors. No change.
2. **Payment-in-full as the sole lab-release trigger — upheld until M18.** A part-paid order
   raising no sample is the correct default for a cash market; the "held — due" path becomes
   reachable when corporate credit (M18) introduces legitimately part-paid orders. The M18 spec
   must revisit this trigger explicitly — recorded as a plan-level obligation, not left to memory.
3. **Cookie-stamped permissions — not acceptable for a hospital with shift handover, fix now.**
   A revoked grant surviving until voluntary re-login is wrong where a supervisor grant is
   pulled mid-shift. The fix is cheap and framework-native: Identity security-stamp validation
   on an interval (≤5 min) so revocation invalidates the principal without changing the
   permission model. Scheduled in Wave 0; amendment recorded on ADR-0019.
4. **Demo seeding on in production with a shared password — acceptable this week, but the
   go-live switch must exist in writing before any real patient record.** A runbook section
   (disable `HMS_SEED`, rotate all demo credentials, verify no demo user can authenticate) is a
   Wave-0 deliverable. Not code — procedure.

## 5. The folio seam — proven

Claim (§9 binding rule): M6 lands without migration pain. Verified against
`03-data-model.md` §4 and the shipped schema:

- `bill.charge_line.folio_id` exists (nullable) with the XOR CHECK against `encounter_id` from
  the **initial** migration — every historical row satisfies it (`InitBill.cs:27,46`).
- `doctor_id` and `referrer_id` are on the line (`BillDbContext.cs:73–74`) for M17/M19 payout
  attribution — accrual computation is a read model over existing data, not a schema change.
- M6 therefore adds: `ipd` schema (folio, admission, bed tables), posting code that sets
  `FolioId` instead of `EncounterId`, and settlement invoicing over folio lines. **Additive
  only.** The one structural gap: nothing yet *proves* the folio-parented path (no service
  posts one). The M6 spec's first test must post a folio line through `PostChargeAsync` before
  any screen exists — prove the seam, then build on it.

## 6. Debt ranked by Phase-2 pain — and what gets fixed before building anything new

| # | Debt | Phase-2 consequence if carried | Disposition |
|---|---|---|---|
| 1 | **No upgrade-path test** | Every module adds migrations; each deploy repeats the reference-band failure mode against live data. The one defect that reached production came exactly this way. | **Fix first** (Wave 0): boot previous-release schema + seed, migrate, run golden thread. ADR-0022. |
| 2 | **No shared input layer** | Fourteen modules hand-roll inputs; the U9/U13 breakage becomes product-wide and unretrofittable. | **Fix first** (Wave 0): §1 ruling. ADR-0020. |
| 3 | **Search over pre-truncated pages / no type-ahead** | Correctness bug at §14 volumes — unpaid invoices reported "not found". | Subsumed by #2: dues, refund, patient pickers move onto the shared search/type-ahead contract. |
| 4 | **Regex cross-context guard** | Each module multiplies cross-schema read pairs; a missed method-chain join is a runtime 500. | Wave 0: replace with a Roslyn-based semantic check over compiled pages. |
| 5 | **Cookie permission staleness** | Shift-handover revocation gap widens as roles multiply (pharmacy, wards, stores). | Wave 0: security-stamp revalidation ≤5 min (§4.3). |
| 6 | **Browser-only PDF** | M17/M18 need server-side batch documents (statements, payout advices). | Carry until M17/M18; wire through the existing `PdfRenderer` then. |
| 7 | **Seed-on in prod, shared demo password** | Fatal the day one real patient exists. | Wave 0 runbook procedure (§4.4); code switch already exists. |
| 8 | **Memory budget still estimated** | Fourteen more `DbContext`-bearing modules on a 3 GB VM; finding out at module ten is too late. | Measure at each wave's deploy: record RSS under the Playwright suite in each spec's notes; abort criterion defined in the build plan. |
| 9 | **No 90-day seed history** (spec 0010) | Dashboard/demo credibility; also blocks #8's measured load. | Fold into Wave 0 as the load generator for the memory measurement. |
| 10 | **`BranchId = 1` const** | ADR-0007 answered multi-branch in principle; nothing exercises it. | Carry. Revisit when a second branch is a sales reality; the schema already carries `branch_id`. |

## 7. Summary

The MVP is architecturally sound: the module boundary is real and machine-enforced, the money
spine holds its invariants in Postgres under concurrency, the folio seam §9 demanded is
genuinely there, and the verification harness is unusually honest for a codebase this young.
The debt is concentrated and known — almost all of it in the seams the next phase stresses
(schema upgrades, input consistency, cross-schema reads). Wave 0 of the build plan clears
items 1, 2, 4, 5, 7, 9 before the first new module; nothing in the review blocks Pharmacy
starting immediately after.
