# 0041 — Plan

## Approved: 2026-08-03

# Nursing Station — spec 0041 (schema → tests → business logic)

## Context

Integrate a **Nursing Station** capability into the ERP: (1) a ward monitoring dashboard, (2) MAR schedule generation from the indoor prescription + overdue-dose visibility (closes QA gap **LC-NUR-06**), (3) patient care tasks, (4) ward duty assignment (closes M6's absent `[S]` duty-assignment item from the 0038 audit).

A detailed plan was authored last night (`~/.claude/plans/sprightly-booping-spark.md`) but never archived as a spec, and the number it claimed (0039) has since been consumed by `0039-lifecycle-hardening`. This becomes **spec 0041-nursing-station**. Two exploration agents verified every assumption against the post-0039/0040 codebase; corrections are folded in below. Shape confirmed: **composition + gap-fill, not a 15th module** — recorded in the PRD as sub-module **R5** (Home: M5/M6/M16). Scope ends at build + local verification; VM deploy rides the next ERP rebuild (image stale since 2026-07-29).

### Key corrections vs. yesterday's plan (verified)

1. **Spec number is 0041**, not 0039.
2. **No cross-module "Nursing" nav group is possible.** `NavComposer.Compose` groups by `Module` (src/Hms.Kernel/Auth/NavComposer.cs:34) and `ModuleNav.BuildPrefixes()` throws if a prefix has two module owners. New entries join existing groups: Station + Duty → `Ipd` group, Care Tasks → `Emr` ("Clinical"). "Nursing Charts" stays at ModuleNav.cs:51.
3. **Architecture tests live in `tests/Hms.Architecture.Tests/`**, not Hms.Web.Tests. `MarScheduleTests` goes in `tests/Hms.Web.Tests/` (references src/Hms.Web; Hms.Emr is transitively reachable) in the LoginValidationTests style (xUnit, global `using Xunit`, `[Theory]/[InlineData]`, prose assert messages, XML-doc header).
4. **`MarDose` has no `NoteDrugId`** (EmrDbContext.cs:123–138); `CareTask` and `DutyAssignment` exist nowhere; **no frequency parser exists** — all net-new. Note entity is **`Note`** (not "IndoorNote"), admission-notes discriminated by `AdmissionId` under `ck_note_parent`.
5. **0039 input tier is binding on new pages**: derive from `HmsPageModel` (enforced by `InputGateCoverageTests`); annotate every bound property with `Hms.Shell/Validation.cs` `Bounds`/`[Money]/[Qty]/[PlausibleDate]`; page attribute ↔ entity `HasMaxLength` ↔ migration column cite the same constant. Copy `Consult.cshtml.cs`/`Folio.cshtml.cs` as templates — **not Charts.cshtml.cs** (it predates 0039 and is unhardened).
6. **CI additive-migration gate lacks `EmrDbContext`/`IpdDbContext`** (.github/workflows/ci.yml:48–78 covers kernel/auth/reg/hr/diag/notif) — must be added since we widen both schemas.
7. **`check-lifecycle-traceability.sh` does NOT check ROLE_GRANTS↔DevSeed** (that claim in role-journeys.py:26 is false). It checks: cited `auto` scripts exist, cited `xunit` classes exist, cast usernames in DevSeed, **every `Perm` constant enforced** (in role-journeys `ROUTES` or a `Can()` call), and **bidirectional ROUTES ↔ `[Authorize]`**. New perms without ROUTES entries fail CI.
8. **ERP host seeds zero HR data** (`HrDemoSeed` runs only on Hms.Hr.Web). `EmployeeService.HireAsync` (src/Modules/Hr/Hms.Hr/EmployeeService.cs:24) mandatorily needs OrgUnit + Designation + Grade ids — seed minimal masters first.
9. **`hrm-thread.py` is unregistered in TIERS on purpose** (different host/port) — don't "fix" it. `nursing-thread.py` targets the ERP host and does belong in `t1`.
10. `AdministerAsync`'s guard is now at EmrService.cs:268–275, audit 277–278. It lacks the post-UPDATE `ReloadAsync` that FinaliseAsync:139 uses — handlers must not pre-load MarDose entities before calling it.
11. Icons: use only existing glyphs — `monitoring` (Station), `task_alt` (Tasks), `group` (Duty) are all already in ModuleNav → no icon-subset rebuild.
12. New entities with `BranchId` auto-inherit branch isolation + `DeleteBehavior.Restrict` (EmrDbContext.cs:328–330, IpdDbContext.cs:347–349) — don't add manually.

---

## Step 0 — Spec artifacts first (hard rule 0)

Invoke **spec-flow**: create `docs/specs/0041-nursing-station/{spec.md, plan.md, tasks.md}`, add index row to `docs/specs/README.md`. spec.md: pillars, PRD refs (§5 M5/M6/M16, §5A-7, new R5, §12 nurse row, P27), closes LC-NUR-06 + M6 duty `[S]` absence, lands on the 0038-audit baseline, open questions (bottom of this plan). Archive this file as `plan.md` on approval.

## Step 1 — PRD edit (business language only; `module-spec` skill)

`docs/project_manager.md` (grep + offset, never whole-file):
- **§5A.2 table**: add **R5 — Nursing Station (ward nursing console)** after R4, Home = M5/M6/M16, **[S]**: ward monitor (bed, latest vitals, doses due/overdue, pending indents, open tasks, on-duty staff); Medicine Chart schedule generated from the indoor prescription with overdue doses visibly flagged; attributable care tasks (create/complete/cancel-with-reason); ward duty assignment per ward/shift/day fed by the HR roster where published.
- 3–4 user stories in house style (`As Salma, the ward nurse…` + `**AC:**`) under M5/M6. No tech vocabulary (hard rule 1), no pricing math (P5).
- Changelog: **v1.2 (03 Aug 2026)** with the R5 entry (current head is v1.1).

## Step 2 — SCHEMA (design first, additive only)

**`emr` schema** (src/Modules/Emr/Hms.Emr/Data/EmrDbContext.cs):
- `MarDose`: add nullable `NoteDrugId` (long?, intra-schema FK to `emr.note_drug`, `Restrict`, follow 0039's `NOT VALID` convention). Partial unique index `(note_drug_id, scheduled_at) WHERE note_drug_id IS NOT NULL` — idempotent generation under concurrency.
- New `CareTask` → `emr.care_task`: `Id, BranchId, AdmissionId (FK→emr? no — admission is ipd; scalar, no cross-schema FK), Title (required, HasMaxLength(Bounds.Name)), Details? (Bounds.Note), Kind?, DueAt?, State ("open"|"done"|"cancelled"), StateReason?, CreatedAt/By(id+name), CompletedAt/By`. CHECKs: `ck_care_task_state`, cancelled ⇒ `state_reason` non-null, done ⇒ `completed_at`/`completed_by` non-null, length CHECKs mirroring Bounds. Indexes `(AdmissionId, State)`, `(BranchId, State)`. No delete path.
- Migration `NursingStation` on `EmrDbContext`.

**`ipd` schema** (src/Modules/Ipd/Hms.Ipd/Data/IpdDbContext.cs) — wards live here; P27 keeps ward vocabulary out of `hr`:
- New `DutyAssignment` → `ipd.duty_assignment`: `Id, BranchId, WardId (FK→ipd.ward), OnDate (DateOnly), ShiftLabel ("morning"|"evening"|"night"), StaffRole ("nurse"|"ward-boy"|"aya"), EmployeeId (nullable scalar — no cross-schema FK), StaffName (required, Bounds.Name snapshot — works when hr is empty), Active, EndedReason/At/By, CreatedAt/By`. CHECKs on shift/role vocab + lengths. **Partial unique `(WardId, OnDate, ShiftLabel, StaffName) WHERE active`** (partial, so end-then-reassign works). Remove = deactivate-with-reason, never delete. Any non-fluent SQL follows the `MIGRATION-SQL:` comment convention (IpdDbContext.cs:203).
- Migration `DutyAssignment` on `IpdDbContext`.

**CI**: add `EmrDbContext` + `IpdDbContext` script/check pairs to the additive-migration gate (.github/workflows/ci.yml:48–78).

## Step 3 — TESTS (written before the logic they test)

**A. `tests/Hms.Web.Tests/MarScheduleTests.cs`** — pure parser cases (class name will be cited by the lifecycle doc; traceability guard greps it):
- `"1+0+1"` × 7 days → 14 doses at 08:00/20:00; `"1+1+1"` → 21 at 08/13/20; `"1+1+1+1"` → 4-part times 08/12/16/20; `"0+0+0"` → 0 doses, returns true.
- `"8 hourly"` → 06/14/22; `"12 hourly"` → 06/18; N ∉ {4,6,8,12,24} → false.
- PRN / prose / blank / garbage → false (never guess a drug time).
- Duration: `"3 days"` → 3; missing → default 3; `"45 days"` → clamp 10; `"0"` → clamp 1.
- Day-1 truncation: slots ≤ `notBefore` dropped — never generate a dose born overdue.
- `IsOverdue`: scheduled + older than 30-min grace → true; exactly at boundary → false; given/missed/refused → never overdue.
- Whitespace/casing tolerance (`" 1 + 0 + 1 "`, `"8 HOURLY"`).

**B. `tests/Hms.Integration.Tests/`** (Postgres fixture, alongside EmrTests.cs) — service cases:
- `GenerateScheduleAsync`: inserts expected rows with `NoteDrugId` set; **re-run inserts zero** (idempotent); refuses non-finalised note; refuses note whose `AdmissionId` ≠ argument; PRN lines skipped and reported; audit row written.
- `CareTask`: create requires title; complete is single-shot (second complete throws "already closed"); cancel requires reason; state CHECKs hold at the DB.
- `DutyAssignment`: duplicate active assignment refused; end-with-reason then reassign same name succeeds (partial unique proof); vocab CHECK holds.

**C. e2e cases** — enumerated now, scripted in Step 7 (`nursing-thread.py` cases 1–10).

Architecture tests already enforce the rest (CrossContextQueryTests — no cross-context joins; InputGateCoverageTests — HmsPageModel; ViewGuardPermissionTests — bare claims in `Can()`; PermissionCatalogTests — nav claims have constants).

## Step 4 — BUSINESS LOGIC

**`src/Modules/Emr/Hms.Emr/MarSchedule.cs`** — pure static, no I/O/clock: `TryExpand(frequency, duration, firstDay, notBefore, out slots, out days)` + `IsOverdue(state, scheduledAt, nowUtc)` per the test cases above. Slot constants in one place (clinical-convention defaults, cheap to change). UTC conversion stays outside via `Ui.DhakaMidnightUtc` (Charts.cshtml.cs:173 pattern). Overdue is **render-time computation — no background worker**.

**EmrService.cs** (mirror AdministerAsync's raw-UPDATE single-shot guard at :268–275 + audit at :277):
- `GenerateScheduleAsync(emr, kernel, branchId, admissionId, noteId, actor…)`: load `Note` (finalised, `AdmissionId == admissionId`), expand each `NoteDrug`, skip already-generated `(NoteDrugId, ScheduledAt)` instants (unique index is the backstop), insert `MarDose { NoteDrugId, DrugName, Dose, ScheduledAt(utc) }`, audit `emr.mar.generated`, return (inserted, unparseable lines).
- `CreateTaskAsync` (title required → `EmrException` with plain message), `CompleteTaskAsync` / `CancelTaskAsync` — guarded `UPDATE … WHERE id=@id AND state='open'`; 0 rows → "already closed — reload"; cancel requires reason; audit `emr.task.done`/`emr.task.cancelled`.

**IpdService.cs**:
- `AssignDutyAsync` (ward in-branch, vocab, name non-blank; duplicate → plain refusal), `EndDutyAsync` (guarded update to inactive, reason required); audit `ipd.duty.assigned`/`ipd.duty.ended`. Module never reads `hr` (ADR-0003): EmployeeId/StaffName arrive as scalars from the composition root.

## Step 5 — PAGES (src/Hms.Web/Pages/, inject `HmsTx`, derive `HmsPageModel`, Validation.cs attributes on every bound prop, top-of-page `alert bad` block, `hms-date` helper, existing CSS classes/glyphs only)

- **`/ipd/station/{WardId:long?}`** — NEW `Ipd/Station.cshtml(.cs)`, `[Authorize(Policy = Perm.IpdRead)]`, read-only (no handlers). Separate per-context reads joined in memory: `s.Ipd` wards/beds/open BedStays/active admissions (Take 120)/requested `Indent`s/today's active DutyAssignments; `s.Reg` patient banners; `s.Emr` latest vitals (24 h), scheduled doses → due/overdue via `IsOverdue`, open CareTask counts; `s.Hr` today's roster panel, degrading to "No roster published — manage on HR → Roster". Per-ward cards with overdue badge, task/indent chips, duty strip; tiles link to `/emr/charts/{id}`, `/emr/tasks/{id}`, `/ipd/folio/{id}`. Reuse Board.cshtml.cs:33–73 + Charts.cshtml.cs:53–93 load shapes.
- **`/emr/tasks/{AdmissionId:long?}`** — NEW `Emr/Tasks.cshtml(.cs)`, `[Authorize(Policy = Perm.EmrTaskManage)]` (one policy covers view+handlers — Charts precedent). Ward-patient rail; open tasks with overdue tint; history with actor names (resolve via `s.Auth.Users`). Handlers `OnPostCreateAsync/OnPostDoneAsync/OnPostCancelAsync` — `tx.RunAsync`, toast, redirect; catch `EmrException` → `LoadAsync` + `Fail(msg)`.
- **`/ipd/duty`** — NEW `Ipd/Duty.cshtml(.cs)`, `[Authorize(Policy = Perm.IpdDutyManage)]`. Date selector (default today), ward × shift grid; staff picker from `s.Hr.Employees` **plus free-text name**; when EmployeeId posted, resolve StaffName server-side from the employee row — never trust the posted name. Handlers `OnPostAssignAsync/OnPostEndAsync`.
- **`/emr/charts`** — EDIT: per finalised admission note, "Generate schedule from prescription" strip → `OnPostGenerateAsync(NoteId)` → toast "n doses scheduled" + hint listing unparseable lines; dose rows get **Overdue** badge via `IsOverdue`; verify missed rows render StateReason (with mark-missed this closes **LC-NUR-06**). Stays under `Perm.EmrChartRecord`. **While touching it, add the missing 0039 validation attributes to its bound props** (Glucose range matching `ck_glucose_value`, StringLengths matching the mar_dose length CHECKs).

## Step 6 — Permissions, nav, seed

- **Perm.cs**: `EmrTaskManage = P + "emr.task.manage"`, `IpdDutyManage = P + "ipd.duty.manage"` (catalog auto-reflects; check PermissionCatalog label reads sensibly, else add a Description).
- **ModuleNav.cs**: `("Ipd","Nursing Station","/ipd/station","ipd.read","monitoring","Indoor (IPD)")`, `("Ipd","Ward Duty","/ipd/duty","ipd.duty.manage","group","Indoor (IPD)")` after line 45; `("Emr","Care Tasks","/emr/tasks","emr.task.manage","task_alt","Clinical")` after line 51. **No new group/module/prefix** (correction #2).
- **DevSeed.cs**: Nurse role (lines 43–47) += `emr.task.manage`, `ipd.duty.manage`. New idempotent `SeedNursingHrAsync` on the ERP host: if branch has no `hr` Shifts → MORN 07–14 / EVE 14–21 / NIGHT 21–07 (EndsNextDay); if no Employees → minimal OrgUnit/Designation/Grade masters (HrDemoSeed.cs:167–193 is the reference), then ~5 staff via `EmployeeService.HireAsync`; set nasrin's `Employee.UserRef = <her AppUser.Id>`; optionally one published roster week so the panel demos.
- **role-journeys.py**: `ROLE_GRANTS["Nurse"]` += both claims; `ROUTES` += `/ipd/station: ipd.read`, `/emr/tasks: emr.task.manage`, `/ipd/duty: ipd.duty.manage` (required by traceability guard's bidirectional check + perm-enforcement check).

## Step 7 — Verify script `eng/verify/nursing-thread.py`

Shape of hrm-thread.py (import `_harness`, `guard("t1")`, single `main()`, `case(...)` blocks; **never** copy legacy ipd-thread.py). After every write, re-GET a rendered screen and assert the row — never a status code (the HmsTx no-flush trap). Persona `nasrin`; helpers `jashim` (admit), `chowdhury` (indoor Rx), `rasel` (Billing, for denials) — all existing cast. Cases:
1. admit; 2. station shows patient, zero overdue; 3. finalise indoor note "Napa 500mg, 1+0+1, 3 days" + one PRN line; 4. Generate → correct count at 08/20 slots, PRN excluded, **re-Generate idempotent**; 5. **LC-NUR-06** — past-scheduled dose shows Overdue on chart + station count, mark-missed-with-reason visible, double-mark refused; 6. task create (due past) → overdue on `/emr/tasks`, station count increments, complete shows actor name, double-complete refused; 7. cancel without reason refused; 8. duty assign → station duty strip shows name, duplicate refused, end-with-reason works; 9. adjacent-job denial — `rasel` denied `/ipd/duty` + `/emr/tasks`, `jashim` denied `/emr/tasks` (use `Session.denied`, not raw status); 10. roster panel renders (seed) or graceful empty message.

**Register in lifecycle-suite.py `TIERS["t1"]`** (it drives the ERP host — belongs there, unlike hrm-thread).

## Step 8 — Lifecycle doc bookkeeping

`docs/qa/patient-lifecycle.md`: LC-NUR-06 (line 283) `gap` → `auto` nursing-thread 5; update the gap register row (:416) per house style; append LC-NUR-07…12 (generation arithmetic + `xunit` MarScheduleTests, station composition, task attributability/single-shot, cancel-reason, duty assign/duplicate, role denial). IDs append-only; keep the `| ID | Case | Expected | By | Coverage |` format the guard parses.

## Step 9 — Verification (before claiming done)

1. `dotnet build` + full test suite (Hms.Web.Tests, Hms.Integration.Tests, Hms.Architecture.Tests).
2. Guards: `check-additive-migrations.sh` (via the CI loop incl. the two new contexts), `check-ui-tokens.sh`, `check-css-classes.sh`, `check-icon-glyphs.sh`, `check-no-native-date.sh`, `check-no-hard-deletes.sh`, `check-lifecycle-traceability.sh`.
3. Local run: dev DB up (docker, 5455), `dotnet run` in src/Hms.Web, then `python3 eng/verify/nursing-thread.py`, `role-journeys.py`, and full `lifecycle-suite.py` for regressions.
4. spec-flow close-out: 0041 → Done, deviations in notes.md, **deploy-later note** (ERP VM image stale since 2026-07-29).

## Open questions recorded in spec.md (v1 rules chosen, not blockers)

- Slot times (08/13/20; 4-part 08/12/16/20; N-hourly from 06:00) are clinical-convention defaults — constants in one pure class; confirm with PM/nursing SME.
- Doses beyond discharge: clamp 10 days, list only active admissions, no auto-mark-missed at discharge.
- R5 MoSCoW **[S]** and PM confirmation that ERP-side duty assignment satisfies M6's `[S]` item.
- Frequency dialects beyond `a+b+c(+d)` / `N hourly` fall back to manual scheduling — never guess.
