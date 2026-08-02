# 0037 — Tasks

- [x] QA sweep of the HRM SKU — every screen, child page, button and search input, driven over
      HTTP and checked against the database (`eng/verify/hrm-thread.py`, new)
- [x] R1 — `HrTx.RunAsync` flushes its three contexts before commit
- [x] R1 — `HmsTx`/`TxScope.SaveAllAsync` does the same for the ERP host's fourteen
- [x] R8 — `TransactionDurabilityTests` (3 cases: insert, mutate, rollback). Seen to fail on the
      pre-fix tree — 2 of 3 red, the row absent after commit
- [x] R4 — blank employee name refused instead of a `NullReferenceException`
- [x] R5 — user creation checks the role first and deletes the account if the grant still fails
- [x] R4 — masters rename/retire resolve through one branch-scoped lookup; a stale tab gets a
      message, not `SingleAsync`'s unhandled throw
- [x] R4 — payroll policy: catch `HrException`, amend today's policy in place rather than raising,
      and refuse a minimum net pay that would not bind
- [x] Payroll policy screen loads what is in force into its own form (it rendered 0 every visit,
      so pressing Save reset the minimum to zero)
- [x] R6 — roster loads before assigning: real week, one header per week, week and unit ride the
      post as hidden fields, and the board states "showing 60 of N"
- [x] R7 — the notifications bell is gated on `notifications.read`, like the search beside it
- [x] Fixed `BanglaSpikeTests`' font path — red on `main` since spec 0035 moved the assets
- [x] Fixed `role-journeys.py`'s grants table — MD's and the Pharmacist's real HR claims were
      missing, so every run reported a permission leak that did not exist

## Verification

- [x] `eng/verify/hrm-thread.py` — 37 cases, 0 failed, fresh local database
- [x] `dotnet test hms-erp.slnx` — 343 tests, 0 failed (was 342 + 1 red)
- [x] ERP `lifecycle-suite.py --tier all` — 14 scripts green, 12/12 roles, on a fresh database.
      This is the evidence for the risk the plan recorded: the flush is a no-op on the ERP paths.
- [x] Guards: css-classes, ui-tokens, icon-glyphs, no-native-date, fkeys, no-external-hosts,
      lifecycle-traceability
- [ ] Deployed to `hrm.specshipper.com` and re-QA'd against the deployment
