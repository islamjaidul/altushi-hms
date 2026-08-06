# Module PRD — M16 HR & Payroll
## Raising the shipped module to an industry-standard HR & Payroll product

| | |
|---|---|
| **Document** | Module Product Requirements Document — expansion of PRD §5 M16 / §5A-16 / §5A-17 |
| **Prepared by** | ERP Project Manager |
| **Audience** | Enterprise Software Architect & Engineering Design Team |
| **Date** | 6 August 2026 |
| **Status** | v1.0 — for architect handoff |
| **Parent document** | `docs/project_manager.md` (PRD v1.3). This document expands §5 M16; it does not replace it. Where the two differ, this document governs M16. |
| **Spec** | `docs/specs/0054-hr-payroll-industry-standard/` |
| **Scope discipline** | **Business requirements only.** No stack, schema, framework, screen technology or deployment decision appears here — all are delegated to the architect (PRD §16). Where this document says "screen", it means *a place in the product where this job is done*, not a page implementation. |

---

## Table of Contents

1. [Why this document exists](#1-why-this-document-exists)
2. [Scope](#2-scope)
3. [Personas](#3-personas)
4. [Current state — what is actually shipped](#4-current-state--what-is-actually-shipped)
5. [Gap analysis](#5-gap-analysis)
6. [Product decisions taken in this PRD](#6-product-decisions-taken-in-this-prd)
7. [Requirements by capability area](#7-requirements-by-capability-area)
8. [User stories & acceptance criteria](#8-user-stories--acceptance-criteria)
9. [Workflow state definitions](#9-workflow-state-definitions)
10. [Data flows](#10-data-flows)
11. [Roles & permissions](#11-roles--permissions)
12. [UX requirements](#12-ux-requirements)
13. [Reports inventory](#13-reports-inventory)
14. [Dashboards](#14-dashboards)
15. [Employee timeline](#15-employee-timeline)
16. [Non-functional requirements](#16-non-functional-requirements)
17. [Delivery phasing](#17-delivery-phasing)
18. [Open questions for the customer & PM](#18-open-questions-for-the-customer--pm)
19. [Traceability](#19-traceability)

---

## 1. Why this document exists

M16 shipped. Employees, attendance import and correction, shift rosters, leave with approvals, and a
five-state payroll run that reconciles against attendance and refuses to be locked twice — all of it
works, and the money path was hardened as recently as 2026-08-06.

That is a **payroll engine with an employee record attached**. It is not yet a product an HR
department chooses, for three reasons a buyer notices in the first ten minutes:

1. **It cannot report.** Eleven menu entries, not one of them a report. No muster roll, no bank
   salary sheet, no leave register, no headcount movement, no PF statement. The data exists; there
   is no way to get it out.
2. **Nothing has a period.** Attendance takes one date. Payslips take one run. The dashboard is
   hardcoded to today. An administrator cannot select "March 2026", or "last financial year", and
   see what happened — which is most of what an HR administrator does all day.
3. **The employee record is four tables on one page.** Nobody can read a person's history as a
   history.

And the commitments the PRD already made in §5A-16 and §5A-17 — bonus, increments, promotion,
comp-off, OT bank, PF withdrawal, welfare and tax ledgers, loans and advances, appointment and
experience letters — are largely unbuilt. Three ledger tables exist with no screen at all.

This document defines what "industry standard" means for this product, in this market, and hands
the architect a complete brief.

---

## 2. Scope

### 2.1 What M16 owns

The **full employment relationship, from offer accepted to final settlement paid**, for any employer:

- The person, their employment, and everything true about it over time
- Time: attendance, shifts, rosters, overtime, holidays
- Absence: leave entitlement, application, approval, balance, encashment
- Money paid *to employees* (as distinct from money paid to doctors — that is M17): salary,
  allowances, deductions, bonus, increments, arrears, loans, advances, statutory contributions and
  final settlement
- The record of all of it: registers, reports, statements, dashboards, timeline and audit log
- Employee and manager self-service

### 2.2 What M16 does not own

| Not ours | Owner | The seam |
|---|---|---|
| Ledger posting, vouchers, chart of accounts | M15 Accounts | M16 hands over a balanced payroll journal on **Post**; M15 receives it. In the HRM-only SKU there is no M15, so the journal is retained and exportable. |
| Doctor/consultant fee shares and payouts | M17 | A doctor on payroll is an employee **and** may earn consultant shares. The two never net against each other inside M16. |
| Users, roles, permission grants, the approval engine | M21 | M16 declares its permissions and its approval workflows; M21 stores and routes them. |
| Sending SMS/email | M20 | M16 raises events; M20 owns templates, gateway and delivery log. |
| Ward duty assignment during a shift | M6 / R5 Nursing Station | M16 publishes the roster; the nursing station consumes it and records who was actually on the ward. Roster is the plan, ward duty is the fact. |
| Recruitment / applicant tracking | **Not built** — see §2.3 | — |

### 2.3 Explicitly out of scope, with routing

| Excluded | Why | Where it goes |
|---|---|---|
| **Recruitment / ATS** (requisition, job posting, candidate pipeline, interview scheduling, offer) | A different user base — candidates are not employees, and most of the value is external-facing. Bundling it doubles M16 without improving the module a buyer is actually comparing. | PM backlog as a candidate **M23 Recruitment**. M16 accepts a hire from it via a documented handover of candidate → employee. |
| **Learning management** (course content, delivery, quizzes) | A separate product category. | Out. M16 keeps §7.14's *training record and certification expiry* — the compliance half — only. |
| **Succession & workforce planning** | Enterprise HCM feature, no observed demand in the 50–300-bed market or in either competitor. | Out. |
| **Mobile app / GPS / face attendance** | Needs a platform investment (camera, location, offline sync) that is a product decision, not an M16 feature. Deferred since spec 0034 (P29). | Open question **Q-HR-6**, priced separately. |
| **Payroll for non-employees** (contractors billing invoices, agency staff) | These are supplier payments, not payroll. | M15 / M12 supplier payment. M16 does cover *contract-of-service employees* on the payroll — see §7.1. |

### 2.4 Two SKUs, one module

M16 ships in two products (established by ADR-0025, PM decision D3 of spec 0034):

- **Inside the hospital ERP** — HR & Payroll for the hospital's own staff, integrated with Accounts,
  Nursing Station and SMS.
- **As a standalone HRM product** sold to any Bangladeshi employer, hospital or not.

Every requirement in this document must hold in both. **Consequence:** M16 carries no clinical
vocabulary. There is no ward, no bed, no patient in HR. "Org unit" is whatever the customer calls
their departments. Hospital-specific requirements (professional licence expiry, roster feeding the
nursing station) are marked **[hospital]** and degrade cleanly to nothing when absent.

---

## 3. Personas

Extends PRD §4. Ages and computer comfort matter: PRD §7's UX principles are binding here too.

| # | Persona | Who they are | What they need from M16 |
|---|---|---|---|
| P-HR1 | **HR Officer / HR Manager** (30–50, moderate computer skill, the module's primary user) | Runs the department. Owns the employee file, attendance corrections, leave decisions and the monthly payroll. | To finish the month in a day. To answer any question about any employee, any period, without help. |
| P-HR2 | **HR Administrator / System Owner** (in the standalone SKU this is often the owner or GM) | Configures the product; answers the board's questions. | Dashboards, the full activity log by any period, and reports they can email to the MD. |
| P-HR3 | **Department Head / Line Manager** (35–55, low computer skill, uses the product weekly, not daily) | Approves their team's leave, publishes their roster, sees who is in today. | Three things on one screen, and never to see anyone's salary. |
| P-HR4 | **Employee** (any age, low computer skill, may share a device, may use only a phone browser) | Applies for leave, checks their payslip, disputes a punch. | Self-service that works on the first try, in plain words. |
| P-HR5 | **Accounts Officer / Accounts Manager** | Approves the payroll lock, receives the journal, pays the bank file. | A salary sheet that ties to the ledger and a bank file the bank accepts. |
| P-HR6 | **Managing Director / Owner** | Signs off payroll; watches cost. | Salary cost by department, month on month, on one screen, with the ability to drill to a person. |
| P-HR7 | **Payroll Auditor / external accountant** (occasional) | Verifies a past period. | To reproduce any historical run exactly, and to see who changed what and when. |

---

## 4. Current state — what is actually shipped

Observed in the codebase on 2026-08-06 (specs 0034–0037, 0039, 0052 merged). Recorded so the gap
analysis is measured against reality rather than intent.

### 4.1 Menu, as an operator sees it

Eleven flat entries in one group, "HR & Payroll": HR Dashboard · Employees · Attendance · Roster ·
Leave · Payroll · Payroll approvals · Payslips · Org structure · Policies · My Leave.

### 4.2 Capability actually present

| Area | Present | Notes |
|---|---|---|
| Employee record | Personal, contact, bank, TIN, join/confirm/separate dates, status, documents (as attachments), assignment history (unit/designation/grade, effective-dated), pay structure (effective-dated), append-only employment events | Rehire creates a new employment linked by person reference — service history is not merged. Good. |
| Org masters | Org unit tree, designation, grade, pay scale (effective-dated), pay component, work location, shift, weekly-off pattern, holiday calendar, leave type | Customer-defined; no hardcoded taxonomy. Good. |
| Attendance | Punch import from file or pasted rows; manual entry; day derivation with night-shift spanning, break deduction, late/early/OT minutes; exception-first review; correction with mandatory reason; post-lock corrections become arrears | Live device feed **not** built (devices not purchased). Import batches are recorded and re-import is a no-op. |
| Roster | Roster per org unit per period, shift per employee per day, publish | Per-employee-per-day assignment only. |
| Leave | Types, effective-dated policies (entitlement, accrual, carry-forward, sandwich, notice, max consecutive, attachment, **tier count**), balances with opening/accrued/availed/encashed/adjustment, applications with a state chain, paid vs unpaid | Encashment is modelled, has no screen. |
| Payroll | Runs (regular / supplementary / reversal) with state chain Generated → Exceptions Reviewed → Approved → Locked → Posted; per-employee lines pinning the pay structure and a policy stamp; component lines each carrying a human-readable basis; payslip issue; minimum-net-pay floor with the shortfall recorded as a recoverable advance; concurrency-guarded state transitions; journal retained for M15 | Historically reproducible. This is the strongest part of the module. |
| Policy configuration | Payroll policy (day-count convention, minimum net pay, rounding residue, leave-year start), tax slabs, PF policy, gratuity rule, overtime rules, deduction rules | Ships **empty** by design; payroll refuses until configured. Correct, and must stay so. |
| Self-service | Apply for leave; see own applications and balances | That is the entire ESS. |
| Permissions | 11 claims, with salary reading split from HR reading | The confidentiality split is right. |

### 4.3 What is not there at all

No reports screen. No period selection anywhere except a single date on attendance. No employee
timeline. No activity log. No loan, advance, PF, welfare or tax ledger screen (three tables, no UI).
No bonus, increment or promotion process. No comp-off or OT bank. No probation, clearance,
separation or final-settlement workflow. No employment type. No dependants or nominees. No
professional-licence tracking. No document generation. No notifications. No manager self-service.
No bank payout file.

---

## 5. Gap analysis

MoSCoW is against **"industry-standard HR & Payroll product for the Bangladesh market"**, not against
the existing build plan. Source column: the PRD reference that already committed us, or `new` where
this PRD adds scope (with the reason given in §6).

### 5.1 Reporting, analytics & period selection — the largest gap

| # | Gap | Today | Required | MoSCoW | Source |
|---|---|---|---|---|---|
| G1 | **No reports at all** | Zero report screens in M16 | A report centre with the inventory in §13, every report exportable and printable | **Must** | §5A-17, §5 M16 |
| G2 | **No period selection** | One date on attendance; everything else fixed to today/this month/one run | The §12.3 period standard — day, week, month, quarter, year, custom, by calendar — on every report, dashboard, register and log | **Must** | new (§7 U9 consistency) |
| G3 | **No activity/audit log surface** | Writes are audited; nothing displays them | An administrator log filterable by employee, actor, action, module area and period | **Must** | §5 M21, hard rule 4 |
| G4 | **No management analytics** | Headcount by unit, as a flat table | Trends, comparisons, cost-by-department, attrition, drill-through from every figure | **Must** | §5A-20 |
| G5 | **No scheduled/saved reports** | — | Save a report configuration; run it for a new period in one click | Should | new |

### 5.2 Dashboards

| # | Gap | Today | Required | MoSCoW | Source |
|---|---|---|---|---|---|
| G6 | **One dashboard, one audience** | Five tiles + a headcount table, for anyone with `hr.read` | Three dashboards: HR administrator, manager (my team), employee (me) | **Must** | §7 U1 |
| G7 | **Fixed to today** | Today and this month, hardcoded | Period-selectable with the §12.3 control; comparison to the previous period | **Must** | G2 |
| G8 | **Nothing is clickable** | Tiles are dead numbers | Every figure drills through to the list of rows behind it | **Must** | §7 U11 |
| G9 | **No alerts** | — | Action-required panel: probation due, contract expiring, licence expiring, documents expiring, unapproved leave ageing, attendance exceptions blocking payroll, payroll approaching cut-off | **Must** | new |
| G10 | **No calendar view** | — | Attendance/leave calendar (month grid, per person or per unit) as a primary view, not a report | **Must** | new |

### 5.3 Employee record & lifecycle

| # | Gap | Today | Required | MoSCoW | Source |
|---|---|---|---|---|---|
| G11 | **No timeline** | Four separate history tables on one page | One chronological service record — §15 | **Must** | new |
| G12 | **No employment type** | Every employee is the same kind of employee | Permanent / probationary / contract (with end date) / part-time / intern / daily-wage / consultant-on-payroll, each driving eligibility rules | **Must** | new |
| G13 | **No probation workflow** | A confirmation date field | Probation register, due-date alerting, extend/confirm decision with a record and a letter | **Must** | new |
| G14 | **No dependants / nominees** | — | Family members and PF/gratuity nominees with share percentages | **Must** | §5A-16 (PF) |
| G15 | **No qualifications / experience** | — | Education, prior experience, skills, references — the file HR keeps | Should | new |
| G16 | **No professional licence tracking** `[hospital]` | — | Registration/licence number, issuing body, validity, expiry alerting; blocks nothing but warns loudly | **Must** | new |
| G17 | **No asset issue/return** | — | What the employee holds (ID card, uniform, laptop, keys, phone) — issued, returned, or recovered in settlement | Should | new |
| G18 | **No separation workflow** | A separation date and a status | Resignation/termination/retirement with notice period, clearance checklist by department, exit interview, last working day vs relieving date | **Must** | new |
| G19 | **No final settlement** | A gratuity rule table with nothing that reads it | Final settlement statement: dues, leave encashment, gratuity, loan recovery, notice-pay adjustment, net payable, paid through an off-cycle run | **Must** | §5A-16 |
| G20 | **No document generation** | — | Appointment letter, confirmation letter, increment/promotion letter, experience certificate, termination letter, salary certificate, NOC — from templates the employer edits | **Must** | §5A-17 |
| G21 | **Documents have no expiry** | Attachments, untyped | Typed documents (NID, passport, certificate, licence, contract) with expiry and alerting | Should | new |

### 5.4 Time & attendance

| # | Gap | Today | Required | MoSCoW | Source |
|---|---|---|---|---|---|
| G22 | **No live device feed** | File import / paste / manual | Device registry + automatic collection, with import remaining the fallback | **Must** | §13 I8 |
| G23 | **No regularization request** | Only HR can correct, from the HR screen | Employee raises a correction request with a reason; manager or HR decides; the audited correction is the outcome | **Must** | new |
| G24 | **No overtime approval** | OT minutes are derived and paid per rule | Pre-approval or post-approval of OT before it is payable, per employer policy | **Must** | §5A-16 |
| G25 | **No OT bank / comp-off** | An unread `bank instead of pay` flag on the OT rule | OT bank ledger; comp-off earn, request, approve, expire | **Must** | §5A-16 |
| G26 | **No grace time** | Late minutes counted from the shift start | Configurable grace, and a late policy (n lates = 1 absent / a deduction) | **Must** | §5A-16 |
| G27 | **Roster is one cell at a time** | Per employee, per day | Roster templates, patterns (rotating N-shift cycles), copy-last-period, bulk assign, coverage warnings when a shift is understaffed | **Must** | §5 M16 ("24/7 rotating shifts") |
| G28 | **No shift-swap request** | — | Employee-to-employee swap with manager approval | Should | new |
| G29 | **No short leave / gate pass / outdoor duty** | An `errand` status nothing produces | Hourly short-leave and outdoor-duty requests that attendance honours | Should | §5A-16 |
| G30 | **No attendance register view** | Day-at-a-time list | Monthly muster-roll matrix (employee × day) — the format every Bangladeshi HR office already uses on paper | **Must** | new |
| G31 | **Holiday calendar has no screen** | Entities only | Manage calendars and holidays; assign per unit/location; year rollover | **Must** | §5 M16 |

### 5.5 Leave

| # | Gap | Today | Required | MoSCoW | Source |
|---|---|---|---|---|---|
| G32 | **No leave calendar** | A pending-decision list | Team and organisation leave calendar; who is out, when, and the clash warning before approving | **Must** | new |
| G33 | **No year-end process** | Balances exist; nothing rolls them | Leave-year close: accrue, cap carry-forward, lapse, encash, open the new year — reviewable before it commits | **Must** | §5A-16 |
| G34 | **No encashment screen** | Modelled, no UI | Encashment request → approval → paid on the salary sheet | **Must** | §5A-16 |
| G35 | **No balance adjustment surface** | An adjustment field with no writer | Adjust a balance with a mandatory reason, audited | **Must** | new |
| G36 | **No approver delegation** | The chain stops when the approver is on leave | Delegate approvals for a date range; escalate after n days | Should | §5A-21 |
| G37 | **No cancellation after approval** | Modelled state, no path | Withdraw/cancel approved leave before or after it is availed, restoring the balance, with an audit line | **Must** | new |

### 5.6 Payroll & compensation

| # | Gap | Today | Required | MoSCoW | Source |
|---|---|---|---|---|---|
| G38 | **No bonus** | — | Bonus register (festival, performance, ad-hoc), eligibility by service/type, generate a bonus sheet, approve, pay in-run or off-cycle | **Must** | §5A-16, §5 M16 |
| G39 | **No increment process** | Pay structure can be edited one person at a time | Increment policy, an increment run (by grade, by rating, by percentage or amount), preview → approve → effective-dated apply → letters | **Must** | §5A-16 |
| G40 | **No promotion process** | An assignment row can be added | Promotion as one action: designation + grade + pay + effective date + letter + timeline entry | **Must** | §5A-16 |
| G41 | **No loan/advance screens** | Two tables, no UI | Request → approve → disburse → automatic installment recovery → outstanding statement → foreclose/write-off | **Must** | §5 M16 `[S]`, §5A-16 |
| G42 | **No PF member surface** | A ledger table with no screen | PF member statement, opening balance, contributions, interest posting, withdrawal/advance request and settlement | **Must** | §5A-16 |
| G43 | **No welfare / tax ledger screens** | Same table, no UI | Member statements per ledger kind | **Must** | §5A-16 |
| G44 | **No tax statement or TDS output** | Tax slabs exist and compute | Per-employee annual tax computation statement; monthly TDS deduction register; the treasury-deposit output §3.4 requires | **Must** | §3.4, §5A-15 |
| G45 | **No bank payout file** | Bank fields exist on the employee | Salary disbursement batch: bank transfer file, cash sheet, cheque list; disbursement status per employee | **Must** | §5A-15, §3.4 |
| G46 | **No salary hold** | — | Hold an individual's salary with a reason; release later; never silently omit a person from a run | **Must** | new |
| G47 | **No variance control** | Totals only | Month-on-month variance report per employee and per component, with the reason for every change, reviewed **before** approval | **Must** | new |
| G48 | **No payroll cut-off / calendar** | Runs whenever | An attendance cut-off date per period, and a payroll calendar showing what is due when | Should | new |
| G49 | **Payslip is a table row** | A list of lines with amounts | A proper payslip document — employer header, period, earnings/deductions, YTD, leave balance, net in words — printable and distributable | **Must** | §5 M16, §7 U10 |
| G50 | **No cost allocation** | Lines carry the org unit | Salary cost by unit / location / cost centre, and the journal split accordingly | Should | §5A-13 |
| G51 | **No arrears visibility** | Arrears are generated; nothing lists them | Pending-arrears register: what will be paid next run and why | Should | new |

### 5.7 Self-service & manager service

| # | Gap | Today | Required | MoSCoW | Source |
|---|---|---|---|---|---|
| G52 | **ESS is leave-only** | Apply for leave; see balances | My profile, my attendance and punches, my payslips and tax statement, my leave, my loans, my documents, my requests, my timeline | **Must** | §5 M16 `[S]`, §5A-16 |
| G53 | **No manager service** | Managers use the HR screens | A manager space: my team today, my approvals, my roster, my team's leave calendar, my team's exceptions — and never a salary | **Must** | §12 |
| G54 | **No profile change request** | HR edits everything | Employee proposes a change (phone, address, bank, emergency contact); HR approves; the change is audited | Should | new |
| G55 | **No notice board** | — | Employer announcements with acknowledgement | Could | §5A-16 `[obs: PiHR]` |
| G56 | **No expense claim** | — | Claim → approve → reimburse through payroll or petty cash | Could | §5A-16 `[obs: PiHR]` |

### 5.8 Compliance, notification & governance

| # | Gap | Today | Required | MoSCoW | Source |
|---|---|---|---|---|---|
| G57 | **No notifications** | — | Leave decided, roster published, payslip ready, probation due, licence expiring, loan closing, birthday/anniversary — via M20 | **Must** | §5 M20, §5A-16 `[C]` |
| G58 | **No statutory registers** | — | The employer's statutory register set as configurable report templates — *content to be supplied and signed off by the customer's counsel*, never authored by us | **Must** | §3.4, hard rule 3 |
| G59 | **No training / certification record** `[hospital]` | — | Training attended, certification held, expiry, and who is out of compliance | Should | new |
| G60 | **No appraisal** | — | A light appraisal cycle: period, form, self + manager rating, outcome feeding the increment run | Should | new |
| G61 | **No disciplinary record** | — | Show-cause, warning, suspension, resolution — on the timeline, permission-restricted | Should | new |
| G62 | **No data retention / privacy statement** | — | Retention period for separated employees; what a separated employee can still see | Should | §8 N5 |

---

## 6. Product decisions taken in this PRD

These are PM decisions. The architect may raise a cost objection to any of them; none is theirs to
change unilaterally.

| # | Decision | Rationale / consequence |
|---|---|---|
| **D1** | **Leave approval tiers are employer configuration, 1 to 3, defaulting to 2.** §11's two-step chain (dept head → HR) is the default; §5A-16's three-tier chain (manager → HR → dept in-charge) is the same mechanism with one more tier. | Resolves the contradiction the build flagged in spec 0034. §11 of the main PRD is amended accordingly. Neither shape is hardcoded. |
| **D2** | **No statutory rate, slab, entitlement or formula is ever shipped by us.** Every one is effective-dated employer configuration. A "Bangladesh statutory pack" may exist as *customer-supplied, counsel-signed content* with a version and an effective date — imported, never embedded. | Hard rule 3. Also the honest commercial answer: we are not the customer's tax adviser, and a wrong slab shipped by us is our liability. The product must say plainly which policy is missing and refuse to run rather than guess. |
| **D3** | **Reporting is a capability, not a screen.** Every register in §13 shares one period control, one filter grammar, one export set, one print layout, one permission model. | A hospital HR officer who learns one report has learned all of them (§7 U9). It also stops thirty reports becoming thirty inconsistent screens. |
| **D4** | **Every number is a door.** No figure on any dashboard, tile, chart or summary row may be a dead end; each drills to the rows that produced it, in the same period. | This is what makes a dashboard trustworthy rather than decorative, and it is the fastest audit tool the module can have. |
| **D5** | **The employee timeline is the employee record.** The profile screen is a summary; the timeline is the truth, and it is printable as a Service Record. | §15. Also the answer to "what happened to this person" — today it takes four tables and a memory. |
| **D6** | **Salary confidentiality is absolute and structural.** Seeing a person (`hr.read`) never reveals what they earn (`hr.salary.read`). This extends to every new surface: reports, dashboards, timeline, exports, notifications, logs. | Already true in the build. Made a standing requirement so nothing new leaks it. A department head approving leave must never see a salary. |
| **D7** | **The module ships for a non-hospital employer too.** Hospital-only features are additive and marked `[hospital]`; their absence must not leave a broken screen. | ADR-0025 / spec 0034 D3. |
| **D8** | **Nothing is deleted.** Corrections are reversals or superseding effective-dated rows, everywhere — not only in payroll. A cancelled leave, an adjusted balance, a withdrawn increment all leave a record. | Hard rule 4, extended from finance to the whole employment record. |
| **D9** | **Attendance and payroll are separated by a cut-off, not by hope.** Each period has an attendance cut-off date after which corrections become arrears rather than edits. | Already the behaviour for locked runs; made explicit and visible so HR can plan the month. |
| **D10** | **Recruitment is not part of M16** (§2.3), and its absence must not block hiring: an employee can always be created directly. | Keeps the module a decision a buyer can make in one meeting. |

---

## 7. Requirements by capability area

Notation: `[M]` Must · `[S]` Should · `[C]` Could · `[hospital]` hospital-specific.
`(G##)` cross-references the gap analysis.

### 7.1 Core HR — the employee record

- `[M]` Employee master: personal, contact, identification, emergency contact, blood group, bank
  details, tax identifier, photograph. **(shipped)**
- `[M]` **Employment type** on every employment — permanent, probationary, contract (with an end
  date), part-time, intern, daily-wage, consultant-on-payroll — driving leave eligibility, payroll
  treatment and settlement rules. (G12)
- `[M]` Effective-dated assignment: org unit, designation, grade, work location, reporting manager,
  weekly-off pattern, holiday calendar. **(shipped)**
- `[M]` Effective-dated pay structure with earning, deduction and employer-contribution components.
  **(shipped)**
- `[M]` **Probation lifecycle**: probation period on hire, a probation-due register, extend (with a
  new due date and a reason) or confirm — each producing a timeline entry and a letter. (G13)
- `[M]` **Dependants and nominees**: family members, and nominees for PF/gratuity with share
  percentages that must total 100%. (G14)
- `[M]` **Professional registration** `[hospital]`: licence/registration body, number, issue and
  expiry date, with expiry alerting. Never blocks; always warns. (G16)
- `[M]` **Typed documents with expiry**: NID, passport, contract, certificate, licence, photograph —
  each with an issue/expiry date where relevant and an alert before expiry. (G21)
- `[M]` **Employee code and identity**: never reused; a rehire is a new employment linked to the same
  person, and the timeline shows both. **(shipped)**
- `[S]` Qualifications, prior experience, skills, references. (G15)
- `[S]` **Asset issue and return**: what the employee holds, when issued, when returned; unreturned
  assets surface at clearance. (G17)
- `[S]` Employee photo on every screen that names them — recognition beats reading for P-HR3.
- `[C]` Employee grouping/tagging for ad-hoc cohorts (e.g. "night-shift eligible").

**Separation & final settlement**

- `[M]` **Separation initiation** with kind (resignation / termination / retirement / end of contract
  / death), notice period, intended last working day. (G18)
- `[M]` **Clearance checklist** routed to the departments that must sign off (HR, Accounts, IT,
  Store, department head), each with a status, a note and an attributable sign-off.
- `[M]` **Final settlement statement**: unpaid salary to the last working day, leave encashment,
  gratuity per the employer's rule, bonus proration, loan and advance recovery, notice-pay
  adjustment either way, asset recovery, PF settlement — netted to one payable, approved, and paid
  through an off-cycle run. (G19)
- `[M]` **Relieving and experience documents** issued on settlement. (G20)
- `[S]` Exit interview record, permission-restricted.
- `[M]` A separated employee's history is never deleted and remains fully reportable. (D8)

### 7.2 Organisation

- `[M]` Org unit tree, designation, grade, pay scale, work location, shift, weekly-off pattern,
  holiday calendar, leave type, pay component. **(shipped)**
- `[M]` **Holiday calendar management** with a year rollover and per-unit/per-location assignment.
  (G31)
- `[M]` Unit head assignment, driving the approval chain. **(shipped)**
- `[S]` **Org chart view** — the reporting tree, navigable, printable.
- `[S]` Position/sanctioned-strength per unit, so vacancy is a number the dashboard can show.
- `[C]` Cost centre distinct from org unit, for employers whose finance structure differs from their
  management structure.

### 7.3 Employee timeline & service record

Specified in full in **§15**. `[M]` (G11)

### 7.4 Time & attendance

- `[M]` Punch capture from **biometric/access devices** — a device registry, automatic collection,
  a per-device last-seen and health indication, and a plain statement when a device has stopped
  reporting. File import and manual entry remain, as fallback and for sites without devices. (G22)
- `[M]` Day derivation from punches against the rostered shift, including night shifts that cross
  midnight, break deduction, late, early-out and overtime minutes. **(shipped)**
- `[M]` **Grace time** and a **late policy** (n lates in a period equal an absence or a defined
  deduction), both effective-dated configuration. (G26)
- `[M]` Exception-first review with mandatory-reason correction, audited; corrections after the
  period's cut-off become arrears. **(shipped, G9/D9)**
- `[M]` **Attendance regularization request** raised by the employee, decided by manager or HR,
  producing the same audited correction. (G23)
- `[M]` **Monthly attendance register (muster roll)**: employee × day matrix with a status code per
  cell, totals per employee and per day, printable on A4 landscape. (G30)
- `[M]` **Attendance calendar** for one employee: a month grid, colour- and letter-coded, with the
  punches behind any day one click away. (G10)
- `[M]` **Overtime approval**: per employer policy, OT is payable only when approved — pre-approved
  by request, or post-approved from the exception list. (G24)
- `[M]` **OT bank and comp-off**: overtime banked instead of paid; comp-off earned, requested,
  approved, and expiring after a configured window. (G25)
- `[S]` **Short leave / gate pass / outdoor duty** — an hourly absence request that attendance
  honours without consuming a leave day. (G29)
- `[S]` Manual bulk attendance entry for sites with no device at all (a day's register, entered as a
  sheet).
- `[C]` Attendance summary posted back to the employee by SMS on request.

### 7.5 Shift & roster

- `[M]` Shifts with start, end, next-day flag, break, pairing tolerance and standard minutes.
  **(shipped)**
- `[M]` **Roster templates and rotation patterns** — define a cycle (e.g. 6 days morning, 1 off, 6
  evening), apply it to a team over a period. (G27)
- `[M]` **Copy the previous period**, then amend — the way rosters are actually made.
- `[M]` **Bulk assignment**: select people, select days, set a shift.
- `[M]` **Coverage view and warnings**: per shift per day, required versus rostered, flagged when
  short; and a warning when a rostered person is on approved leave.
- `[M]` **Publish** makes the roster visible to employees and, `[hospital]`, available to the
  Nursing Station as the duty plan. **(publish shipped; the feed is R5's)**
- `[S]` **Shift swap request** between two employees, with manager approval. (G28)
- `[S]` Roster printable per unit for the notice board — a real requirement in this market.

### 7.6 Leave & absence

- `[M]` Employer-defined leave types and effective-dated policies: entitlement, accrual, carry
  forward, encashability, sandwich-day treatment, notice, maximum consecutive days, attachment
  requirement, approval tiers. **(shipped)**
- `[M]` Balances explained rather than asserted — opening, accrued, availed, encashed, adjusted, and
  what remains. **(shipped)**
- `[M]` Application → approval chain (1–3 tiers, D1) → availed, with a rejection reason, and unpaid
  leave deducting per the employer's rate. **(shipped)**
- `[M]` **Leave calendar** — team, unit and organisation views, month grid, with a clash warning
  shown to the approver *before* they approve. (G32)
- `[M]` **Cancellation and withdrawal** of an approved or availed application, restoring the balance,
  audited. (G37)
- `[M]` **Balance adjustment** with a mandatory reason. (G35)
- `[M]` **Leave-year close**: a previewable, reversible-before-commit process that accrues, caps
  carry-forward, lapses the excess, encashes what the policy allows, and opens the new year. (G33)
- `[M]` **Encashment** request → approve → paid on the salary sheet. (G34)
- `[M]` Half-day leave. **(shipped)** `[S]` hourly leave (see 7.4 short leave).
- `[S]` **Approver delegation** for a date range, and escalation after n days pending. (G36)
- `[S]` Leave liability valuation — what the carried balances would cost if encashed. Finance asks
  for this every year-end.

### 7.7 Payroll

- `[M]` Salary sheet generated from attendance, with every exception pre-listed for review, and every
  deduction traceable to an attendance event or an entry. **(shipped)**
- `[M]` Run states Generated → Exceptions Reviewed → Approved → Locked → Posted, with maker-checker
  separation, and reversal as the only way to change a locked run. **(shipped)**
- `[M]` Regular, supplementary and off-cycle runs; arrears from post-cut-off corrections. **(shipped)**
- `[M]` **Variance review before approval**: this period versus last, per employee and per component,
  with a reason required for anything outside a configured tolerance. Approval is not offered until
  variance has been looked at. (G47)
- `[M]` **Salary hold and release** per employee, with a reason; a held person appears on the sheet as
  held, never silently absent. (G46)
- `[M]` **Bonus**: a bonus register (festival, performance, ad-hoc), eligibility by employment type
  and service length, computed on a configurable basis, generated as a sheet, approved, and paid in
  the run or off-cycle. (G38)
- `[M]` **Increment run**: select a cohort, apply a policy (percentage, amount, or grade movement),
  preview every affected person with old and new, approve, apply effective-dated, generate letters.
  (G39)
- `[M]` **Promotion**: one action changing designation, grade and pay together with an effective
  date, a letter and a timeline entry. (G40)
- `[M]` **Payslip document** — employer letterhead, period, employee and assignment identification,
  earnings and deductions with a readable basis for each, employer contributions where the employer
  chooses to show them, net pay in figures and words, period and year-to-date columns, leave
  balances, and the payment method. Printable on A4, distributable individually or in bulk. (G49)
- `[M]` **Disbursement**: a salary disbursement batch per run, split by payment method — bank
  transfer file in the format the customer's bank accepts, cash payment sheet with a signature
  column, cheque list — and a per-employee paid/unpaid status. (G45)
- `[M]` **Journal to Accounts** on Post, balanced, with the employer-cost side included; retained and
  exportable where M15 is absent. **(shipped)**
- `[S]` **Cost allocation** by org unit / location / cost centre, in the journal and in reporting.
  (G50)
- `[S]` **Payroll calendar and cut-off** per period, visible to HR, with a checklist of what must be
  done before the run. (G48)
- `[S]` **Pending arrears register** — what will be paid next run, and why. (G51)
- `[C]` Payslip delivery by email/SMS link, respecting D6.

### 7.8 Statutory & contributions

Everything here is **employer configuration** (D2). The product supplies the mechanism and the
statement; the customer supplies the numbers and signs them off.

- `[M]` **Provident fund**: eligibility, employee and employer share, cap; a member statement with
  opening balance, contributions, interest and withdrawals; withdrawal and advance requests with
  approval and settlement. (G42)
- `[M]` **Income tax deducted at source**: an effective-dated slab table, a per-employee annual
  computation statement showing taxable income and the tax spread across the year, a monthly
  deduction register, and the treasury-deposit output §3.4 requires. (G44)
- `[M]` **Gratuity**: the employer's rule, a liability figure per employee, and the settlement
  calculation at separation. (G19)
- `[M]` **Welfare fund and any other employer ledger**: member statements. (G43)
- `[M]` **Statutory registers** as configurable report templates whose content is supplied by the
  customer's counsel, versioned and dated. We ship the template mechanism and none of the content.
  (G58, hard rule 3)
- `[M]` The configuration screen must say plainly **which policy is missing and what will not run
  until it is set**. **(shipped — keep it)**

### 7.9 Loans, advances & reimbursements

- `[M]` **Loan and advance**: request → approve → disburse → automatic installment recovery in
  payroll → outstanding statement → foreclosure or write-off with approval. Recovery respects the
  minimum-net-pay floor and defers rather than forcing a negative net. (G41)
- `[M]` **Recoverable-advance visibility**: anything the employer is owed by an employee (including a
  carried payroll shortfall) appears on one outstanding statement, and at final settlement.
- `[C]` **Expense claims**: submit with attachment → approve → reimburse via payroll or petty cash.
  (G56)

### 7.10 Employee self-service (ESS)

The employee's whole relationship with HR, on one space, usable on a phone browser (P-HR4).

- `[M]` My profile, with a **change request** for the fields the employer allows (contact, address,
  emergency contact, bank), approved by HR before it takes effect. (G54)
- `[M]` My attendance: this month's calendar, my punches, my late/OT totals, and a
  **regularization request** where I disagree.
- `[M]` My leave: balances explained, apply, my applications and their state, my team's leave
  calendar (subject to permission), cancel/withdraw.
- `[M]` My payslips: any period, printable; my annual tax statement; my PF statement.
- `[M]` My loans and advances: outstanding, schedule, what will be deducted next month.
- `[M]` My documents: what HR holds, what is expiring, and my letters.
- `[M]` My requests: one inbox for everything I have raised and its state.
- `[S]` My timeline — the employee's own view of §15.
- `[C]` Notice board with acknowledgement. (G55)

### 7.11 Manager self-service (MSS)

- `[M]` **My team today**: present, absent, late, on leave, on which shift — one screen. (G53)
- `[M]` **My approvals**: leave, regularization, OT, comp-off, shift swap, expense — one queue,
  each with the context needed to decide (balance, clash, history).
- `[M]` **My roster**: build, copy, publish for my unit.
- `[M]` **My team's calendar**: attendance and leave, month grid.
- `[M]` **My team's exceptions** ahead of payroll cut-off.
- `[M]` **No salary, anywhere in MSS**, unless the manager separately holds salary read. (D6)
- `[S]` My team's timeline entries relevant to me (probation due, contract ending, licence expiring).

### 7.12 Reports & analytics

Specified in full in **§13** (inventory) and **§12.3–12.5** (the period, filter, export and print
standards). `[M]` (G1, G2, G4, G5)

### 7.13 Dashboards

Specified in full in **§14**. `[M]` (G6–G10)

### 7.14 Administration, audit & notification

- `[M]` **Activity log**: every write in M16 — who, when, what changed from what to what, and the
  reason where one was required — filterable by employee, actor, action, area and **period, by
  calendar** (day / month / year / range). Exportable. Never editable. (G3)
- `[M]` **Per-employee audit**: the same log, scoped to one person, reachable from their record.
- `[M]` **Notifications** raised to M20 for: leave decided, roster published, payslip ready, probation
  due, contract expiring, licence/document expiring, loan closing, attendance exception unresolved
  near cut-off, birthday and work anniversary. Each independently switchable by the employer. (G57)
- `[M]` **Permission model** extended per §11, with salary confidentiality preserved on every new
  surface. (D6)
- `[S]` **Training and certification record** with expiry and a non-compliance list. `[hospital]`
  (G59)
- `[S]` **Appraisal cycle**: define a period and a form, collect self and manager ratings, record an
  outcome, feed the increment run. (G60)
- `[S]` **Disciplinary record**: show-cause, warning, suspension, resolution — permission-restricted,
  on the timeline. (G61)
- `[S]` **Retention policy** for separated employees: how long the record is kept, and what the
  separated person can still reach. (G62)

---

## 8. User stories & acceptance criteria

Continues the PRD's US16.x numbering. US16.1–US16.3 remain as written in §5 M16.

**US16.4 — The administrator's calendar.**
As an HR Administrator, I want to pick any day, month, quarter or year from a calendar and see
everything that happened in that period, so that answering a question never requires knowing which
screen it lives on.
**AC:** One period control, in the same place, on every report, register, dashboard and log. Its
selection persists as I move between them. The resolved range is always stated in words with the
timezone. Selecting a future period returns an empty result with a sentence, never an error.

**US16.5 — The morning dashboard.**
As an HR Officer, I want the dashboard to tell me what needs me today and let me act from it, so that
I start work without hunting.
**AC:** Every tile is clickable and lands on the rows behind it for the same period. An
"action required" panel lists exceptions blocking payroll, leave ageing past its notice period,
probations and contracts due, and expiring licences and documents — each with a count and a link.
The panel is empty when there is nothing to do, and says so.

**US16.6 — The employee timeline.**
As an HR Officer, I want one screen showing everything that ever happened to an employee, in order,
so that a query about a person is answered by reading rather than by remembering.
**AC:** One chronological stream covering employment events, assignments, pay revisions, leave,
attendance exceptions and corrections, documents, loans, discipline and training. Filterable by
category and by year. Every entry names who recorded it and when. Salary-bearing entries are hidden
without salary read. Printable as a Service Record.

**US16.7 — Payroll I can defend.**
As an HR Officer, I want to see what changed since last month before I send the payroll for approval,
so that I never explain a mistake after it is paid.
**AC:** A variance view per employee and per component, this period against last, with the reason
for each difference. Anything beyond a configured tolerance must be acknowledged before the run can
be sent for approval.

**US16.8 — The bank does not argue.**
As an Accounts Officer, I want the approved payroll to produce a bank transfer file, a cash sheet and
a cheque list, so that paying 200 people is one upload and two signatures.
**AC:** The file is in the format the customer's bank accepts; every employee is on exactly one
payment method; totals reconcile to the run to the taka; each person's paid status is recorded.

**US16.9 — I punched, the machine did not.**
As an Employee, I want to raise a correction for a day the device got wrong and see it decided, so
that I do not lose pay by argument.
**AC:** I can raise it from my own attendance view with a reason; my manager or HR sees it in one
queue with my punches beside it; the decision is audited and the corrected day flows into payroll or
into arrears if the period has closed. I am told the outcome.

**US16.10 — Nobody leaves owing.**
As an HR Officer, I want a separation to walk a clearance and produce one settlement statement, so
that nothing is discovered after the person has gone.
**AC:** Clearance is per department, each signed off attributably. The statement nets dues, leave
encashment, gratuity, loan recovery, notice pay and unreturned assets to one figure. It is approved
before payment and paid through an off-cycle run. Relieving and experience letters issue from it.

**US16.11 — The roster is made in ten minutes.**
As a Department Head, I want to copy last month's roster and amend it, and be told where I am short,
so that a 24/7 unit is scheduled without a spreadsheet.
**AC:** Copy-previous, pattern-apply and bulk-assign all exist. A shift below its required strength
is flagged before publish. Rostering someone who is on approved leave is refused with the reason.
Publishing notifies the team.

**US16.12 — Leave without a clash.**
As a Department Head, I want to see who else is off before I approve, so that I do not empty my unit.
**AC:** The approval screen shows the team's calendar for the requested dates and warns when
approving breaches a configured minimum on duty. The warning does not block; it is recorded with the
decision.

**US16.13 — My money, my proof.**
As an Employee, I want my payslips, my tax statement and my PF statement without asking HR, so that
routine questions stop being conversations.
**AC:** I see only my own; any period; printable; no other employee's figure is reachable by any URL,
export or search.

**US16.14 — The board's question.**
As a Managing Director, I want salary cost by department, this month against last and against the
same month last year, and to click into any figure, so that I understand cost movement without asking
for a spreadsheet.
**AC:** One screen; three comparisons; drill-through to the employees behind any number; export.

**US16.15 — Who changed that?**
As an HR Administrator, I want to select a period on a calendar and see every change made in HR in
it, by whom, so that any dispute has a documentary answer.
**AC:** Every M16 write appears, with before and after and the reason where required. Filterable by
employee, actor and action. Exportable. Nothing in the log is editable by anyone.

**US16.16 — The licence that expired.** `[hospital]`
As an HR Officer, I want to be warned before a clinical staff member's registration expires, so that
nobody works unregistered.
**AC:** Expiry appears on the dashboard from a configurable lead time, on the employee's record and
in a compliance report. The employee and their manager are notified. It warns; it never blocks a
roster silently.

---

## 9. Workflow state definitions

Extends PRD §11. `⚿` marks a state requiring approval.

| Object | States |
|---|---|
| **Employment** | Offer accepted → Joined → **Probation** → Confirmed⚿ *(or Probation Extended⚿ → Confirmed⚿)* → *(Promoted / Transferred / Incremented — events, not states)* → Notice Served → **In Clearance** → Relieved → **Settled**⚿ · terminal variants: Terminated⚿, Retired, Contract Ended, Deceased · re-entry: Rehired (a **new** employment, linked to the same person) |
| **Leave application** | Applied → *(tier 1)* Recommended → *(tier 2)* Recommended → Approved⚿ / Rejected → Availed → *(Cancelled / Withdrawn⚿ — restores balance)* · **tier count is employer configuration, 1–3, default 2 (D1)** |
| **Attendance day** | Derived → *(Exception: Incomplete / Absent-with-punch / Unapproved-OT)* → Reviewed → **Corrected**⚿ *(reason required)* → Locked-by-payroll → *(post-lock correction → Arrears Pending → Arrears Paid)* |
| **Regularization request** | Raised(employee, reason) → Recommended(manager) → Approved⚿ / Rejected(HR) → applied as an audited correction |
| **Overtime** | Derived → *(Pre-approved⚿ or Post-approved⚿)* → Payable / Banked → *(Banked → Comp-off Available → Availed / Expired)* |
| **Roster** | Draft → *(Coverage checked)* → Published → *(Amended⚿ after publish, notified)* |
| **Payroll run** | Generated → Exceptions Reviewed → **Variance Reviewed** → Approved⚿ → Locked → Posted to Accounts → **Disbursed** · off-path: Cancelled (before lock), Reversed⚿ (after lock, by a referencing reversal run) |
| **Bonus sheet** | Drafted → Eligibility Reviewed → Approved⚿ → Attached to a run / Paid off-cycle |
| **Increment run** | Proposed → Previewed → Approved⚿ → Applied (effective-dated) → Letters Issued |
| **Loan / advance** | Requested → Approved⚿ → Disbursed → Recovering *(per installment; deferred where the net-pay floor bites)* → Closed · off-path: Foreclosed⚿, Written Off⚿ |
| **Leave-year close** | Proposed → Previewed (per employee: accrue, carry, lapse, encash) → Committed⚿ · never partially applied |
| **Final settlement** | Initiated → Clearance In Progress → Computed → Approved⚿ → Paid → Documents Issued |
| **Profile change request** | Raised(employee) → Approved⚿ / Rejected(HR) → applied, audited |

**Cross-role approvals** to add to §12's table:

| Workflow | Requester | Approver(s) |
|---|---|---|
| Leave (1–3 tiers) | Employee | Manager → HR → *(optional)* Department In-charge |
| Attendance regularization | Employee | Manager → HR Officer |
| Overtime | Employee / Manager | Manager (pre) or HR Officer (post) |
| Payroll run lock | HR Officer | Accounts Manager / MD **(shipped)** |
| Payroll reversal | HR Officer | Accounts Manager **and** MD |
| Bonus sheet | HR Officer | Accounts Manager / MD |
| Increment / promotion | HR Officer | Department Head → MD |
| Loan / advance | Employee | Manager → HR → Accounts Manager |
| Leave-year close | HR Officer | HR Manager |
| Leave balance adjustment | HR Officer | HR Manager |
| Final settlement | HR Officer | Accounts Manager → MD |
| Salary hold / release | HR Officer | HR Manager / MD |

---

## 10. Data flows

Extends PRD §6.

| From → To | Business event | What crosses |
|---|---|---|
| **Attendance → Payroll** | Period generation | Per-employee payable days, present/absent/leave/unpaid-leave days, late count, approved OT minutes. Every payroll deduction traces back to a named attendance day. **(shipped)** |
| **Leave → Attendance** | Leave approved | The days become `on leave`; unpaid types drive the without-pay deduction. **(shipped)** |
| **Leave → Payroll** | Encashment approved | An earning line on the sheet. |
| **Loans → Payroll** | Run generation | An installment deduction line, deferred rather than forced where the net-pay floor bites. **(partially shipped)** |
| **Payroll → Accounts (M15)** | Run posted | A balanced journal: salary expense (allocated by cost centre where configured), statutory and employer-contribution liabilities, net-pay payable, advance recovery. Retained and exportable where M15 is absent. **(shipped)** |
| **Payroll → Bank** | Run disbursed | A bank transfer batch per §3.4/§5A-15, plus cash and cheque sheets. |
| **Payroll → Ledgers** | Run posted | PF, welfare and tax member entries — the statements employees can read. |
| **Roster → Nursing Station (M6/R5)** `[hospital]` | Roster published | Who is planned on duty, by ward, shift and day; the nursing station records who was actually there. Plan and fact stay distinct. |
| **M16 → SMS/Notification (M20)** | §7.14's event list | Recipient, template, variables. M20 owns delivery. |
| **M21 → M16** | Always | Users, roles, permission grants, approval routing and delegation. |
| **M16 → M21 audit** | Every write | Actor, time, entity, before/after, reason. Append-only. |
| **Recruitment (future M23) → M16** | Candidate hired | A documented handover creating an employment. Never a hard dependency. |

**Standing obligation:** a doctor who is both an employee (M16 salary) and a consultant (M17 fee
share) is paid twice, from two modules, and the two are never netted inside M16. Any statement that
shows "total paid to Dr X" is a reporting concern spanning both, not an M16 feature.

---

## 11. Roles & permissions

Extends PRD §12. The existing eleven permissions are kept; the additions below are what the new
capability needs. **Rule D6 governs everything: no permission other than salary-read may reveal a
pay figure, on any surface — screen, report, export, notification, log or timeline.**

| Permission | Grants | Typically held by |
|---|---|---|
| `hr.read` *(shipped)* | The directory, org structure, roster, attendance. **Never pay.** | Manager, HR, Admin |
| `hr.salary.read` *(shipped)* | Salary figures, pay structures, payslips, ledgers, cost reports | HR Officer, Accounts, MD |
| `hr.employee.manage` *(shipped)* | Create and amend employment records | HR Officer |
| `hr.attendance.review` *(shipped)* | Correct attendance with a reason | HR Officer |
| `hr.roster.manage` *(shipped)* | Build and publish rosters | Department Head, HR |
| `hr.leave.apply` / `.recommend` / `.approve` *(shipped)* | The leave chain | Employee / Manager / HR |
| `hr.payroll.run` / `.approve` *(shipped)* | Maker and checker, deliberately separate | HR Officer / Accounts Manager, MD |
| `hr.policy.manage` *(shipped)* | Effective-dated policy and master configuration | HR Manager, Admin |
| **`hr.reports.view`** | The report centre, excluding salary-bearing reports | Manager, HR, Admin |
| **`hr.reports.salary`** | Salary-bearing reports and exports | HR Officer, Accounts, MD |
| **`hr.team.view`** | Manager self-service, scoped to the holder's reporting line | Department Head |
| **`hr.request.decide`** | Regularization, OT, comp-off, swap, profile-change decisions | Manager, HR |
| **`hr.loan.manage`** | Loan and advance lifecycle | HR Officer |
| **`hr.settlement.manage`** | Separation, clearance and final settlement | HR Manager |
| **`hr.compensation.manage`** | Bonus, increment and promotion runs | HR Manager |
| **`hr.ledger.manage`** | PF, welfare and tax ledger operations and withdrawals | HR Officer, Accounts |
| **`hr.document.issue`** | Generate letters and certificates | HR Officer |
| **`hr.discipline.manage`** | Disciplinary records — restricted, and hidden entirely without it | HR Manager |
| **`hr.audit.view`** | The activity log | HR Manager, Admin, Auditor |
| **`hr.self`** | The employee's own space — implied by having a linked employment | Everyone |

**Scoping rules**

1. `hr.team.view` is **always** scoped to the holder's own reporting line or org unit. A manager
   cannot read another unit by changing a parameter.
2. Every employee sees their own record without any HR permission; nobody sees another's by default.
3. A separated employee's access ends on relieving, subject to the retention policy (G62).
4. Salary-bearing exports carry the same permission as the screen; there is no export back door.

---

## 12. UX requirements

PRD §7's fifteen principles are binding. What follows is what they mean specifically for M16, whose
operators are HR staff and ordinary employees rather than counter clerks.

### 12.1 Navigation — the menu must be restructured

Eleven flat entries already crowd one group; the capability in this document would make it thirty.
Required information architecture — **eight groups, each a job an operator has**:

| Group | Contains |
|---|---|
| **Dashboard** | HR command centre (role-aware landing) |
| **People** | Employees · Onboarding & probation · Documents & compliance · Separations & settlement · Org structure |
| **Time** | Attendance review · Attendance register · Devices & imports · Roster · Overtime & comp-off |
| **Leave** | Leave desk · Leave calendar · Balances & year-end · Holidays |
| **Payroll** | Payroll runs · Approvals · Payslips · Bonus · Increments & promotions · Loans & advances · Ledgers (PF / welfare / tax) · Disbursement |
| **Reports** | The report centre (§13), by category |
| **Setup** | Policies · Pay components & scales · Leave policies · Shifts & weekly-offs · Templates & letters · Notifications |
| **My space** | My profile · My attendance · My leave · My payslips · My requests *(and, for managers, My team · My approvals · My roster)* |
| **Governance** | Activity log · Approvals inbox |

Rules: a group with no permitted entry does not render. **"My space" is always present** for anyone
with a linked employment — it is the only group most employees ever see, and it must be first for
them. Group state (expanded/collapsed) persists per user. Maximum two levels; no third-level menus.

### 12.2 Screen inventory

| Group | Screen | New / Existing |
|---|---|---|
| Dashboard | HR command centre · Manager dashboard · Employee dashboard | rebuild + 2 new |
| People | Employee list · Employee record *(with timeline)* · New employee · Onboarding & probation register · Compliance & expiry register · Separation & clearance · Final settlement · Org structure · Org chart | 6 new |
| Time | Attendance review · Attendance register (muster roll) · Attendance calendar · Devices & import batches · Roster builder · Roster coverage · Overtime & comp-off | 5 new |
| Leave | Leave desk · Leave calendar · Balances · Leave-year close · Encashment · Holiday calendars | 5 new |
| Payroll | Runs · Run detail with exceptions **and variance** · Approvals · Payslips · Payslip document · Bonus · Increments & promotions · Loans & advances · PF / welfare / tax ledgers · Disbursement batch | 7 new |
| Reports | Report centre + the §13 inventory | all new |
| Setup | Policies *(existing)* · Pay components & scales · Leave policies · Shifts & patterns · Letter templates · Notification switches | 5 new |
| My space | My profile · My attendance · My leave · My payslips · My loans · My documents · My requests · My timeline · My team · My approvals · My roster | all new |
| Governance | Activity log · Approvals inbox | 2 new |

### 12.3 The period selector — one control, everywhere

**Binding standard.** Every report, register, dashboard, calendar and log in M16 selects its period
through the same control, in the same screen position.

- **Granularity tabs:** `Day` · `Week` · `Month` · `Quarter` · `Year` · `Custom range`
- **Calendar picker** appropriate to the granularity: a day picker for Day, a month grid for Month,
  a year list for Year, a two-date calendar for Custom.
- **Presets, one click:** Today · Yesterday · This week · This month · Last month · This quarter ·
  This year · Last 12 months · **This leave year** · **This financial year** *(both taken from the
  employer's configured start month, never assumed)*.
- **Arrows** step the selection forward and back one unit — the fastest way to compare months.
- **The resolved range is always stated in words**, with the timezone: *"1 – 31 July 2026 ·
  Asia/Dhaka"*. Never only a control state.
- **It persists.** Moving from a report to a dashboard to the log keeps the selection; it is shown,
  never silently reused.
- **It is in the URL**, so a period is a link an HR officer can send to the MD.
- **Comparison is part of it:** where a report or tile supports comparison, the same control offers
  *previous period* and *same period last year*.
- Future periods return an empty result with a sentence, not an error. A range longer than a
  configured maximum warns before running rather than timing out.
- **Dates are entered forgivingly** (§7 U13) and always displayed unambiguously — `06 Aug 2026`,
  never `06/08/26`.

### 12.4 Report standard

Every report in §13 shares one grammar, so learning one is learning all (§7 U9, D3):

1. **Header:** report name, the resolved period in words, the filters actually applied, who ran it,
   when, and the employer's letterhead on print.
2. **Filters:** period *(§12.3)* · org unit *(tree, multi-select)* · designation · grade ·
   employment type · employee *(type-ahead, §7 U5)* · status. Filters are shown as removable chips,
   never hidden in a collapsed panel.
3. **Body:** sortable columns; totals and subtotals per group; a stated row count; numbers
   right-aligned and tabular; **taka in whole numbers with thousands separators**; no decimals.
4. **Empty state:** a sentence explaining why there are no rows and what to change — never a blank
   table.
5. **Drill-through:** every summary figure opens the rows behind it, carrying the period and filters.
6. **Export:** PDF (print layout), Excel (data layout, one header row, no merged cells), CSV. The
   export contains exactly what the screen showed, including the applied filters in its header.
7. **Print:** A4 portrait by default, landscape for wide registers, with the letterhead, page
   numbers, "page n of m", and the period repeated on every page.
8. **Permission:** salary-bearing reports require salary read; a user without it sees the report
   listed as unavailable with the reason, not a silently truncated version.
9. **Performance:** a report that cannot return promptly says so and offers a narrower period rather
   than hanging (§16).
10. **Saved views** `[S]`: name a configuration, re-run it for a new period in one click. (G5)

### 12.5 Data-visualisation standard

Charts appear only where a shape communicates faster than a number — trend, composition and
comparison. Every chart:

- states its period and its unit in words;
- uses status-safe encoding — never colour alone (§7 U12) — and is legible in monochrome print;
- carries a data table one click away, because HR staff print tables, not pictures;
- is clickable through to the rows (D4);
- never uses more than one chart type per question, and never a pie chart with more than five slices.

### 12.6 Screen-level UX requirements

- **Employee identity banner** — photo, name, employee code, designation, unit, status — appears
  identically on every screen scoped to one person, the way the patient banner does in the ERP
  (§7 U9).
- **One screen, one job** (§7 U2): correcting attendance, approving leave and running payroll each
  complete where they start; no workflow requires remembering a second screen.
- **Bulk actions where the work is bulk** — roster assignment, leave-year close, increment run,
  payslip issue, bonus eligibility — each with a **preview before commit** showing exactly what will
  change and for whom, and a plain-English consequence summary (§7 U8).
- **Reason fields are first-class**: every correction, adjustment, cancellation, hold and override
  requires a typed reason, and that reason is displayed everywhere the change is later seen — never
  buried in an audit table.
- **Approvals are decidable in place**: an approval queue shows the context needed to decide (balance,
  clash, history, attachment) without navigating away.
- **Status by colour and word** everywhere (§7 U12); the attendance register's cell codes are letters
  first, colour second, with a legend on screen and on print.
- **Micro-help** (§7 U14) on every configuration screen, because policy screens are the ones a
  30–55-year-old non-technical administrator gets wrong.
- **Everything printable** (§7 U10): payslip, salary sheet, attendance register, leave register,
  roster, service record, settlement statement, every report.
- **Self-service must work on a phone browser** — employees will not use a desktop. Manager and HR
  screens target 1366×768 desktop.
- **No screen may show a pay figure to a user without salary read** — including tooltips, exports,
  print layouts, notification text and error messages (D6).

---

## 13. Reports inventory

The report centre, by category. `[M]`/`[S]`/`[C]` per report; **$** marks salary-bearing (requires
`hr.reports.salary`). All obey §12.3 and §12.4.

### People

| Report | Answers | |
|---|---|---|
| Employee directory / master list | Who works here, filtered any way | `[M]` |
| Headcount summary — by unit, designation, grade, type, gender, location | The shape of the workforce | `[M]` |
| Headcount movement | Opening, joiners, leavers, closing — for any period | `[M]` |
| New joiners | Who started in this period | `[M]` §5A-17 |
| Separations / resigned | Who left, when, why | `[M]` §5A-17 |
| Attrition | Leavers as a rate, by unit and by reason, trended | `[M]` |
| Probation due & confirmation status | Who needs a decision | `[M]` |
| Contract expiry | Which contracts end when | `[M]` |
| Service length / long service | Who has served how long | `[S]` |
| Document expiry | What is expiring, whose, when | `[M]` |
| Professional licence compliance `[hospital]` | Who is unregistered or expiring | `[M]` |
| Dependants & nominees | PF/gratuity nomination completeness | `[S]` |
| Birthdays & work anniversaries | This month's list | `[C]` §5 M16 `[C]` |
| Asset holdings & unreturned assets | What employees hold | `[S]` |
| Employee service record | One person's full history, printable | `[M]` §15 |

### Time & attendance

| Report | Answers | |
|---|---|---|
| **Monthly attendance register (muster roll)** | Employee × day matrix, the paper format HR already uses | `[M]` |
| Daily attendance summary | Present, absent, late, on leave, on which shift, by unit | `[M]` |
| Late arrival & early departure register | Who, how often, how many minutes | `[M]` |
| Absence register | Absent without leave, by employee and period | `[M]` |
| Overtime register | OT minutes by employee, approved vs derived, banked vs paid | `[M]` |
| Comp-off ledger | Earned, availed, expiring | `[M]` |
| Attendance exception log | What needed a human, what was done, by whom, why | `[M]` |
| Correction & regularization log | Every amendment with its reason | `[M]` |
| Device & import health | Which devices reported, which files landed, what was rejected | `[M]` |
| Roster vs actual | Who was rostered and who came | `[S]` |
| Shift coverage | Understaffed shifts, by day | `[S]` |
| Working-hours summary | Hours worked per employee per period | `[S]` |

### Leave

| Report | Answers | |
|---|---|---|
| Leave register | Every application in the period with its outcome | `[M]` |
| Leave balance statement | Per employee per type — opening, accrued, availed, encashed, adjusted, available | `[M]` |
| Leave availed analysis | By type, unit, month — where absence concentrates | `[M]` |
| Pending approvals ageing | What is waiting, and how long | `[M]` |
| Leave without pay register | Unpaid days and their payroll effect | `[M]` |
| Encashment register | Who encashed what, when, for how much | `[M]` **$** |
| Leave liability | What carried balances would cost | `[S]` **$** |
| Leave-year close statement | What accrued, carried, lapsed and encashed | `[M]` |

### Payroll & compensation — all **$**

| Report | Answers | |
|---|---|---|
| **Salary sheet** | The run, in full, per employee per component | `[M]` |
| Salary summary by unit / designation / grade / location | Where the cost sits | `[M]` |
| **Bank transfer / disbursement sheet** | What the bank is told to pay | `[M]` |
| Cash payment sheet & cheque list | Non-bank payment, with a signature column | `[M]` |
| Payslip batch | Every payslip for a run, printable | `[M]` |
| **Salary variance / comparison** | This period vs last, per employee and per component, with reasons | `[M]` |
| Salary comparison across periods | Any two periods, side by side | `[M]` §5A-17 |
| Component-wise register | One component across everybody | `[M]` |
| Deduction register | Every deduction, by kind | `[M]` |
| Employer cost statement | Gross + employer contributions — the true cost | `[M]` |
| Cost-centre allocation | Payroll cost split as finance needs it | `[S]` |
| Arrears register | Pending and paid arrears, with their cause | `[S]` |
| Bonus register & bonus sheet | Who is eligible, who was paid, on what basis | `[M]` §5A-16 |
| Increment & promotion register | Every revision, old and new, with effective dates | `[M]` |
| Loan & advance outstanding | Who owes what, and the recovery schedule | `[M]` |
| Salary hold register | Who is held and why | `[M]` |
| Final settlement register | Settlements computed, approved and paid | `[M]` |
| Gratuity liability | What separation would cost today | `[S]` |
| Payroll run audit | Every state change on every run, with actor and time | `[M]` |

### Statutory & ledgers — all **$**

| Report | Answers | |
|---|---|---|
| PF member statement | One member's contributions and balance | `[M]` |
| PF contribution register | The period's employee and employer shares | `[M]` |
| PF withdrawal register | Withdrawals and settlements | `[M]` |
| Tax deduction (TDS) register | What was deducted from whom | `[M]` §3.4 |
| Employee annual tax computation statement | The employee's own tax picture | `[M]` §3.4 |
| Tax deposit / treasury output | What §3.4 requires to be filed | `[M]` §3.4 |
| Welfare fund statement | Member and fund position | `[M]` |
| Statutory registers | The employer's required register set, from customer-supplied templates | `[M]` D2 |

### Management & analytics

| Report | Answers | |
|---|---|---|
| HR scorecard | Headcount, attrition, absenteeism, overtime, cost — trended | `[M]` |
| Salary cost trend | 12 months, by unit, with drill-through | `[M]` **$** |
| Absenteeism & lateness trend | Where discipline is drifting | `[M]` |
| Overtime cost trend | Where overtime is replacing headcount | `[M]` **$** |
| Manpower budget vs actual | Sanctioned vs filled | `[S]` |
| Department-wise summary | One page per department | `[M]` §5A-17 |

### Governance

| Report | Answers | |
|---|---|---|
| **Activity log** | Every HR write, any period, by anyone | `[M]` |
| Per-employee audit | One person's change history | `[M]` |
| Approval history | Every approval and rejection with its decider | `[M]` |
| Login / access history | Who signed in and when | `[S]` §5A-17 |
| Permission grant report | Who can do what in HR today | `[S]` |

---

## 14. Dashboards

Three dashboards, one per audience (G6). All obey §12.3 (period selectable, comparison available)
and D4 (every figure drills through).

### 14.1 HR command centre — P-HR1, P-HR2

**Row 1 — Action required.** The panel that decides whether the module is useful at 9am. Each item
is a count, a severity, and a link; the panel says "nothing needs you today" when it is empty.

- Attendance exceptions blocking the current payroll period, and days remaining to cut-off
- Leave applications awaiting a decision, with the oldest age
- Regularization / OT / comp-off requests pending
- Probation decisions due, contracts expiring
- Licences and documents expiring within the configured lead time
- Payroll: the open run and its state, with the next action named
- Loans closing, salary holds outstanding

**Row 2 — The period at a glance** (tiles, all clickable, all comparable to the previous period):
headcount · joiners · leavers · attrition rate · present today · absent · on leave · late ·
overtime hours · payroll cost **$** · average cost per head **$**.

**Row 3 — Shape and trend:**
- Headcount by unit *(bar, drill-through)*
- Headcount movement over 12 months *(line: joiners, leavers, closing)*
- Attendance composition for the period *(present / absent / leave / holiday / weekly off)*
- Salary cost trend, 12 months, with the same month last year **$**
- Absence and lateness trend
- Overtime hours and cost trend **$**

**Row 4 — Calendars:** an organisation month calendar showing holidays, leave density per day and
roster-coverage warnings; and this month's birthdays and anniversaries.

**Row 5 — Recent activity:** the last changes made in HR, with a link to the full log.

### 14.2 Manager dashboard — P-HR3

Deliberately small. A department head uses this weekly, not daily, and must never see a salary.

- **My team today**: present · absent · late · on leave · on which shift — with names, not just
  counts
- **My approvals**: one queue, decidable in place
- **My team's calendar** for the selected period: leave and attendance, month grid
- **My roster**: current state, coverage warnings, publish action
- **Attention**: my team's exceptions before cut-off, probations due, contracts ending
- **My team**: headcount, attrition and absence for my unit only — no cost, no salary

### 14.3 Employee dashboard — P-HR4

- My leave balances, explained, with one large **Apply for leave** action
- My attendance this month as a calendar, with my late/OT totals and a **Raise a correction** action
- My latest payslip and a link to all of them
- My pending requests and their state
- My loan outstanding and next installment
- What is expiring that is mine to fix (documents, licence)
- Holidays coming up; the roster I am on

---

## 15. Employee timeline

The requirement the PM asked for, and the one that changes how the module feels. **`[M]` (G11, D5)**

### 15.1 What it is

One chronological stream on the employee's record — the whole employment, newest first by default,
readable as a story rather than assembled from four tables.

### 15.2 What appears on it

| Category | Entries |
|---|---|
| **Employment** | Joined · probation started · probation extended · confirmed · promoted · transferred · re-designated · notice served · resigned / terminated / retired · relieved · settled · rehired |
| **Compensation** **$** | Pay structure set · increment · revision · bonus paid · salary held / released |
| **Time** | Attendance exceptions and their corrections *(with the reason)* · regularizations · significant OT · comp-off earned and availed |
| **Leave** | Every application with its outcome · encashment · balance adjustment *(with the reason)* |
| **Money owed** | Loan requested, approved, disbursed, closed · advance recovered |
| **Documents** | Letters issued · documents uploaded · documents expired |
| **Compliance** `[hospital]` | Licence recorded, renewed, expired · training attended · certification expired |
| **Conduct** *(restricted)* | Show-cause · warning · suspension · resolution — visible only with `hr.discipline.manage` |
| **Assets** | Issued · returned · unreturned at clearance |
| **Recognition** | Award, commendation, honour duty `[C]` |

### 15.3 How it behaves

- **Filter by category** (chips, multi-select) and **by year** — a year scrubber, so a fifteen-year
  employment is navigable.
- **The §12.3 period control applies**: "show me 2024" is one click.
- **Every entry carries** what happened, its effective date, who recorded it, when, and the reason
  where one was required. Nothing on the timeline is editable; a correction is a new entry that
  supersedes and says so (D8).
- **Entries link to their source** — an application, a run, a correction, a document.
- **Salary-bearing entries are hidden entirely** without salary read, and their absence is stated
  ("3 compensation entries hidden") rather than silently omitted, so the record is never
  misread as complete (D6).
- **Printable as a Service Record**: employer letterhead, the employee's identity, the full or
  filtered timeline, and the signature block a Bangladeshi employer expects — the document HR is
  asked for when a bank, an embassy or a next employer asks about a person.
- **Available in self-service** `[S]`: the employee sees their own, minus restricted conduct entries.

---

## 16. Non-functional requirements

Inherits PRD §8 and §16; what is specific to M16:

| # | Requirement |
|---|---|
| N-HR1 | **Volumetrics.** Design for 1,000 employees per employer, 5 years of history: ~1.8M attendance days, ~4M punches, ~60 payroll runs with ~60,000 payroll lines. Reports must remain usable at that size. |
| N-HR2 | **The deployment ceiling is unchanged** — a single VM, 2 vCPU / 3 GB RAM (§16). Reports and dashboards must not be designed on the assumption of a reporting server. A report that cannot answer promptly must narrow the period rather than hang. |
| N-HR3 | **Historical reproduction is absolute** (hard rule 5). Re-opening any past run, payslip, register or report for a past period reproduces the original figures exactly, after any rate, policy or master has changed. |
| N-HR4 | **Append-only truth** (hard rule 4). No financial or employment record is ever hard-deleted. Corrections are reversals or superseding effective-dated rows. Every write is attributable to a person and a time. |
| N-HR5 | **Timezone.** Asia/Dhaka, no DST. A shift's date is the shift's own date, never the calendar date a punch happened to land on. Every displayed period states the timezone. |
| N-HR6 | **Money.** BDT, whole taka, no decimals anywhere — entry, storage, display or export (§C3). Net pay appears in words on the payslip. |
| N-HR7 | **Power and connectivity** (§8 N2). A payroll run interrupted mid-generation leaves no half-run. An import interrupted leaves a batch that can be re-run without duplication. |
| N-HR8 | **Confidentiality.** Salary is a distinct sensitive-data class, protected on every surface (D6). Access to salary data is itself logged. |
| N-HR9 | **Retention.** Separated employees' records are retained for a configurable period; the policy is stated in the product, not assumed. |
| N-HR10 | **Both SKUs.** Every requirement here holds in the standalone HRM product, where M15, M17, M20 and the hospital modules do not exist. Absence degrades to an export, never to a broken screen. |
| N-HR11 | **Statutory neutrality.** No statutory rate, slab, entitlement or formula is embedded (D2). This is verifiable: a search for such constants finds none. |
| N-HR12 | **Accessibility of the operator kind** (§7): 16px base, 44px targets, colour never the sole carrier of meaning, keyboard-operable approval queues, works at 1366×768; self-service works on a phone browser. |

---

## 17. Delivery phasing

Sequencing is a PM recommendation; the architect owns the plan. Each phase is independently
demonstrable and independently sellable.

**Phase 1 — Make it answerable** *(the gap a buyer sees first)*
The period standard (§12.3) · the report centre with the People, Time, Leave and Payroll registers ·
the three dashboards · the employee timeline · the activity log · the menu restructure.
*Nothing new is captured; everything already captured becomes visible. Highest value per unit of
work in this document.*

**Phase 2 — Close the employment lifecycle**
Employment type · probation workflow · dependants and nominees · typed documents with expiry ·
professional licence `[hospital]` · separation, clearance and final settlement · document and letter
generation · notifications.

**Phase 3 — Complete the money**
Loans and advances · PF, welfare and tax member surfaces · tax statements and TDS output · bonus ·
increment and promotion runs · variance review · salary hold · disbursement and bank file · payslip
document.

**Phase 4 — Time depth**
Live device feed · regularization requests · OT approval, OT bank and comp-off · grace and late
policy · roster templates, patterns and coverage · short leave and outdoor duty · shift swap.

**Phase 5 — Self-service and the rest**
Full ESS and MSS · leave calendar, year-end close and encashment · appraisal · training and
certification · disciplinary · asset register · notice board · expense claims · saved reports.

---

## 18. Open questions for the customer & PM

Raised here rather than answered, because answering them without evidence would breach hard rule 3.
Each blocks a specific requirement; none blocks the phase-1 work.

| # | Question | Blocks | Owner |
|---|---|---|---|
| **Q-HR-1** | **The statutory pack.** Who supplies and signs off the Bangladesh leave entitlements, tax slabs, PF rules, gratuity formula and required statutory registers? We will not author them (D2). Options: the customer's counsel per deployment, or a licensed content pack from a named provider. | §7.8 content, G58 | PM + customer's counsel |
| **Q-HR-2** | **The bank file format.** Which bank(s), and which BEFTN/EFT batch format? §3.4 flags per-bank variation. We need one real specification and one real file to test against. | G45, US16.8 | Customer + Accounts |
| **Q-HR-3** | **Biometric devices.** Which make and model will actually be installed? §9A.3 deferred this because none was purchased. The live feed cannot be built against a hypothetical device. | G22 | Customer |
| **Q-HR-4** | **Leave approval shape.** Confirmed as configurable 1–3 tiers (D1) — but which is the *default* the customer wants on day one, and who holds tier 2 and 3? | Configuration, not build | Customer HR |
| **Q-HR-5** | **Payroll cut-off and pay date.** What day of the month does attendance close, and what day is salary paid? Drives the payroll calendar and every notification's timing. | G48 | Customer HR |
| **Q-HR-6** | **Mobile.** Is a phone app required, or is a phone browser enough for self-service? A native app with GPS/face attendance is a separate product investment (P29, §2.3). | ESS scope | PM |
| **Q-HR-7** | **Recruitment.** Confirm it stays out of M16 (D10) and whether an M23 is wanted at all. | §2.3 | PM |
| **Q-HR-8** | **Retention.** How long after separation is an employee's record kept, and what may the separated person still access? | G62, N-HR9 | PM + customer |
| **Q-HR-9** | **Doctors on payroll.** For a hospital, which doctors are employees (M16 salary) and which are consultants (M17 share), and are any both? The two must never net, but somebody will ask for a combined statement. | §10 standing obligation | PM + customer |
| **Q-HR-10** | **Cost centres.** Is the finance cost-centre structure the same as the HR org structure, or different? Determines whether §7.2's cost centre is needed. | G50 | Accounts |

---

## 19. Traceability

| This document | Main PRD |
|---|---|
| §7.1–7.3 Core HR, org, timeline | §5 M16 `[M]` employee records; §10 Employee entity; §5A-17 |
| §7.4 Time & attendance | §5 M16 `[M]` biometric attendance + correction; §13 I8; §5A-16 (grace, weekly-off, holiday-work) |
| §7.5 Shift & roster | §5 M16 `[M]` shift & roster (24/7 rotating) |
| §7.6 Leave | §5 M16 `[M]` leave management; §11 Leave Application; §5A-16 (3-tier, policy/balance setup) |
| §7.7 Payroll | §5 M16 `[M]` payroll from attendance, bonus, payslips, posts to M15; §11 Payroll Run; US16.1 |
| §7.8 Statutory | §3.4 (TDS, TR Form 6, BEFTN); §5A-16 (welfare & tax ledgers, PF withdrawal) |
| §7.9 Loans | §5 M16 `[S]` loan/advance with installment deduction, PF |
| §7.10–7.11 Self-service | §5 M16 `[S]` online leave application; §12 "U (own leave)" |
| §12–§14 UX, reports, dashboards | §7 U1–U15; §5A-17 (reports); §5A-20 (management dashboards) |
| §15 Timeline | New scope — §6 of this document, D5 |
| §11 Permissions | §12 Roles & Permission Matrix |
| §9 State machines | §11 Workflow State Definitions |
| §10 Data flows | §6 Module-to-Module Data Flow |
| §16 Non-functional | §8 N1–N6; §16 deployment constraint; hard rules 3, 4, 5 |

---

**Handoff note to the architect.** Phase 1 asks for almost no new data — it asks for the data
already captured to become visible, on one period control, with one report grammar, and for the
employee's history to be readable as a history. That is where this module's next unit of value is,
and it is worth designing the period control, the report grammar and the drill-through contract once
and properly, because everything in phases 2–5 will be displayed through them.
</content>
</invoke>
