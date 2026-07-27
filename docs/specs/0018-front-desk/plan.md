# 0018 — Plan

## Approved: 2026-07-27

| # | Requirement | Surface | Proof |
|---|---|---|---|
| 1 | Admitted patient enquiry [M] | `/frontdesk` typeahead → admission card (bed, doctor, dates) | Playwright + thread step |
| 2 | Bed board [M] | `/ipd/board` (0017) linked; occupancy summary repeated on `/frontdesk` | already covered (0017) |
| 3 | Live bill estimate [M] (US2.2) | read-only computation: unbilled folio lines + accrued unposted bed days (rate-resolved, **not posted**) − advances | integration test `FrontDeskEstimateTests` |
| 4 | Appointment details [M] | today's doctors + serial counts from `appt` | Playwright |
| 5 | Visit history summary [S] | recent encounters + past admissions on the enquiry card | Playwright |
| 6 | Reservation advance [S] | **deferred** — P19 | — |

Technical: one page `/frontdesk` (`Perm.IpdRead`), read-only over Reg/Ipd/Bill/Appt/Adm via
TxScope; estimate reuses `FolioService.ComputeUnpostedBedDaysAsync` + `RateResolver` without
calling `PostFolioChargeAsync`. Nav entry under "Front Desk" for `ipd.read`. No new tables,
no migrations, no new permissions.
