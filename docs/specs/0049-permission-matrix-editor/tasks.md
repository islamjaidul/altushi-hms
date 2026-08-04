# 0049 — Tasks

- [ ] Matrix → single form with checkboxes + sticky Save
- [ ] `OnPostMatrixAsync`: token validation, per-role diff, audit per change, lockout guard
- [ ] Stamp-bump per affected role + `RefreshSignInAsync(actor)`
- [ ] Keep `OnPostPermissionAsync` verbatim; heading "Role permissions" intact
- [ ] `_harness.grant_cells` regex → checkbox markup
- [ ] `Auth:RevalidationMinutes` = 1 (dev + compose)
- [ ] Verify: row-level persistence assertion, LC-ROLE-14, grant-drift
