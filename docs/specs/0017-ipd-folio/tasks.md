# 0017 — Tasks

- [x] Spec + matrix + P18 appended to `09-questions-for-pm.md`; index row added
- [x] Bill migration: invoice/receipt folio parents (XOR), day-close `advances_taken`
- [x] BillingService: PostFolioChargeAsync / CollectAdvanceAsync (negative = excess return) /
      AdvanceHeldAsync / CreateFolioInvoiceAsync; DayCloseService advances figure
- [x] Diag migration: test_order folio parent (XOR); CreateFolioOrderAsync (born In-Progress)
- [x] Hms.Ipd project: entities + InitIpd migration + IpdService + FolioService + CertificateService
- [x] Composition root: IpdBilling (bed-day catch-up, settlement, indents, indoor orders,
      R4 OPD guard); TxScope.Ipd; DI + MigrateAsync; pharm IssueAllocation + RestockIndentAsync
- [x] Screens ×8 (/ipd/board, admit, admissions, folio, discharge, certificates, reports,
      /pharmacy/indents) + nav + Perm entries; DevSeed (wards/beds/tariffs/package/nasrin/policies,
      all additive)
- [x] 10 integration tests green (seam proof first, bed concurrency, idempotent bed days,
      settlement math, advances + day-close reconcile, R4 + bogus-approval refusal, late-post ⚿,
      §11 machine ordering, indoor return restock, certificate series)
- [x] Playwright: nasrin + 7 routes + 3 denied pairs + spec-0017.spec.ts (8 tests) — suite 166 green
- [x] ipd-thread.py green on fresh AND dirty DB; golden/discount/pharmacy threads still green
- [x] Upgrade gate green (current build over previous-release dump, ipd-thread included) +
      full .NET suite (103) + 4 CI greps; spec closed
