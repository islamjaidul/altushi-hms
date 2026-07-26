# 0003 — Tasks

- [x] Archive design reference to `docs/architecture/assets/altushi-hms-demo.html`
- [x] `00-architecture-overview.md`
- [x] `01-adr/` — ADR-0001 stack · ADR-0002 database · ADR-0003 application architecture · ADR-0004 numbering/IDs · ADR-0005 deployment model (Q1) · ADR-0006 offline (Q2) · ADR-0007 multi-branch/tenancy (Q3) · ADR-0008 device integration (Q4) · ADR-0009 printing/reporting (Q5) · ADR-0010 migration tooling (Q6) · ADR-0011 audit (Q7) · ADR-0012 portal readiness (Q8) · ADR-0013 backup/DR (Q9) · ADR-0014 Bangla text (Q10) · ADR-0015 concurrency (Q11) · ADR-0016 licensing/entitlements (Q12) · ADR-0017 BEFTN readiness (Q13) · ADR-0018 TDS/VAT placement (Q14) · ADR-0019 auth hardening (Q15)
- [x] `02-domain-model.md`
- [x] `03-data-model.md`
- [x] `04-api-and-module-boundaries.md`
- [x] `05-ui-architecture.md` (bound to Altushi design reference)
- [x] `06-deployment.md` (memory budget table, capacity ceiling, laptop demo)
- [x] `07-demo-kit.md`
- [x] `08-build-plan.md`
- [x] `09-questions-for-pm.md`
- [x] Verification: Q1–Q15 → ADR sweep · 34 edge-case sweep · § citation check · spec-auditor run
- [x] Close spec (Done) + update index

## Edge-case coverage (brief items 1–34 → where discharged)

| # | Edge case | Doc |
|---|---|---|
| 1–10 | Demo-day failure modes | 06, 07 (05 for PDF fallback, 04 for SMS simulation) |
| 11–14 | Construction-phase config | 02, 03 (placeholders, import, effective dates, bed states) |
| 15–16 | Numbering, business-day boundary | 03, ADR-0004 |
| 17–20 | Day-close discipline, delegation, post-close refund | 02, 04 |
| 21–23 | Cancel-after-collection, amended report, patient merge | 02 |
| 24–26 | No phone, unknown patient, age⇄DOB | 02, 05 |
| 27 | Label reprint | 02, 05 |
| 28 | Concurrent billing/serials | ADR-0015 |
| 29 | Shared logins | ADR-0019 |
| 30 | Rounding | 03 |
| 31 | Clock drift | 06 |
| 32 | Disk full | 06 |
| 33 | Sample↔test multiplicity | 02, 03 |
| 34 | Reporting-consultant verification | 02 |
