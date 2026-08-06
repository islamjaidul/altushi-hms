# 0056 — M16 Phase 2: close the employment lifecycle (type, probation, the personal file, separation, settlement, letters, alerts)

- **Status:** Done
- **Date:** 2026-08-06
- **PRD ref:** `docs/m16-hr-payroll-prd.md` §5.3 (G12–G21), §7.1, §7.2, §7.14, §9, §11, §12.2, §17 Phase 2;
  main PRD §5 M16, §5A-16, §5A-17, §7, §10, §11, §12, §16
- **Parent:** `docs/specs/0054-hr-payroll-industry-standard/` — the module PRD. Second of five build
  specs (0055–0059).
- **Predecessor:** `docs/specs/0055-hr-reporting-and-period-standard/` — the period standard, the
  report grammar and the timeline this spec's registers and events render through.
- **MVP:** n/a — the §9A.2 freeze was lifted 2026-07-27.

## Problem

M16 can hire a person and pay them. Between those two acts it knows almost nothing about the
employment itself. Four of these are not gaps in ambition but **columns and tables that already
exist and nothing reads or writes**:

**1. Nobody can ever be confirmed.** `Employee.ConfirmedOn` and `EmploymentStatus.Confirmed` are
written in exactly one place in the product — `src/Hms.Hr.Web/HrDemoSeed.cs`. An employee hired
through the UI is on probation permanently, because there is no date the probation is *due* and no
screen that ends it. `EmploymentEventKind.Confirmed` is a constant the timeline knows how to render
and nothing ever emits.

**2. The gratuity rule cannot be applied.** `PolicyResolver.SetGratuityAsync` writes
`hr.gratuity_rule`, and `Policies.cshtml.cs` displays it back. Nothing else reads it — not payroll,
not separation, because there is no settlement to read it. An employer can configure end-of-service
pay in full detail and the product will never compute a taka of it.

