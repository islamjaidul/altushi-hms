# 0042 — Tasks

- [x] Spec artifacts + README index row
- [x] Schema: `ipd.consultant_visit` (unique AdmissionId+DoctorId+OnDate) — migration `ConsultantVisit`
- [x] Schema: `reg.patient.allergies` — migration `Allergies`
- [x] Tests first: `WardMoneySeamTests.cs` (9: visit auto-post ×2, indoor lab seam ×2, indent issue, service post, write guards, discharge/death survival) + branch-scope test in `NursingStationTests` — **red run captured** ("No exception was thrown"), green after the fix
- [x] Logic: `AdministerAsync` `AND branch_id` predicate; `EmrOrdering` `InvalidOperationException` → `EmrException`; `WardGuard.RequireAsync`/`RequireLiveAsync`; `IpdBilling.PostConsultantVisitAsync` (claim-first idempotency, blocked-folio = visit without charge)
- [x] `/emr/indoor`: WardGuard on writes, visit auto-post on sign, product typeahead + formulary validation, banner
- [x] `/emr/charts` + `/emr/tasks`: WardGuard on POSTs (live for new work, existing for close-out), read-only terminal mode, banner, NRE fixes
- [x] `/ipd/discharge`: "ward still holds open items" clearance warning (informational, money gate unchanged)
- [x] `/ipd/folio`: death/abscond toasts name remaining open work; "raise indent from prescription" strip + handler
- [x] `/diagnostics/order`: `FindOpenAdmissionAsync` + `EnsureNotBlockedAsync`; folio branch (no invoice, no cash) + admitted banner
- [x] `_PatientBanner` partial (UHID · age/sex · blood group · allergy · diagnosis) on Charts/Tasks/Indoor; Station tile allergy + diagnosis; Registration allergies input through `RegisterPatientCommand`
- [x] `/emr/history`: Charts link per past admission (the read-only access route)
- [x] `nursing-thread.py` LC-NUR-14…18; lifecycle doc rows; gap register: 4 new rows for the recorded-not-built findings
- [x] Verification: 498 tests, 8 guards, nursing-thread ×2, role-journeys, full t1 suite — green (`notes.md` §4)
- [x] Close-out: Status Done, deviations in `notes.md`
- [x] **Deploy** — ERP image rebuilt on the VM 2026-08-03 (commit 6da0ee5, with 0041); all four migrations applied at boot; nursing-thread 13/13 green against hms.specshipper.com (`notes.md` §6)
