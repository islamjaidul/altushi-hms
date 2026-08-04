# 0047 — Plan

## Approved: 2026-08-04

(Section of the approved demo-day plan; shared checkpoint/verify/deploy steps apply.)

- **New print page** `src/Hms.Web/Pages/Ipd/Certificate.cshtml(.cs)`: `@page "/ipd/certificates/{id:long}"`, `[Authorize(Policy = Perm.IpdSettle)]`, deserializes frozen `Body` into the established sheet pattern — `_PrintTools` (back → /ipd/certificates; "the screen IS the preview") → `.sheet` → `_Letterhead` → `.sheet-info` (patient/UHID/admission no/admitted/closed) → summary + extra paragraphs → `.sheet-signs` → `_SheetFooter` + CertNo/print-count. Model on Registration/Card.cshtml / Lis/Report.cshtml.
- Issue + Reprint handlers now **redirect to the print page** (reprint keeps counting + auditing); list rows get View/Print links.
- **Editable pre-issue**: `ClinicalSummary` textarea (prefilled from admission) + optional follow-up date composed into the body json; post-issue stays frozen (legal document, by design).
- **Fix the state trap**: filter the admission dropdown per kind server-side (discharge → FinanciallySettled/Discharged; death → Death) and validate before `IssueAsync` — no red-error surprise mid-demo.
- ROUTES += `"/ipd/certificates/1": "ipd.settle"`. No service change (`Body` stays caller-composed, frozen; body shape `{patient, uhid, admissionNo, admitted, closed, summary, extra, issuedOn}` per Certificates.cshtml.cs:83-94).
