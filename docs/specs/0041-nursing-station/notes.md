# 0041 — Notes

Deviations from `plan.md` and what caused them. The plan is the record of what was agreed and
is not rewritten; this file is what actually happened.

## 1. The plan's biggest miss: indoor prescribing had no screen at all

The plan's whole first pillar — "generate the Medicine Chart from the finalised indoor
prescription" — assumed an indoor prescription could exist. It could not.

`emr.note` has carried a nullable `AdmissionId` since spec 0024 and `ck_note_parent` admits it,
but the only writer is `Pages/Emr/Consult.cshtml.cs`, which is keyed to an outdoor
`EncounterId` and passes `admissionId: null` on both of its `OpenDraftAsync` calls. So no row in
the running product has ever had `admission_id` set, the Generate strip would have been
permanently empty, and the verify script's "finalised indoor note" step was unwritable.

**Added** `/emr/indoor/{AdmissionId:long?}` (`Pages/Emr/Indoor.cshtml`), the ward-round
prescription: complaint / diagnosis / advice plus five drug lines, save-draft and sign, under the
existing `Perm.EmrNoteWrite` — no new permission. Deliberately thinner than the OPD consult: no
billing, no test ordering, no favourites, because an indoor stay's money is the folio's job (M6).

This is plumbing the approved scope required rather than new scope. It is worth a PM note that
indoor prescribing is now a screen in its own right; §5 M5's sub-features describe prescription
entry without distinguishing the two encounters.

## 2. Two defects in the new dev seed, both found by running it

`SeedNursingHrAsync` was written from the plan and crashed the host twice on first start:

1. **`HireAsync` needs an ambient transaction.** It issues an employee code, and
   `NumberSeriesService` refuses to run outside the caller's transaction (ADR-0004 / G19):
   *"Number issuance requires the caller's ambient transaction."* The seed used the DI-scoped
   contexts every other seed block uses. Rewritten to run inside `HmsTx.RunAsync`, taking `s.Hr`,
   `s.Kernel` and `s.Auth` from the scope.
2. **The idempotency guard was on the wrong row.** Masters were gated behind
   `if (await hr.Employees.AnyAsync(...)) return`. The first crash had already committed the
   org unit, three designations and the grade, so the next start died on
   `ix_designation_branch_id_code`. Each master is now find-or-create against its own unique
   code, which is what "additive and guarded" claimed in the first place.

## 3. The stale instance cost a debugging cycle — the documented trap, walked into

The first `nursing-thread.py` run reported `HTTP 404` on `/ipd/station`, which read as a routing
defect. It was not: an `Hms.Web` binary started at 11:22 that morning still held port 5199, so
every request went to a build with none of these pages, while the new instance was crashing on
the seed above. `lsof -nP -iTCP:5199 -sTCP:LISTEN` is in `security-guardrails` §7 for exactly
this, and running it first would have saved the cycle.

## 4. Smaller departures

- **Spec number.** The plan was authored as 0039; that number was taken by
  `0039-lifecycle-hardening` before this work started. Everything is filed as 0041.
- **No "Nursing" nav group.** `NavComposer.Compose` groups by `NavItem.Module` and
  `ModuleNav.BuildPrefixes()` throws when two modules claim one URL prefix, so a distinct group
  would have required a new module and a new entitlement. The four entries join the existing
  **Indoor (IPD)** and **Clinical** groups instead. Recorded here because the plan's step 6
  originally proposed the group.
- **The verify script's dose count is computed, not hard-coded.** "1+0+1 for 3 days" is six
  doses before 08:00 Dhaka and five after, because day one drops slots that have already passed.
  The first version asserted `== 6` and failed at 14:00 — the same shape as the spec 0027 defect
  where a suite was only green for part of the day. `expected_doses()` now derives it from the
  wall clock, and a separate check asserts no generated dose is born overdue.
- **`Charts.cshtml.cs` was hardened while it was open.** It predates the 0039 input tier and had
  fourteen unannotated bound properties, so a mistyped glucose reached Postgres and returned a
  23514 through the generic fault boundary. Its bindings now cite `Bounds` and `[Range]` limits
  matching `ck_glucose_value` and `ck_glucose_insulin`.
- **LC-NUR-06's gap-register row was removed rather than marked closed.**
  `check-lifecycle-traceability.sh` separates case rows from register rows by the presence of
  `**High|Medium|Low**`; a `**Closed**` marker would have made the register row count as an
  uncovered case. The closure is recorded in the case row's coverage cell.
- **Audit tier 1** for `emr.mar.generated`, `emr.task.*` and `ipd.duty.*`. Tier 2 is reserved for
  reversals and money (`security-guardrails` §3); administering a dose stays tier 2 as before.
- **`check-icon-glyphs.sh` could not run locally** (fontTools not importable by this `python3`).
  Every glyph used — `monitoring`, `group`, `task_alt`, `edit_note`, `save`, `schedule`, `error`
  — was already in the committed subset, verified by `git grep` against pre-existing files, so no
  font rebuild was needed. CI runs the real check.

## 5. Verification actually performed

- `dotnet test hms-erp.slnx` — **488 passed, 0 failed** across all five test projects
  (40 new `MarScheduleTests`, 21 new `NursingStationTests`).
- Guards: ui-tokens, css-classes, no-native-date, no-hard-deletes, fkeys, no-external-hosts,
  lifecycle-traceability all pass (182 cases, 170 covered, 12 gaps). Icon guard: see above.
- `nursing-thread.py` against a locally-run app: **8 cases, 0 failed**, run twice (re-runnable,
  teardown returns the bed both times).
- `role-journeys.py`: 15 cases, 0 failed, 12/12 roles.
- `lifecycle-suite.py --tier t1`: **11 scripts green**, ward census unchanged at 13 free beds.

One thing to expect in `git status` afterwards: any `dotnet test` run rewrites
`eng/spike-artifacts/bangla-sample.pdf` (`Hms.PrintGolden.Tests/BanglaSpikeTests.cs:40` writes it
unconditionally, and the PDF carries a creation timestamp, so the bytes differ at identical size).
It is test fallout, not a change belonging to any spec — `git checkout` it before committing.

## 6. Open, deliberately

- Slot times (08/13/20; four-part 08/12/16/20; N-hourly from 06:00) are a clinical-convention
  default in `MarSchedule`, unconfirmed by a nursing SME.
- The dose horizon is clamped to 10 days and nothing auto-marks doses missed at discharge; a
  trailing scheduled dose on a discharged admission is left for a nurse to close.
- Frequency dialects beyond `a+b+c(+d)` and `N hourly` — the dash form `1-0-1` in particular —
  fall back to manual scheduling. Adding a dialect is a constant in one pure class plus a test.
- **Not deployed.** The ERP VM image has been stale since 2026-07-29; this rides the next rebuild.
