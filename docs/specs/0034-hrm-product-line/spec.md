# 0034 — HRM as a dual-SKU product line (M16 HR & Payroll, sellable standalone)

- **Status:** In Progress
- **Date:** 2026-07-31
- **PRD ref:** §5 M16, §5A-16, §5A-17, §3.4 (TDS/PF/Welfare/BEFTN), §10 (Employee), §11 (Leave
  Application, Payroll Run), §12 (P12 HR Officer, permission matrix), §13 I8 (biometric devices), §7
- **MVP:** post-MVP — M16 is Wave 5 of `11-build-plan-phase2.md`; this spec pulls it forward and adds
  a packaging dimension the build plan did not contemplate.

## Problem

Two problems, one build.

**1. M16 does not exist.** The PRD specifies HR & Payroll in full; `docs/qa/module-coverage.md` records
it as "out of scope — no code"; `src/Modules/` has thirteen module directories and no `Hr`. A hospital
running this product still does attendance, leave, and payroll on paper and spreadsheets.

**2. A customer wants HR without the hospital.** That is not a discount — it is a different product.
Today it is impossible to deliver: `src/Hms.Web` is a monolithic host that owns every Razor Page,
every `AddDbContext` call, and `HmsTx`, which hard-references all fourteen module assemblies. Shipping
"just HR" today means shipping the entire hospital system and hiding the sidebar — and even that
hiding is cosmetic, because ADR-0016's choke point 2 (endpoint enforcement) was never built. A
customer with an HR-only entitlement can reach `/billing/opd` by typing it.

So the commercial promise ADR-0016 already made — *"new sale/packaging = new entitlement file, zero
deploys"* — is not yet a promise the code can keep.

## Scope decisions this spec records

These are business decisions, taken with the product owner on 2026-07-31, recorded here because
CLAUDE.md rule 2 routes genuinely new scope to the PM rather than absorbing it silently. They are
also raised as **P26–P29** in `docs/architecture/09-questions-for-pm.md` so the PM can overturn any
of them before the wave that consumes it.

| # | Decision | Consequence |
|---|---|---|
| D1 | **Two hosts, one codebase.** `Hms.Web` (full ERP) and a new `Hms.Hr.Web` (HRM-only), sharing Kernel, a new shared-UI library, and the HR module itself. | One repo, one CI, two Docker images, two entitlement files. Rejected: a separate repo (guarantees Kernel divergence) and entitlement-only gating of the monolith (ships the whole hospital system to an HR customer). |
| D2 | **Full §5A superset, built in waves.** Wave A is independently sellable; B–D add depth. | The product competes with the observed MEDISpa/PrimeMIS HR superset and with PiHR, rather than shipping the six `[M]` bullets and stopping. |
| D3 | **Market is any Bangladeshi employer**, not only hospitals. | HR carries no clinical vocabulary. Org structure is customer-entered masters. This is new scope beyond the PRD's 22-module hospital product — **P27**. |
| D4 | **Zero hardcoded statutory rates.** Every rate, slab, entitlement and formula is effective-dated customer configuration. | Honours CLAUDE.md rule 3 (no unverified regulation) and the build plan's standing instruction that BD statutory rules go to the PM, not into code. A verified BD default policy pack is a later spec — **P26**. |

## Requirements

The PRD-to-screen traceability matrix is in `plan.md` (DoD rule 1: matrix first). Summary:

**§5 M16 `[M]` — Wave A, the sellable core**
- Employee records (personal, official, salary grade, department, designation, document attachments)
- Attendance capture + admin review/correction with reason
- Shift & roster management (24/7 rotating shifts)
- Leave: types, balances, applications, approvals; without-pay handling
- Payroll: auto salary sheet from attendance (late/absent/OT/deduction rules), bonus entry, pay
  slips; posts to Accounts (M15)
- `[S]` Online leave application (self-service)

**§5A-16 — Waves B and C**
Comp-off, OT bank ledger, OT assist fee, weekly-off, grace-time, holiday-work pay policy, tiered
leave approval, leave policy/balance setup, bonus (register/create/sheet), increment policy,
promotion management, welfare & tax ledgers, PF withdrawal, salary-deduct settings, loan/advance
with installment deduction.

**§5A-17 — Wave D**
Appointment letter, experience certificate, termination letter; employee auth (login) history; job
age limit; new-joinee / resigned / salary-compare / dept-wise summary reports.

**Competitive additions** `[obs: PiHR]` — evidence: `mypihr.com/attendance-management-software/`
feature menu, captured 2026-07-31.
- Digital notice board (Wave B)
- Expense management with approval → reimbursement (Wave C)

