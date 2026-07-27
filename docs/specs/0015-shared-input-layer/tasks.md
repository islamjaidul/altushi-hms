# 0015 — Tasks
- [x] Kernel FlexibleDate + ParseAge delegation (`Hms.Kernel/Time/FlexibleDate.cs`)
- [x] hms-date tag helper + JS; masters + reports converted
- [x] Dues/Refund predicates into SQL (patient ids via reg, invoice ILIKE in bill)
- [x] /api/typeahead/patients + typeahead.js data-submit + 3 pickers replaced
- [x] Reports: dates imply custom; period select clears dates then applies
- [x] Security-stamp revalidation (5 min) + stamp bumps on permission/role/deactivate
- [x] Cross-context guard: method-chain joins (arch test #19)
- [x] Upgrade gate: fixture + run.sh + CI job (`eng/verify/upgrade/`)
- [x] check-no-native-date.sh + ci.yml guard step
- [x] RUNBOOK §9 go-live switch
- [x] Verification green: 82 .NET tests · golden-thread · discount-and-dues · 113 Playwright
      (9 new contract tests in `spec-0015.spec.ts`) · upgrade gate end-to-end · all 4 CI greps
