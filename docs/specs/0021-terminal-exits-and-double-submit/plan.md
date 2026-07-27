# 0021 — Plan

## Approved: 2026-07-27

| # | Gap | Fix | Proof |
|---|---|---|---|
| 1 | Death → bill unbillable | `IpdBilling.PrepareSettlementAsync` accepts `ClinicallyCleared`, `Death`, `Absconded`; `ConfirmSettlementAsync` calls `MarkFinanciallySettledAsync` **only** from `ClinicallyCleared`, so a terminal clinical state is never overwritten by a money step | integration: settle-after-death keeps state `Death`, invoice+due exist |
| 2 | Absconded → no due to follow up | same path; the settlement invoice's due lands on `/billing/dues` unchanged | integration + thread step |
| 3 | Double submit → second invoice | `bill.invoice.submission_token uuid` + **unique partial index**; `BillingService.FindBySubmissionAsync`; both invoice creators accept the token; the four issuing pages mint one on GET, carry it through postbacks, and resolve a repeat to the existing invoice (also catching the unique violation, so two concurrent posts resolve to one) | integration: sequential repeat + **concurrent** double-post both yield one invoice; thread step; Playwright |

## Implementation notes

- **Terminal-exit settlement.** The discharge screen gains a "close the bill" branch for
  `Death`/`Absconded` admissions: prepare → confirm, no gate pass (they have already left).
  Certificates are untouched. Wording matters: the panel says *close the bill*, never
  *discharge*, so the screen never claims a dead patient walked out.
- **Idempotency is a constraint, not a check.** The check (`FindBySubmissionAsync`) makes the
  common case pleasant — the operator lands on their invoice. The unique index is what makes
  it *true* under concurrency: the loser of a race catches `DbUpdateException`, re-reads by
  token, and redirects to the winner's invoice. Same discipline as ADR-0015.
- **Token lifecycle.** Minted per page render (`Guid.NewGuid()`), carried as a hidden field
  through cart postbacks (Add/Remove re-emit the same value), consumed at save. A new visit to
  the screen mints a new token, so a *deliberate* second identical bill is still possible.
- Diagnostics ordering short-circuits **before** creating the order, so a repeat cannot leave
  an orphan test order behind.

## Files

`bill/Data/BillDbContext.cs` + `SubmissionToken` migration · `Hms.Billing/BillingService.cs`
(`FindBySubmissionAsync`, both creators) · `Pages/Billing/Opd.cshtml(.cs)` ·
`Pages/Diagnostics/Order.cshtml(.cs)` · `Pages/Pharmacy/Pos.cshtml(.cs)` ·
`Hms.Web/PharmacySale.cs` · `Hms.Web/IpdBilling.cs` · `Pages/Ipd/Discharge.cshtml(.cs)` ·
`tests/Hms.Integration.Tests/TerminalExitTests.cs` · `eng/verify/lifecycle-thread.py` ·
`eng/verify/ui/tests/spec-0021.spec.ts`
