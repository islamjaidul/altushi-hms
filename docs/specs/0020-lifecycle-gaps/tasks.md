# 0020 — Tasks

- [x] Lifecycle smoke walk of one patient across every built module; three defects confirmed
- [x] Spec + plan archived; index row; P20 appended to PM questions
- [x] Gap 1: `reg.patient.phone_digits` generated column + index (`PhoneDigits` migration);
      `PatientSearch.Matching/Searchable` helper; type-ahead, registration directory, dues
      and refund all search on it
- [x] Gap 2: `DischargeAsync(…, outstanding, reason)` refuses a silent release and audits at
      tier 2; discharge screen lists every unpaid invoice (this admission's and the others')
      and hides the one-click button while money is owed
- [x] Gap 3: `IpdBilling.FindOpenAdmissionAsync` + banners on `/pharmacy/pos` and `/billing/opd`
- [x] 8 integration tests (`LifecycleGapTests`) — phone forms, generated-column maintenance,
      refusal, permitted path + audit tier, clean one-click path
- [x] `eng/verify/lifecycle-thread.py` (9 steps, 7 roles) + `spec-0020.spec.ts` (4 tests,
      self-provisioning) + upgrade gate wired
- [x] Harness robustness: every dirty-DB script now survives repeated runs (per-run patients,
      duplicate-guard acknowledgement, bed housekeeping, property-based FEFO assertion)
- [x] Full verification green: 111 .NET tests · 6 scripts · 177 Playwright · upgrade gate ·
      4 CI greps; spec closed
