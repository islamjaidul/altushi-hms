# 0054 — M16 HR & Payroll raised to an industry-standard HRMS: gap analysis and module PRD

- **Status:** In Progress
- **Date:** 2026-08-06
- **PRD ref:** §5 M16, §5A-16, §5A-17, §3.4, §7, §10, §11, §12, §13 I8, §16
- **Parent:** `docs/specs/0034-hrm-product-line/` — this is the product-management answer to the
  waves 0034 deferred (B, C, D) plus everything the shipped Wave A revealed as missing.
- **Deliverable:** `docs/m16-hr-payroll-prd.md` (Module PRD v1.0) + `§5 M16` rewrite in the PRD.
- **MVP:** n/a — the §9A.2 freeze was lifted 2026-07-27. This is scope definition, not build.

## Problem

M16 shipped as the HRM SKU's Wave A (specs 0034–0037, 0039, 0052) and it works: employees,
attendance import and correction, rosters, leave, and a five-state payroll run that reconciles with
attendance and refuses to be locked twice. That is a *payroll engine with an HR record attached*. It
is not an HR & Payroll product a Bangladeshi employer would choose over PiHR, and it is not what
§5A-16/§5A-17 already committed the product to.

Three problems, in order of how badly they hurt a buyer:

**1. The module cannot report.** There is no reports screen in HR at all — eleven nav entries, zero
of them a report. There is no muster roll, no salary sheet a bank will accept, no leave register, no
attendance register, no headcount movement, no salary comparison, no PF/tax/welfare statement. The
data to produce every one of these is already in the schema. An HR officer cannot answer "show me
March" without a database client.

**2. Nothing in the module has a period.** `/hr/attendance` takes a single date. `/hr/payslips`
takes a run. The dashboard is hardcoded to *today* and *this month*. There is no way for an
administrator to select a year, a month, a week or an arbitrary range and see what happened — the
single most common thing an HR administrator does. Ten screens, ten different (or absent) notions of
"when".

**3. The employee record is six disconnected tables on one page.** `hr.employment_event` is already
append-only and attributable, and assignment, pay-structure, leave and attendance history all carry
dates — the substrate for a service-record timeline exists and is rendered as four separate tables
that cannot be read as one life.

Underneath those, the §5A-16/§5A-17 commitments are largely unbuilt (bonus, increments, promotion,
comp-off, OT bank, PF withdrawal, welfare/tax ledger screens, loans — the `hr.loan`,
`hr.loan_installment` and `hr.employee_ledger_entry` tables exist with **no screen at all**), and the
whole employment lifecycle after "hired" and before "separated" is missing: no probation
confirmation workflow, no employment type, no dependants or nominees, no professional-licence expiry
(in a *hospital* product), no clearance, no final settlement despite a gratuity rule table, no
document generation despite §5A-17 asking for three letters by name.

## Requirements

- [M] A gap analysis of the shipped module against an industry-standard HR & Payroll product,
  traced to what is actually in `src/Modules/Hr`, not to what the specs claimed.
- [M] A module PRD an Enterprise Software Architect can be handed without a conversation:
  scope in/out, personas, requirements with MoSCoW, user stories with acceptance criteria, state
  machines, data flows, a permission matrix, UX requirements, non-functionals, phasing, and
  traceability back to the main PRD.
- [M] **Reports and analytics** specified as a first-class capability with a named report inventory,
  a single period-selection standard, and export/print requirements.
- [M] **A period selector standard** — day / week / month / quarter / year / custom via calendar —
  binding on every report, dashboard, register and log in the module.
- [M] **Dashboards** specified for three audiences (HR administrator, manager, employee) with
  drill-through from every figure.
- [M] **An employee timeline** — one chronological service record spanning employment events,
  assignments, pay revisions, leave, attendance exceptions, documents, discipline and training.
- [M] **An administrator activity log** filterable by employee, action and calendar period.
- [M] Resolve the PRD's own §11-vs-§5A-16 contradiction on leave approval tiers, as a PM decision.
- [M] Business requirements only (CLAUDE.md rule 1) — no stack, schema, or screen technology.
- [M] No fabricated statute (rule 3): no Labour Act entitlement, NBR slab, PF rate or gratuity
  formula is asserted. All remain effective-dated employer configuration; the verified statutory
  pack stays an open question owned by the customer's legal counsel.
- [S] Scope decisions recorded for the things a "full HCM" would include and this module will not
  (recruitment/ATS, LMS, succession, mobile app), with the routing for each.

## Acceptance criteria

1. `docs/m16-hr-payroll-prd.md` exists, is self-contained, and states its scope boundary explicitly
   in both directions (what M16 owns; what it hands to M15/M20/M21/M6).
2. Every requirement carries a MoSCoW tag and traces to a §5/§5A reference or is marked as new scope
   with a rationale.
3. Every gap in the analysis names the current behaviour observed in the shipped module, not an
   assumption.
4. No BDT statutory rate, slab, entitlement or formula appears as a required constant anywhere in the
   PRD; a grep for such numbers finds only examples explicitly labelled as illustrative.
5. `docs/project_manager.md` §5 M16 is rewritten to match, its changelog bumped to v1.3, and it
   points at the module PRD as the expansion rather than duplicating it.
6. `docs/specs/README.md` carries this spec.
7. The §11 leave-approval contradiction is resolved in the PRD with a decision and a default.

## Out of scope

| Deferred | Reason |
|---|---|
| Implementation, architecture, schema, screens | This is a PM deliverable. The architect's answer belongs in `docs/architecture/`. |
| Recruitment / applicant tracking | A distinct buying decision and a distinct user base (candidates, who are not employees). Routed to the PM backlog as a candidate M23, not folded into M16. |
| Learning management (course delivery) | M16 records *that* training happened and when a certification expires; delivering the training is a different product. |
| Mobile / GPS / face attendance | Already deferred by 0034 under P29; restated in the PRD's open questions with a price-it-please note rather than silently absorbed. |
| A seeded Bangladesh statutory policy pack | Blocked on verified legal sources (P26). The PRD specifies the *shape* the pack must have and who signs it off. |

## Notes

The gap analysis was taken from the code as it stands on `main` at 2026-08-06 (spec 0053 merged),
by reading `src/Modules/Hr/**`, `src/Hms.Hr.Web/**`, the nav and permission registries, and the four
entity files — not from the specs, which describe intent. Where the two disagree, the code wins and
the PRD says so.
</content>
</invoke>
