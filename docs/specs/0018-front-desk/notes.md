# 0018 — Notes (afterwards)

- **Plan deviation:** the estimate-math proof shipped as `eng/verify/frontdesk-check.py`
  (end-to-end over real HTTP, wired into the upgrade gate) instead of the planned
  `FrontDeskEstimateTests` integration test — the estimate lives in the page model's
  composition, and the integration-test project deliberately does not reference `Hms.Web`.
  The script asserts the same equation (posted + accrued − advances) plus the read-only
  property, against seeded constant rates, and cleans up after itself (absconds + frees the bed).
- The accrued figure reuses `FolioService.ComputeUnpostedBedDaysAsync` + `RateResolver`
  without ever calling `PostFolioChargeAsync` — one source of truth for the P18 rule, two
  consumers (posting and estimating).
- Reservation deposits stayed out per P19; the recommended default (reservations hold no
  money) is what the code does by construction.
