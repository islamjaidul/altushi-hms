# 0030 — Tasks

## F2 — `appointments.create`

- [x] Failing test first: both handlers reachable with `appointments.read` alone
- [x] `Pages/Appointments/Index.cshtml.cs` — `CanIssue`, checked in `OnPostIssueAsync` and
      `OnPostAdvanceAsync`
- [x] `KNOWN_UNENFORCED` emptied in `eng/check-lifecycle-traceability.sh`
- [x] `role-journeys.py` — LC-QUE-08 handler probe
- [x] Audit of permissions enforced only by a page policy → `notes.md`

## F1 — grant drift

- [x] `eng/verify/grant-drift.py` — code matrix vs deployment matrix, both directions, read-only
- [x] `--fix` revokes extras through `/admin/users?handler=Permission` (audited, stamp-bumping),
      behind the `guard()` interlock
- [x] Added to tier t0 in `lifecycle-suite.py`
- [x] `ADR-0023` — report, do not reconcile
- [x] Production drift corrected and re-verified

## Document

- [x] `LC-QUE-08` and `LC-XCUT-14` flipped from `gap` in `docs/qa/patient-lifecycle.md`
- [x] Both removed from the gap register
