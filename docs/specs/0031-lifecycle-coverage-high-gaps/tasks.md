# 0031 — Tasks

## F5 — routes never loaded

- [x] Eleven routes added to `ROUTES` in `eng/verify/ui/helpers/users.ts`, permission and title
      read from each page's `@page` directive and `[Authorize]` attribute
- [x] `LC-XCUT-13` flipped from `gap`

## F7 — the High register

- [x] `eng/verify/money-and-controls.py` on `_harness.py`, tier t1
- [x] LC-BIL-11 — historical invoice survives a reprice (`admin` drives a thread at last)
- [x] LC-BIL-10 — cancellation is a reversal, nothing deleted
- [x] LC-DX-03 — partial payment does not release the lab
- [x] LC-BLK-03 — block and release both refused without an approval
- [x] LC-DIS-04 — discharge with a due: typed reason + tier-2 audit
- [x] LC-DIS-07 — settlement reopen is approval-gated
- [x] LC-LAB-08 — amend after verification versions the result
- [x] LC-ROLE-14 — revocation bites mid-session; the grant is restored in teardown
- [x] `tests/Hms.Integration.Tests/ConcurrencyTests.cs` — LC-XCUT-09, LC-XCUT-10 on real Postgres
- [x] `eng/verify/load-probe.py` — first-cut concurrency probe, stdlib only, wired into no tier
- [x] LC-XCUT-11 raised with the architect rather than answered

## Document

- [x] Every covered case's marker flipped in `docs/qa/patient-lifecycle.md`
- [x] Gap register reduced; remaining High gaps each carry a reason
