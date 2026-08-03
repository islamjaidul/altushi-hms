# 0042 — Plan

## Approved: 2026-08-03

# Spec 0042 — Nursing Station cross-module hardening (audit → remediation)

## Context

A Principal-Architect-level audit of the 0041 Nursing Station's cross-module data flows (doctor rounds → charges, ward → pathology, ward → pharmacy, folio → accounts, patient-info propagation). Two deep-trace agents produced 15 evidence-pinned findings. The user chose: **fix the critical defect class now** + two opted-in additions (**allergy surface**, **Rx→pharmacy product link**); remaining visibility gaps get recorded, not built. Decisions taken: visit charge **auto-posts on signing** an indoor prescription; terminal-exit orphans get **read-only access + gate warnings** (no machine ever writes a clinical outcome).

Frame: M12/M13/M14/M15/M17/M18 are unbuilt (Wave-4+). M17 payout *computation* is future scope; recording the facts it needs is present scope where a built module carries the [M].

## Audit findings (all verified with file:line)

**Fixed by this spec:**
- **F1 [M] gap — consultant visit entry has no code path.** No Visit entity (IpdDbContext.cs:224-234); signing an indoor note is financially inert (Indoor.cshtml.cs:123 → EmrService.FinaliseAsync — audit row only); only path is a cashier manually posting seeded `IPD-VISIT` (DevSeed.cs:465) with a *nullable* doctor (Folio.cshtml.cs:51,251). The doctor who wrote the round (emr.note.doctor_id) and the doctor who gets paid (bill.charge_line.doctor_id) are unlinked facts. OT is the done-right reference (OtBilling.cs:56-57 writes AmountPosted/ChargeLineId to case_team for M17).
- **F2 — diagnostics counter bills inpatients outdoors.** Pages/Diagnostics/Order.cshtml.cs:184-198 always creates encounter+invoice; no FolioId branch; missing `EnsureNotBlockedAsync` R4 guard (present on Opd:114, Pos:160, Admit:96). Violates M6 [M] "every chargeable event posts to the folio". (Folio-parented ordering itself works from /ipd/folio — IpdBilling.OrderTestsAsync:352 is the correct reference pattern.) Bonus defect: EmrOrdering.cs:31-32 throws unmapped `InvalidOperationException` for indoor notes → 500 not a sentence.
- **F8 — orphaned clinical work at every terminal exit (worst finding).** Discharge/Death/Absconded never coordinate with emr, and all three readers of MarDoses/CareTasks gate to live admissions (Station.cshtml.cs:61-67, Charts.cshtml.cs:99, Tasks.cshtml.cs:71). At the terminal instant every open task and scheduled dose becomes **invisible and un-closable**; a discharged patient's MAR cannot even be read (records retention). 0041 notes.md §6's "left for a nurse to close" is false of the built system — 0042 must supersede that statement. Not on the QA gap register. nursing-thread.py's teardown closes all work *before* discharging — the exposing path is the one it avoids.
- **F9 — `AdministerAsync` missing branch predicate (cross-tenant write).** EmrService.cs:275-280 `WHERE id={doseId} AND state='scheduled'` — no `AND branch_id`; CompleteTaskAsync:393 has it. A branch-B operator can mark a branch-A dose Given.
- **F10 — writes accept any AdmissionId in any state.** CreateTaskAsync validates title only; ScheduleDose/Glucose/Handover accept admissionId verbatim; Indoor.WriteAsync loads admission with no state predicate (a doctor can prescribe on a dead patient by posting the id). Live-admission filters run on GET only. Several `AdmissionId!.Value` NREs on POST (500s).
- **F7/F15 — money seams untested.** Zero tests call IpdBilling.OrderTestsAsync, IssueIndentAsync (IpdFolioTests:497 bypasses with fake id), PostServiceAsync, FolioService.CreateIndentAsync, EmrOrdering.OrderTestsAsync. RecordDeathAsync/RecordAbscondedAsync have zero xunit coverage; no test anywhere covers discharge-with-open-clinical-work.
- **F11a (opted in) — allergy surface absent.** No allergy field anywhere in src/ (PRD §5 M5 [C], cut in 0024); 0041 added four clinical screens incl. a prescribing one without re-stating the cut. BloodGroup captured but never shown clinically; ProvisionalDx write-only; Charts/Tasks/Indoor banners show name+admission-no only.
- **F4a (opted in) — Rx→pharmacy disconnected.** No code reads NoteDrug→IndentItem; indents typed from scratch. /emr/indoor hard-codes `ProductId=null` (Indoor.cshtml.cs:139) so indoor lines can't resolve to products even if a converter existed.

