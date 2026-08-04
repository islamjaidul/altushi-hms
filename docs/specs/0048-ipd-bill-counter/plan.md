# 0048 — Plan

## Approved: 2026-08-04

(Section of the approved demo-day plan; shared checkpoint/verify/deploy steps apply.)

- **Seed** (additive): `Counter { Name = "IPD Billing Counter", Kind = "ipd" }` when no ipd-kind counter exists.
- **CounterContext.cs**: kind-aware resolution — required-kind variant (`"ipd"`) and an exclude-ipd variant for OPD surfaces; fix `EncounterType` so an ipd session never mints encounters. (One operator can hold both sessions — unique-open is per counter.)
- **Enforcement**: Discharge.cshtml.cs `LoadAsync`/`OnPostConfirmAsync` resolve the **ipd** session ("Open the IPD billing counter before settling"); Opd.cshtml.cs resolves non-ipd and blocks invoice creation from an ipd-only session ("This counter handles IPD only"). Dues/Refund stay kind-agnostic (IPD counter may collect IPD dues).
- **New page** `src/Hms.Web/Pages/Billing/Ipd.cshtml(.cs)`: `@page "/billing/ipd"`, `[Authorize(Policy = Perm.IpdSettle)]` — IPD cashier workspace: ipd-session banner, settlement queue (discharge-pipeline admissions with folio gross / advances / balance; per-context queries joined in memory), links to /ipd/folio/{id} + /ipd/discharge/{id}, today's IPD invoices. Nav: `new("Billing", "IPD Billing", "/billing/ipd", "ipd.settle", "receipt_long", "Billing & Cash")`. ROUTES += `"/billing/ipd": "ipd.settle"`.
- **No new Perm constant, no new role** — reuse `ipd.settle` everywhere (avoids ROLE_GRANTS/grant-drift/traceability churn entirely).
- **Verify-script sweep (one commit, shared helper)**: add `_harness.ensure_ipd_counter(sess)` and call it in `settle_and_discharge` (_harness.py:413) + before each direct Prepare/Confirm block: money-and-controls.py ~:288/:316, ipd-thread.py ~:249, lifecycle-thread.py ~:280/:367, edge-cases.py ~:107/:263.
