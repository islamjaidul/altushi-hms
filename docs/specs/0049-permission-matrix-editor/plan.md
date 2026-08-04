# 0049 — Plan

## Approved: 2026-08-04

(Section of the approved demo-day plan; shared checkpoint/verify/deploy steps apply.)

- **Users.cshtml matrix** (:171-222): one `<form asp-page-handler="Matrix">` around the table; each cell → `<input type="checkbox" name="Grants" value="@r.Id:@perm.Claim" data-role="@r.Name" checked?>`; one sticky **Save changes** button. Keep the literal heading "Role permissions" (grant-drift hard-exits without it).
- **Users.cshtml.cs**: new `OnPostMatrixAsync(List<string> Grants)` — validate tokens against `catalog.Claims`, diff posted-vs-held per role in one `tx.RunAsync`, audit `role.grant`/`role.revoke` per change, **lockout guard** (refuse removing `admin.users.manage` from its last holding role), stamp-bump once per affected role, then `RefreshSignInAsync(actor)` so the admin isn't signed out and sees their own sidebar update instantly. Toast: "Saved N change(s) — others pick this up within a minute or at next sign-in." **Keep `OnPostPermissionAsync` verbatim** (LC-ROLE-14 + grant-drift --fix post it directly).
- **_harness.py grant_cells** (:274-290): rewrite regex to the checkbox markup (`name="Grants" value="(\d+):([a-z][a-z.]+)" data-role="([^"]+)"( checked)?`), same return tuple → grant-drift + LC-ROLE-14 keep working untouched.
- `Auth:RevalidationMinutes: 1` in appsettings.Development.json + deploy compose for snappy propagation.