**3. Separation is a date and a status.** `EmployeeService.SeparateAsync` stamps `SeparatedOn`,
picks one of three statuses, closes the open assignment and records an event. There is no notice
period, no clearance, no dues, no settlement, no relieving date distinct from the last working day,
and no document at the end of it. §9's `Initiated → Clearance In Progress → Computed → Approved⚿ →
Paid → Documents Issued` is seven states of which the module implements the first, partially.

**4. Every employee is the same kind of employee.** There is no employment type, so a six-month
contract, an intern and a permanent consultant are one shape, nothing knows a contract has ended,
and no eligibility rule can ever differ by engagement.

**5. Documents are untyped and never expire.** `Employee.DocumentsJson` is
`[{name,path,uploadedAt}]`. A hospital cannot answer "whose professional registration expires next
month" — the single compliance question a private hospital is actually asked.

**6. There are no dependants or nominees.** `hr.pf_policy` and `hr.gratuity_rule` exist to pay money
to a person the system cannot name.

**7. There are no letters.** An appointment letter, an experience certificate and a salary
certificate are the three documents a Bangladeshi employee asks HR for, and none exists.

**8. Nothing is time-aware.** No probation falls due, no contract ends, no document expires, and
nobody is told.

## Requirements

- [M] **Employment type** on every employment — permanent, probationary, contract (with an end
  date), part-time, intern, daily-wage, consultant-on-payroll — captured at hire, changeable only as
  an attributed, dated event. (G12, §7.1)
- [M] **Probation lifecycle** — a probation due date set at hire from employer configuration, a
  probation-due register, and **extend** (new due date + reason) or **confirm** decisions, each
  producing a timeline entry and an offered letter. (G13)
- [M] **Dependants and nominees** — family members, and nominees for PF and gratuity whose shares
  must total 100% per purpose. Nominations are superseded, never edited away. (G14)
- [M] **Typed documents with expiry** — NID, passport, contract, certificate, licence, photograph and
  other, each with a number, issuing body where relevant, issue and expiry dates, and an alert before
  expiry. A renewal supersedes its predecessor rather than replacing it. (G21)
- [M] **Professional registration** `[hospital]` — licence body, number, issue and expiry, with
  expiry alerting. **Never blocks; always warns.** (G16)
- [M] **Separation** with kind (resignation / termination / retirement / end of contract / death),
  notice period, intended last working day, actual last working day and a relieving date distinct
  from it. (G18)
- [M] **Clearance checklist** routed to the departments that must sign off, each with a status, a
  note, a recoverable-dues figure and an attributable sign-off. (§7.1)
- [M] **Final settlement statement** — unpaid salary to the last working day, leave encashment,
  gratuity per the employer's rule, loan and advance recovery, notice-pay adjustment either way,
  clearance dues, netted to one payable, approved through the kernel approval engine, and paid
  through an off-cycle (supplementary) payroll run. (G19)
- [M] **Document and letter generation** — appointment, confirmation, probation extension,
  experience, termination, relieving, salary certificate and NOC, from **templates the employer
  edits**. An issued letter freezes its rendered body and is append-only. (G20)
- [M] **Notifications** for probation due, contract expiring, licence/document expiring, birthday and
  work anniversary, plus leave decided and payslip ready — **each independently switchable by the
  employer**, with the lead time configurable. (G57, §7.14)
- [M] The Phase-2 register inventory, through the 0055 report grammar — no new report is a page.
- [M] Salary confidentiality (D6) on every new surface: the settlement statement and the salary
  certificate are salary-bearing; separation, clearance and the personal file are not.
- [M] **No statutory value is shipped** (D2, N-HR11): notice period, probation length, gratuity days
  per year and encashment eligibility are all employer configuration with a stated default of "not
  configured", never an invented Bangladeshi number.
- [M] Nothing is hard-deleted (hard rule 4). A wrong nominee, document or clearance line is
  superseded or reversed, and the history stays.
- [M] Both SKUs (N-HR10) — every surface works in the standalone HRM host with no hospital module.
- [S] Exit-interview record, permission-restricted. (§7.1)

## Acceptance criteria

1. An employee hired through the UI gets an employment type and, if probationary, a due date; the
   probation register lists them before it falls due, and Confirm ends it — status, `ConfirmedOn`,
   timeline entry and an offered confirmation letter, in one transaction.
2. Extending a probation records the old and the new due date and the reason, and the register
   reflects the new date. A probation cannot be confirmed twice, nor extended after confirmation.
3. Nominee shares that do not total 100% for a purpose are refused with a sentence naming the
   shortfall; superseding a nomination leaves the previous one readable.
4. A document with an expiry date appears in the expiry register at its configured lead time, on the
   command centre's action panel, and in the notification queue when the employer has switched that
   kind on — and never blocks any action.
5. A separation runs `initiated → in clearance → computed → approved → paid → documents issued`,
   refuses to skip a state, and cannot be computed while any clearance item is outstanding.
6. The settlement statement reproduces its own arithmetic: every line states *why this number*, the
   gratuity line names the rule row and the completed years it used, and the statement prints with
   letterhead and a signature block.
7. A settlement whose employer has configured no gratuity rule computes **no gratuity line and says
   so**, rather than computing zero silently.
8. A settlement is approved through `kernel.approval_request` and can only be marked paid against a
   supplementary payroll run.
9. An issued letter's body is frozen at issue: editing the template afterwards does not change any
   letter already issued.
10. A salary certificate is refused to a reader without salary read, and no salary figure reaches any
    non-salary-bearing surface added here.
11. Every new register renders through the 0055 grammar, carries the period control where a period
    applies, and states a reason when empty.
12. All guards pass, including `check-additive-migrations.sh` on the new migration, plus the full
    test suite.

## Out of scope

| Deferred | Reason | Goes to |
|---|---|---|
| Loans and advances as a *capture* surface | `hr.loan` still has no writer. The settlement **reads** it and legitimately recovers zero today; the request/approve/disburse lifecycle is Phase 3's. | 0057 |
| PF and gratuity **ledger** settlement (paying the nominee out) | `hr.employee_ledger_entry` has no writer. The nominee is captured here so 0057 has someone to pay. | 0057 |
| Bonus proration in the settlement | There is no bonus. | 0057 |
| Asset issue and return (G17) | `[S]`, and the clearance checklist already carries a recoverable-dues figure per department, which is the part settlement needs. | 0059 |
| Qualifications, experience, skills, references (G15) | `[S]`, and not named in §17's Phase 2 list. | 0059 |
| Org chart, sanctioned strength (§7.2 `[S]`) | Calendar/tree-shaped; wants the same component Phase 4's roster grid needs. | 0058 / 0059 |
| Disciplinary record (G61), training (G59), appraisal (G60) | `[S]`, §17 Phase 5. | 0059 |
| Retention policy for separated employees (G62) | `[S]`; needs a PM decision on how long and what a separated person may still reach (Q-HR). | — |
| Employee-facing notification *inbox* | M20 is an SMS gateway. An in-product inbox is ESS work. | 0059 |
| `hr.discipline.manage`, `hr.request.decide`, `hr.loan.manage`, `hr.compensation.manage`, `hr.ledger.manage` | §11 names them; each belongs with the capability it guards, not ahead of it. | 0057–0059 |

## What landed

| Area | Delivered |
|---|---|
| Schema | One additive migration, `HrLifecycle0056`: three columns on `hr.employee` (`employment_type`, `contract_ends_on`, `probation_due_on`) and nine tables — `employment_policy`, `employee_dependant`, `employee_document`, `separation`, `clearance_item`, `settlement_line`, `letter_template`, `issued_letter`, `notification_setting`. Eight hand-written invariants EF cannot express, including a GiST no-overlap on the employment policy and `CHECK (NOT is_nominee OR purpose IS NOT NULL)`. |
| Employment type | Seven kinds from §7.1, decided at hire and changed only as a dated, attributed event. Contract-end and death now land on their own statuses; both used to be recorded as a resignation. |
| Probation | `ProbationDueOn` at hire (pre-filled from the employer's configured length, never invented), a probation register, and Confirm / Extend on the record. An extension records both dates and its reason on the append-only event stream. |
| Personal file | `/hr/employees/{id}/file` — dependants and nominees with a 100%-per-purpose rule, typed documents and professional licences with expiry. Nothing is edited or deleted: a nomination is superseded, a document is renewed, and the previous row stays readable. |
| Separation | `/hr/employees/{id}/separation` — §9's seven states on one page, each transition under a row lock. Clearance is five routed departments with an attributable sign-off and a recoverable-dues figure; a settlement cannot be computed while one is outstanding. |
| Final settlement | `SettlementCalculator` — unpaid salary, leave encashment, gratuity, notice either way, loan recovery, clearance dues, every line carrying its basis. Approved through `kernel.approval_request`, paid only against a locked supplementary run. **A missing policy produces no line and a sentence**, never a silent zero. This is the first code in the product that reads `hr.gratuity_rule`. |
| Letters | `/hr/letters` — employer-edited templates with `{{tokens}}`, ten kinds, starter wording offered but never seeded. An issued letter freezes its rendered body, is numbered and append-only, and prints on letterhead at `/hr/letters/{id}`. |
| Alerts | `HrAlertService` computes six time-based alerts from live data — **derived, never stored**, so a renewal silences its own warning. `/hr/notifications` switches each independently with its own horizon; the same horizon drives the registers and the command centre. |
| Registers | Nine new reports through the 0055 grammar: probation, contract expiry, compliance & expiry, licence register `[hospital]`, nominees, separation & clearance, birthdays & anniversaries, settlement register `$`, letters issued. **35 reports total.** |
| Command centre | Four lifecycle rows added to the action-required panel, each stating how many are already overdue. |
| Timeline | A Documents category — documents recorded and letters issued now appear on the service record. |
| Both SKUs | Every surface works in the standalone HRM host. SMS delivery is ERP-only, because the HRM SKU ships no notifications module (ADR-0025) — the switches screen says so rather than offering a switch that cannot fire. |

**Two defects fixed on the way, both found by tests written for this spec:**

1. **Every employee drill-through in every report was a 404.** Spec 0055 built `/hr/employee/{id}`, singular, against a page routed at `/hr/employees/{id}`. Nothing failed: the URL was well-formed, the report rendered, the cell was blue. `ReportRouteTests` now checks every URL a report can build against the routes on disk.
2. **`issued_letter.letter_no` was globally unique behind a per-branch number series**, so a second branch's first letter would have been rejected. Caught by the branch-isolation test; fixed to `(branch_id, letter_no)`. `hr.payroll_run.run_no`, `hr.payslip.payslip_no` and `hr.leave_application.application_no` have the same latent collision — pre-existing, verified, and left as a tracked cleanup rather than widened into this spec.

**Verification:** 836 tests green (156 Kernel, 318 Web, 104 Architecture, 257 Integration, 1 PrintGolden) — 89 of them new. Every guard passes, including `check-additive-migrations.sh` on the new migration. Both hosts were booted and driven over HTTP: the personal file accepts a licence and warns on an incomplete nomination, a separation refuses to compute until all five departments sign off and then produces a statement whose gratuity line reads "7 completed years × 30 days of pay at 854/day (rule #1)", an issued letter does not change when its template is rewritten, and a department head is refused the separation page outright.

## Notes

**Registers are reports, not screens.** §12.2 lists "Onboarding & probation register" and "Compliance
& expiry register" under People as screens. ADR-0029 obliges every new *read* to be a class in
`ReportCatalog`. Both readings are satisfied by making each register a report that drills to the
employee record, where the decision (Confirm / Extend / renew a document) actually lives — the
context an operator needs to decide is on the record, not in the list. Separation, clearance and
settlement are a *workflow*, so those stay pages.

**One workflow, one row.** §9 gives Employment and Final settlement two overlapping state lists.
They are modelled as one `hr.separation` row carrying §9's final-settlement states, with clearance
items and settlement lines as children — because "computed" is a fact about a separation, and two
rows would need a rule about which one is authoritative.

**Permission names come from §11**, not from us: `hr.settlement.manage` and `hr.document.issue` are
the PRD's own strings. Clearance sign-off has no permission in §11; rather than invent one, an item
is signed off by a holder of `hr.settlement.manage` **or** by the head of the org unit the item is
routed to (§11 scoping rule 1's existing shape). Whether each department should hold its own
sign-off claim is a question for the PM, recorded in `09-questions-for-pm.md`.
