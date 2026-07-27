# 0020 — Plan

## Approved: 2026-07-27

## 1. Gap → fix → proof

| # | Gap | Fix | Proof |
|---|---|---|---|
| 1 | Phone search fails for digit-typed numbers | `reg.patient.phone_digits` **generated column** `GENERATED ALWAYS AS (regexp_replace(coalesce(phone,''), '\D', '', 'g')) STORED` + index; `PatientSearch.DigitsOf(term)` helper; typeahead + registration + dues + refund predicates add `phone_digits ILIKE %digits%` (SQL-side) | integration test over real Postgres (stored dashed → found by digits, +88 prefix, partial); thread step |
| 2 | Silent discharge with money owed | `IpdService.DischargeAsync(…, long outstanding, string? reason)`: refuses when `outstanding > 0 && reason` blank; audit tier 2 carries amount + reason. Discharge page computes the settlement due **and every other unpaid invoice** for the patient, renders the total, and swaps the plain button for a reason-required one | integration test (refusal + permitted path + audit); Playwright (button absent while owing); thread step |
| 3 | Outdoor charge for an admitted patient is invisible | `IpdBilling.FindOpenAdmissionAsync(s, patientId)` → banner on `/pharmacy/pos` and `/billing/opd` with bed + folio link | Playwright + thread step |
| 4 | No cross-module lifecycle regression | `eng/verify/lifecycle-thread.py` (7 roles, register→…→return visit), added to the upgrade gate | runs green fresh + dirty |

## 2. Notes on the chosen implementations

- **Generated column, not app-side normalisation.** A second write path can be forgotten;
  a generated column cannot. `regexp_replace` is immutable, so Postgres accepts it in a
  `STORED` generated column, computes it for every existing row at migration time, and keeps
  it exact forever. EF maps it read-only (`ValueGeneratedOnAddOrUpdate`, never written).
- **Search stays sargable**: the digits predicate only runs when the typed term contains a
  digit run of ≥4, so name searches are unaffected.
- **Discharge outstanding** = settlement invoice balance + the sum of every other `bill.due`
  row with `balance > 0` for that patient (cross-context: ids from `bill.invoice` by
  `patient_id`, joined in memory per ADR-0003).
- **Reason, not approval** (P20 default): the counter must not stall at the gate. The reason
  is attributable and lands in the tier-2 audit stream the MD dashboard already surfaces.
- **Banner, not a block** for gap 3 — the attendant case is legitimate (spec §out-of-scope).

## 3. Files

`RegDbContext` + migration `PhoneDigits` · `src/Hms.Web/PatientSearch.cs` (new helper) ·
`Program.cs` typeahead · `Pages/Registration/Index.cshtml.cs` · `Pages/Billing/Dues|Refund` ·
`Hms.Ipd/IpdService.cs` · `Pages/Ipd/Discharge.cshtml(.cs)` · `IpdBilling.cs` ·
`Pages/Pharmacy/Pos.cshtml(.cs)` · `Pages/Billing/Opd.cshtml(.cs)` ·
`tests/Hms.Integration.Tests/LifecycleGapTests.cs` · `eng/verify/lifecycle-thread.py` ·
`eng/verify/ui/tests/spec-0020.spec.ts` · `eng/verify/upgrade/run.sh`