**Packaging (this spec's own novel requirement)**
- The HR module and its screens build into both hosts from one source.
- An entitlement that omits a module makes that module's **endpoints** refuse, not merely its nav
  entries vanish.
- Entitlement expiry behaves per P6: grace with banner, then read-only; data is never locked away.

## Acceptance criteria

1. **One source, two products.** `dotnet run --project src/Hms.Hr.Web` serves a complete HRM against
   a database containing only the `kernel`, `adm` and `hr` schemas; `dotnet run --project src/Hms.Web`
   serves the unchanged ERP *plus* HR. No HR source file is duplicated between them.
2. **The boundary is real.** With an HR-only entitlement loaded into the **ERP** host, a user holding
   `billing.*` receives 403 from `/billing/opd` — by URL, not by hidden menu. Proven by an endpoint
   test, not by inspection.
3. **Entitlement lifecycle.** Past expiry inside grace: a banner shows and everything works. Past
   grace: GETs succeed, mutating handlers refuse with a clear message. Never a lockout of data.
4. **HR is module-independent.** `Hms.Hr` and `Hms.Hr.Screens` reference no module assembly other than
   `Hms.Hr*`, enforced by an architecture test that fails the build.
5. **Statutory neutrality.** No tax slab, PF rate, gratuity formula, festival-bonus rule or statutory
   leave entitlement appears as a constant in C#. A grep for such constants in `src/Modules/Hr/`
   returns nothing; every one is a row in an effective-dated policy table.
6. **Historical reproduction (rule 5).** Re-opening a locked payroll run for a past month reproduces
   its original figures exactly, because the pay structure and policy rows it used are still present
   and still dated — even after rates change.
7. **No financial hard deletes (rule 4).** A `Locked` payroll run is never edited. Corrections are a
   reversal run referencing the original; post-lock attendance corrections become arrears lines in
   the next run. Every payroll, attendance and ledger write is attributable and audited.
8. **Confidentiality.** `hr.read` never reveals pay. A department head approves leave without seeing
   salaries. A payslip is retrievable only by its owner or by a holder of `hr.salary.read`.
9. **Both-direction boot.** A database created by the HRM host boots cleanly under the ERP host
   (which migrates the other thirteen contexts). A database created by the ERP host is refused by the
   HRM host rather than being silently half-served.
10. **Tests at three levels** per `11-build-plan-phase2.md` §6: service integration tests against real
    Postgres, Playwright screen tests, and an end-to-end `eng/verify/hr-thread.py`; the ADR-0022
    upgrade-path gate passes with the new migrations; measured RSS recorded for both hosts.

## Out of scope (explicit deferrals)

| Deferred | Reason |
|---|---|
| **Live biometric device feed** (§13 I8, ZKTeco-class) | §9A.3 excludes it — the devices are not bought or installed. Wave A ships file import + manual entry behind an `IPunchSource` adapter; live polling is its own spec once a device exists, and would introduce this codebase's first background worker. |
| **Remote/GPS attendance and face recognition** `[obs: PiHR]` | Needs a mobile app or PWA with camera and location APIs — a different platform investment from server-rendered Razor Pages. Raised under P29 so the PM can price it. |
| **Task management** `[obs: PiHR]` | Not HR/payroll core; it is a lightweight project-management tool. Revisit only on explicit customer pull. |
| **Seeded BD statutory policy pack** | Blocked on verified sources (P26). The engine ships configurable and empty; fabricating slabs would breach rule 3. |
| **Posting into a real ledger** | M15 Accounts does not exist (Wave 4, unbuilt) and never will in the standalone SKU. Payroll owns its journal and exposes `IPayrollPosting`; M15 consumes it when it lands. |
| **Multi-branch resolution in the ERP host** | `BranchId` stays the existing constant there. The HRM host resolves branch per user (P28). |

## Risks / open questions

- **PRD internal conflict.** §11 describes a two-step leave chain (dept head → HR); §5A-16 asks for
  three-tier (Manager → HR → Dept-Incharge). Resolved in build by making tier count a policy row, so
  either shape is configuration. Flagged to the PM rather than silently picking one.
- **Sequencing.** M16 jumps ahead of its Wave-5 slot and ahead of M15. Accepted: the Accounts seam
  ships as a contract plus a journal export, wired to M15 when it exists.
- **Salary is a new sensitive-data class** in a codebase whose confidentiality rules were written for
  PHI. The permission split (`hr.read` vs `hr.salary.read`) is the mitigation and is a Wave-A
  requirement, not a later hardening pass.
- **Self-service implies every employee is a user account** — hundreds of new `AppUser` rows, with no
  email infrastructure for password reset in an offline-tolerant deployment. Admin-driven reset is
  therefore a Wave-A requirement.
- **The 2 vCPU / 3 GB ceiling** applies to both SKUs. Payslip PDFs render on demand rather than in
  batch; the standing abort criterion (sustained RSS > 2.2 GB) still governs.
</content>
