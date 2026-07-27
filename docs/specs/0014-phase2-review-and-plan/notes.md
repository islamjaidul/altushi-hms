# 0014 — Notes

- **Verification deviation:** the standard reset (`DROP DATABASE hms`) was not run in this
  session; instead a parallel fresh database `hms_verify` was created and the app pointed at
  it via `ConnectionStrings__Hms`. Equivalent for harness purposes (all scripts and the
  Playwright suite ran green against it); `hms_verify` can be dropped at leisure.
- **Environment finding:** a stale `Hms.Web` process from a previous session was holding
  port 5199 and connected to the old `hms` DB — the first golden-thread run failed one
  dashboard check against it. Killed, rerun clean. Symptom worth remembering: a
  half-passing golden thread can mean "wrong app instance", not "broken app".
- Every claim in `10-mvp-review.md` carries a file:line or a fresh verification result —
  acceptance criterion 2 met by construction.
- Follow-ups: Wave-0 spec (input layer + safety rails) next, then M11 Pharmacy with
  ADR-0021. P13–P16 await PM answers; none block Wave 0 or Wave 1.
