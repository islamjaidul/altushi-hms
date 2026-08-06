# Plan — 0056, M16 Phase 2: close the employment lifecycle

**Parent plan:** the five-phase program plan approved in session on 2026-08-06 and archived at
`docs/specs/0055-hr-reporting-and-period-standard/plan.md`. That plan set the sequencing
(0055–0059), the scope decision ("all five phases as a rolling program, one spec per phase,
committed as each lands") and the export/empty-data/audit decisions that still bind. This is its
Phase 2 section, expanded to a build order.

---

## Build order

### 1. Schema — one additive migration, `HrLifecycle0056`

Additive only (03 §12). No column is dropped, no meaning is changed.

**On `hr.employee`:**

| Column | Why |
|---|---|
| `employment_type` text NOT NULL DEFAULT `'permanent'` | G12. Default `permanent` because that is what every existing row *is* — the module has only ever created permanent-shaped employments, so the default states a fact rather than guessing one. |
| `contract_ends_on` date NULL | Contract / intern / daily-wage engagements. Feeds the contract-expiring register. |
| `probation_due_on` date NULL | The date the module has never had. Null for everyone already confirmed and for types with no probation. |

**New tables** (all in `hr`, all carrying `branch_id` so `ApplyBranchIsolation` filters them
structurally):

- `employee_dependant` — name, relation, date of birth, phone, national id, `is_nominee`,
  `nominee_purpose` (`pf` | `gratuity` | `both`), `share_bp`, plus `superseded_at` / `superseded_by`
  / `supersedes_id`. Never deleted (hard rule 4's shape applied to people).
- `employee_document` — `kind` (`nid` | `passport` | `birth_certificate` | `contract` |
  `certificate` | `licence` | `photograph` | `other`), title, number, issuing body, `issued_on`,
  `expires_on`, file path, note, `supersedes_id`, `superseded_at`. **The professional licence
  (G16) is `kind = licence` with an issuing body** — one table, one expiry engine, one register,
  rather than a second near-identical table that would drift.
- `separation` — kind, state, `notice_on`, `notice_days`, `intended_last_working_day`,
  `last_working_day`, `relieving_on`, reason, exit-interview note, `computed_at`/`by`,
  `net_payable_taka`, `approval_request_id`, `approved_at`/`by`, `paid_run_id`,
  `documents_issued_at`. One row carries §9's whole final-settlement state machine.
- `clearance_item` — separation id, department, `org_unit_id` (who it is routed to), status,
  note, `dues_taka`, `signed_off_by`/`_name`/`_at`.
- `settlement_line` — separation id, ordinal, label, `kind` (earning | deduction), `amount_taka`,
  `source`, and `basis` — the "why this number" string the payroll component line already carries.
- `letter_template` — kind, title, body with `{{tokens}}`, active, attribution.
- `issued_letter` — employee, kind, `letter_no` (number series), title, **frozen rendered body**,
  `issued_on`, issuer id + name, optional separation id. Append-only.
- `notification_setting` — kind, enabled, `days_before`. Employer configuration.

Indexes: `(branch_id, probation_due_on)` filtered on status, `(branch_id, contract_ends_on)`,
`(branch_id, expires_on)` on documents, `(employee_id)` everywhere a record page reads, unique
`letter_no`, unique `(separation_id, department)`.

### 2. Services — `src/Modules/Hr/Hms.Hr/`

- **`EmployeeService`** gains `ConfirmProbationAsync`, `ExtendProbationAsync`,
  `ChangeEmploymentTypeAsync`. `HireAsync` takes the type and the probation due date. Each writes an
  `EmploymentEvent` and a tier-1 audit row, in the caller's transaction.
- **`EmployeeFileService`** — dependants, nominees and documents. Owns the 100%-per-purpose nominee
  invariant and the supersede-don't-edit rule.
- **`SeparationService`** — `InitiateAsync`, `SignOffClearanceAsync`, `ComputeAsync`,
  `RaiseApprovalAsync`, `MarkPaidAsync`, `IssueDocumentsAsync`, `CancelAsync`. State transitions are
  guarded by a state-checked `UPDATE`, the shape spec 0052 established for payroll runs, so a
  concurrent second click loses with a sentence.
- **`SettlementCalculator`** — pure where it can be: given the pay structure, the leave balance, the
  gratuity rule, the loan outstanding and the clearance dues, produce the lines. Every line carries
  its basis. **A missing gratuity rule produces no line and a stated reason** (ADR-0027's rule).
- **`LetterService`** — template CRUD, token rendering, issue. Rendering is a pure static so it can
  be tested without a database.
- **`HrAlertService`** — computes the due/expiring set from live data. Alerts are **derived, never
  stored**: a stored alert outlives its cause and then has to be reconciled.
- **`HrAlertJob`** — an `IJobHandler` for the existing daily `notif.due_reminders` job type, enqueuing
  SMS through `SmsQueue` for the kinds the employer has switched on.

### 3. Screens

**New:**

| Route | What | Permission |
|---|---|---|
| `/hr/employees/{id}/file` | Personal file — dependants & nominees, typed documents & licences | `hr.read` to view, `hr.employee.manage` to write |
| `/hr/employees/{id}/separation` | Separation → clearance → settlement → approval → paid → documents, one staged page | `hr.settlement.manage` |
| `/hr/letters` | Letter templates (Setup) | `hr.policy.manage` |
| `/hr/letters/{id}` | One issued letter, printable on letterhead | `hr.document.issue` |
| `/hr/notifications` | Notification switches (Setup) | `hr.policy.manage` |

**Modified:** the employee record (employment type + probation card with Confirm / Extend, file
summary, separation entry, Issue letter), new-employee (type, probation due, contract end), the HR
command centre (probation due, contracts ending, documents expiring, clearance outstanding rows in
the action panel), the timeline (probation, document, separation and letter categories).

### 4. Reports — nine, through the 0055 grammar

`probation-due`, `contract-expiring`, `document-expiry`, `licence-register` `[hospital]`,
`nominee-register`, `dependants`, `separation-clearance`, `settlement-register` `$`,
`letters-issued`, `birthdays-anniversaries`. Each drills to the record where the decision lives.

### 5. Permissions, nav, seeds

Two new permissions, both **§11's own names**: `hr.settlement.manage`, `hr.document.issue`. Both
forms plus `Claim.All`, a `ROUTES` row per new route in `eng/verify/role-journeys.py`, an
enforcement point the traceability guard can grep as a **string literal**, and an upgrade grant to
the roles that already hold the adjacent capability.

Nav gains: People → Personal file is reached from the record, not the menu; Setup → Letter
templates, Notification switches; Reports pick up the nine registers automatically.

### 6. A Phase-1 defect to fix on the way

`ReportContext.Employee` builds `/hr/employee/{id}` — singular. The route is `/hr/employees/{id}`.
**Every employee drill-through from every report is a 404.** Fix it and add the architecture test
that would have caught it: every URL a report can produce must match a real page route.

---

## Verification

- **Kernel/Web tests (pure)** — letter token rendering (unknown token, missing value, HTML in a
  value); nominee share arithmetic; settlement line arithmetic against a fixed fixture including the
  no-gratuity-rule case; probation date arithmetic across month ends.
- **Integration (real Postgres)** — the separation state machine refuses every skipped transition and
  every double transition; clearance blocks compute; the settlement reads the gratuity rule that was
  in force on the last working day, not today's; branch isolation on all seven new tables; an issued
  letter's body does not change when its template does.
- **Architecture** — the new registers are in the catalogue, salary-bearing flags match §13, a report
  cannot produce a URL that no page serves.
- **Guards** — `check-additive-migrations.sh` (this is the first phase with a migration),
  `check-css-classes.sh` for every new class, `check-ui-tokens.sh`, `check-no-native-date.sh`,
  `check-lifecycle-traceability.sh`, `check-fkeys.sh`.
- **Both hosts build and boot**; the HRM SKU gets the same surfaces.
