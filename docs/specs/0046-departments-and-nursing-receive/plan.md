# 0046 — Plan

## Approved: 2026-08-04

(Section of the approved demo-day plan; shared checkpoint/verify/deploy steps in the plan's
Context apply to all five specs 0046–0050.)

**Schema — the day's only migration** (IpdDbContext; additive; intra-schema FKs only; BranchId ⇒ branch isolation + Restrict come free):
- `Department { Id, BranchId, Name, Active }` → `ipd.department`
- `DepartmentStaff { Id, BranchId, DepartmentId (FK), UserId (scalar → adm.app_user, no FK), StaffName, Active }` → `ipd.department_staff`
- `Ward.DepartmentId` (nullable long, FK)
- `dotnet ef migrations add DepartmentsAndWardDepartment0046 --project src/Modules/Ipd/Hms.Ipd --startup-project src/Hms.Web --context IpdDbContext`

Deliberate choice: department membership is **queried per request** from `ipd.department_staff`, not a claim — module-owned, no kernel/HRM impact, and changes take effect immediately (no 5-minute cookie staleness).

**Seeds** (DevSeed.cs, each block additive-guarded like the pharmacy counter at DevSeed.cs:167-173):
- Departments (Medicine, Critical Care, Private Wing, Surgery, Gynae & Obstetrics, Paediatrics);
  ward→department mapping by Name for wards with `DepartmentId == null` (separate block, NOT
  inside the `!Wards.Any()` guard); `DepartmentStaff`: nasrin → Medicine, rehana → Critical
  Care; Cast += `("rehana", "Rehana Begum", "Nurse")` (existing role — zero
  ROLE_GRANTS/grant-drift churn).

**Station** (src/Hms.Web/Pages/Ipd/Station.cshtml(.cs)):
- Resolve signed-in user's departments; if assigned → restrict ward list + patients to those departments' wards, department name in header; unassigned (admin/matron) → all wards as today.
- One extra Emr query: `ReceiveNote` presence per live admission → buckets **"New — awaiting receive"** vs **"In care"**; summary strip; NEW pill + **Receive** button per unreceived tile (visible when `Can("emr.vitals.record")`).
- Preserve the `<tr>`-per-patient table and literal strings nursing-thread greps: "0 overdue", "nothing scheduled", "overdue", "late", "open", "On duty today", "No roster published", in-row allergy text.

**Receive page (new)** — `src/Hms.Web/Pages/Emr/Receive.cshtml(.cs)`:
- `@page "/emr/receive/{AdmissionId:long}"`, `[Authorize(Policy = Perm.EmrVitalsRecord)]`, derives `HmsPageModel` (InputGateCoverageTests). Vitals fields with the same `[Range]`s/tenths conversion as Vitals.cshtml.cs:23-34 + ReceivedFrom/Condition/Belongings.
- POST = one `HmsTx.RunAsync`: server-side department guard (open BedStay → Bed → Ward.DepartmentId ∈ user's departments, unless unassigned) → `RecordHandoverAsync` + `RecordVitalsAsync(admissionId: …)` (skip if all vitals blank — service throws on empty) + audit `emr.receive`. Redirect `/ipd/station`.

**Script updates (same commit):** role-journeys.py ROUTES += `"/emr/receive/1": "emr.vitals.record"`; nursing-thread.py:94 bed regex `(GW[MF]|CAB|ICU)` → `(GW[MF])` so the fixture patient lands in nasrin's department.