**Recorded, not built (→ QA gap register + spec Out-of-scope):** F3 nurse-side service posting (oxygen from tasks), F5 clinical discharge checklist beyond warnings, F6 dead `TestOrderPaid` outbox writes (no consumer anywhere) + no pharmacy/pathology notifications (poll-only), F12 ward/bed invisible to pharmacy porter/LIS phlebotomist/OT (OtCase has no AdmissionId), F13 receive-note unreachable from admit flow + no admission→ward notification, F14 duty EmployeeId validated in page not service, MarDose.ProductId (dose-vs-issue reconciliation), doctor-facing ordering UI on /emr/indoor.

## Remediation plan

### Step 0 — spec-flow
`docs/specs/0042-nursing-cross-module-hardening/{spec.md,plan.md,tasks.md}` + index row. spec.md cites §5 M6 [M] (visit entry, folio universality), §5 M5 [C] (allergy), §11, §12, security-guardrails §2; states it **supersedes 0041 notes.md §6's terminal-dose claim**. No PRD edit needed: everything implements existing §5 text ([M] M6 visit entry, [C] M5 allergy) — nothing is new scope.

### Step 1 — Schema (additive; 3 migrations)
- **ipd**: new `ConsultantVisit` → `ipd.consultant_visit`: Id, BranchId, AdmissionId(FK), DoctorId (scalar — doctor master is cross-schema), OnDate(DateOnly), NoteId? (emr scalar), ChargeLineId? (bill scalar — null when folio wasn't postable at sign time), CreatedAt/By. **Unique (AdmissionId, DoctorId, OnDate)** — the BedDay-style idempotency anchor: one charge per doctor per day per admission no matter how many notes are signed. Migration `ConsultantVisit`.
- **reg**: `Patient.Allergies` (string?, max 4000 = Bounds.Note). Migration `Allergies`. (reg is already in the CI gate.)
- **emr**: `NoteDrug` already has ProductId — no change. MarDose.ProductId deliberately deferred (register).
- ipd + emr + reg contexts already/now in the CI additive gate (emr/ipd added by 0041; verify reg lines exist — they do).

### Step 2 — Tests first (the failing tests that define done)
`tests/Hms.Integration.Tests/` (new `WardMoneySeamTests.cs` + extend `NursingStationTests.cs`):
1. **Visit auto-post**: sign indoor note → `ipd.consultant_visit` row + folio charge line at rate-plan price with doctor id; sign a second note same doctor/day → **no second charge**; different doctor same day → second charge; blocked folio → visit row with null ChargeLineId, no charge, no throw.
2. **Indoor lab seam end-to-end** (first-ever): IpdBilling.OrderTestsAsync → folio gate → charge → folio-parented order born InProgress → samples raised; blocked folio refused.
3. **Indent issue seam** (first-ever, real path): FolioService.CreateIndentAsync → IssueIndentAsync → folio charge per batch at MRP; return posts negative line at exact MRP.
4. **PostServiceAsync**: rate resolve + folio gate (first-ever).
5. **Branch scoping**: AdministerAsync with wrong branchId → "already recorded" refusal, row untouched (fails today — the F9 proof).
6. **Write guards**: CreateTask/ScheduleDose/Generate/prescribe on a discharged admission → refused with a sentence (fails today); on a nonexistent admission → refused.
7. **Terminal exits**: discharge with 1 open task + 2 scheduled doses → doses/tasks still readable, closable-with-reason via service; RecordDeathAsync leaves them queryable; clearance surfacing data correct. First xunit coverage of RecordDeathAsync/RecordAbscondedAsync.
`tests/Hms.Web.Tests/`: none needed beyond existing (no new pure parsing).

### Step 3 — Business logic
- **EmrService.AdministerAsync**: add `AND branch_id = {branchId}` (one line + test 5).
- **EmrOrdering.cs:31**: `InvalidOperationException` → `EmrException` with an operator sentence.
- **New composition-root guard** `src/Hms.Web/WardGuard.cs`: `RequireLiveAsync(TxScope, admissionId)` → admission in Admitted|DischargeInitiated|Blocked (else EmrException-style sentence "This admission is closed — the chart is read-only."), and `RequireAsync` (exists at all, any state) for close-out actions. Used by every ward POST handler (cross-module state check belongs at the composition root per ADR-0003 — the same reason IpdBilling exists).
- **New `IpdBilling.PostConsultantVisitAsync(s, branchId, admissionId, doctorId, noteId, serviceDate, actor…)`**: find-or-skip on the unique key (idempotent); resolve `IPD-VISIT` via RateResolver by service date; if folio postable → PostFolioChargeAsync with doctorId and link ChargeLineId (the OtBilling case_team pattern — the M17 fact + the money in one record); if folio blocked/locked → write visit row only (charge follows the existing late-post/manual path). Audit `ipd.visit.recorded` tier 1.
- **FolioService/IpdService**: no signature changes. Discharge/death/abscond handlers gain no emr writes (decision: no machine-authored clinical outcomes) — coordination is read-only surfacing (Step 4).

### Step 4 — Pages
- **/emr/indoor** (Indoor.cshtml.cs): sign path calls WardGuard.RequireLiveAsync then, after FinaliseAsync, `IpdBilling.PostConsultantVisitAsync` (page gains IpdBilling + RateResolver deps — the Consult precedent). Add product typeahead per drug row (`data-typeahead="/api/typeahead/products"` + hidden DrugProductId — copy Consult.cshtml:201-206), pass ProductId through CollectDrugs, validate posted ProductIds exist against s.Pharm (Consult's AUD-VAL-21b block). Toast gains "· visit charge posted" when it was.
- **/emr/charts + /emr/tasks**: POST guards via WardGuard (`RequireLiveAsync` for Schedule/Generate/Glucose/Handover/Create; `RequireAsync` for Administer/Done/Cancel so close-out works on terminal admissions). **Read-only terminal mode**: load no longer nulls AdmissionId for terminal states — instead sets `ReadOnly=true`; views hide Schedule/Generate/Create/Handover forms and keep Administer (reason required) + Done/Cancel forms; banner "Admission closed (discharged/death/absconded) — record is read-only except closing open items." Rail stays live-only; terminal access arrives via /emr/history links (add "Charts" link per past admission row) and the Step-4 warnings. Fix all `AdmissionId!.Value` NREs → `Fail` sentence.
- **/ipd/discharge** (clinical clearance): panel "Open ward items" — open CareTasks + Scheduled MarDoses counts with links to the (read-only) chart/tasks; purely informational, does not block (money gate unchanged).
- **/ipd/admissions** death/abscond handlers: after recording, toast includes "N open doses/tasks remain — close them from the patient's record" when N>0.
- **/ipd/folio**: "Indent from prescription" strip on the indent card — pick a signed indoor note; lines with ProductId prefill the indent items (qty editable, default 1); product-less lines listed as "not on formulary — add manually". Reuses CreateIndentAsync unchanged.
- **/diagnostics/order**: on patient pick, `FindOpenAdmissionAsync` + `EnsureNotBlockedAsync`; if open admission → banner "Admitted patient — charges post to the folio", order routes through `IpdBilling.OrderTestsAsync` (no separate invoice); blocked → refusal sentence. Outdoor path unchanged.
- **Patient banner (allergy)**: shared partial `_PatientBanner.cshtml` (Hms.Web/Pages/Shared) — UHID · age/sex · **blood group** · **allergy pill (red, `pill bad`, only when present)** · ProvisionalDx where admission-scoped. Used on Charts/Tasks/Indoor/Consult; Station tile gains allergy glyph + ProvisionalDx line. **Registration/New + edit**: `Allergies` input (Bounds.Note, `[StringLength]`).

### Step 5 — Verify script + docs
- **nursing-thread.py**: new cases — (LC-NUR-14) sign → folio shows consultant-visit line with doctor name, second sign adds nothing; (LC-NUR-15) discharge with an open task + scheduled dose → station drops the patient, chart still opens read-only, close both with reasons, clearance panel showed them; (LC-NUR-16) allergy entered at registration renders on indoor/charts banners; (LC-NUR-17) indent-from-prescription prefills and issues; diagnostics-counter case moves to lifecycle-thread or stays here: admitted patient at /diagnostics/order → folio line not a new invoice.
- **patient-lifecycle.md**: add LC-NUR-14…17 rows; **gap register additions** for everything in "Recorded, not built" above (F3, F5, F6, F12, F13, F14, MarDose.ProductId, indoor ordering UI) — the audit's whole point is that F8 wasn't on the register; don't repeat that for the rest.
- **0041 notes.md is append-only** — 0042's spec.md carries the superseding correction; do not edit 0041.
- Nurse role: no new permissions (all surfaces reuse existing claims); role-journeys ROUTES unchanged except none — verify.

### Step 6 — Verification
1. `dotnet test hms-erp.slnx` — new tests fail before Step 3/4, pass after; all 488+ green.
2. Guards: additive-migrations (3 contexts), css-classes, ui-tokens, icon-glyphs (no new glyphs planned — reuse), no-native-date, no-hard-deletes, lifecycle-traceability (new LC rows ↔ script cases ↔ ROUTES unchanged).
3. Local run on :5199 (`--no-launch-profile`; **check `lsof -nP -iTCP:5199` first** — the stale-instance trap): nursing-thread.py (old + new cases) ×2, role-journeys.py, full `lifecycle-suite.py --tier t1`.
4. spec-flow close-out; deploy note (rides next ERP image rebuild).

## Answer to the user's headline question ("is there any problem or not")

**Yes.** The station's *reads* comply (branch-isolated, in-memory joins, correct patterns), and pharmacy-indent + settlement seams are sound. But: the doctor-round charge had no code path ([M]), the diagnostics counter breaks folio universality for inpatients, clinical work orphans at every terminal exit, one cross-tenant write exists, ward writes don't validate admission state, and the money-carrying indoor seams had zero test coverage. This spec closes those; the visibility gaps (ward/bed for porters and phlebotomists, OT↔ward linkage, notifications) are recorded on the QA register for a follow-up spec.
