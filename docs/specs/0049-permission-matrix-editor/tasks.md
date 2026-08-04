# 0049 — Tasks

- [x] Matrix → single form with checkboxes + sticky Save
- [x] `OnPostMatrixAsync`: token validation, per-role diff, audit per change, lockout guard
- [x] Stamp-bump per affected role + `RefreshSignInAsync(actor)`
- [x] Keep `OnPostPermissionAsync` verbatim; heading "Role permissions" intact
- [x] `_harness.grant_cells` regex → checkbox markup
- [x] `Auth:RevalidationMinutes` = 1 (dev + compose)
- [x] Verify: row-level persistence assertion, LC-ROLE-14, grant-drift
