# 0057 — M16 Phase 3: complete the money (loans, ledgers, tax, bonus, increments, variance, hold, disbursement, payslip)

- **Status:** Done
- **Date:** 2026-08-06
- **PRD ref:** `docs/m16-hr-payroll-prd.md` §5.6 (G38–G51), §7.7, §7.8, §7.9, §9, §11, §17 Phase 3;
  main PRD §5 M16, §5A-15, §5A-16, §3.4, §6.6, §11, §12
- **Parent:** `docs/specs/0054-hr-payroll-industry-standard/`. Third of five build specs.
- **Predecessors:** `0055` (period standard + report grammar), `0056` (employment lifecycle). The
  final settlement 0056 built reads `hr.loan` and recovers nothing, because nothing writes it. This
  spec is what makes that read true.

## Problem

M16 computes a payslip. It does not complete a month of payroll.

**1. Three tables the engine writes to nothing.** `PayrollService.BuildLineAsync` computes a
provident-fund deduction and an income-tax deduction and puts both on the payslip — and writes **no
`hr.employee_ledger_entry` for either**. The only ledger row the product has ever written is the
minimum-net-pay shortfall. So the money is deducted from the employee every month and the member
balance it is deducted *into* does not exist: there is no PF statement, no TDS register, no annual
tax computation, and nothing to pay a nominee at settlement.

**2. `hr.loan` and `hr.loan_installment` have zero writers.** Two tables, a state machine in
constants, and no screen, no service and no recovery. Spec 0056's settlement reads the loan
balance and correctly recovers zero, because there is nothing to recover from.

**3. Nothing between "computed" and "approved".** §9 puts **Variance Reviewed** between Exceptions
Reviewed and Approved. The module goes straight from one to the other, so a run where one person's
pay tripled is approved exactly as fast as a run where nothing changed.

**4. A person cannot be held.** There is no way to withhold one employee's salary. The only thing an
operator can do is nothing, and the run pays them.

**5. Nothing is disbursed.** `PostAsync` hands a journal to Accounts. No bank file, no cash sheet, no
cheque list, no per-employee paid status — the money is computed, posted and then, as far as the
product is concerned, vanishes. §9's terminal state is **Disbursed** and the module has no such state.

**6. The payslip is a table.** `/hr/payslip/{id}` lists components and amounts. It is not the
document §7.7 describes — no year-to-date, no leave balance, no net in words, no payment method.

**7. No bonus, no increment, no promotion.** Compensation can only be changed one person at a time,
by hand, on the employee record.

## Requirements

- [M] **Loans and advances** — request → approve⚿ → disburse → automatic installment recovery in
  payroll → outstanding statement → foreclose or write off⚿. Recovery respects the minimum-net-pay
  floor and **defers rather than forcing a negative net**. (G41, §7.9)
- [M] **PF, welfare and tax ledgers written by the run** — every computed deduction posts a member
  ledger entry, so a statement exists. Member statements per ledger kind. (G42, G43)
- [M] **Tax statement and TDS output** — a per-employee annual computation and a monthly deduction
  register, from the ledger the run now writes. (G44)
- [M] **Bonus** — a sheet with eligibility by employment type and service length, computed on a
  configurable basis, approved⚿, paid in a run or off-cycle. (G38)
- [M] **Increment and promotion runs** — select a cohort, apply a policy, preview old versus new,
  approve⚿, apply effective-dated, issue letters. (G39, G40)
- [M] **Variance review before approval** — this period versus last, per employee, with anything
  outside a configured tolerance requiring a reason. **Approval is not offered until variance has
  been reviewed.** (G47, §9)
- [M] **Salary hold and release** with a reason; a held person appears on the sheet **as held, never
  silently absent**. (G46)
- [M] **Disbursement** — a batch per run split by payment method, a bank transfer file, a cash sheet
  with a signature column, a cheque list, and a per-employee paid status. (G45)
- [M] **Payslip document** — letterhead, identification, earnings and deductions with a readable
  basis, period and year-to-date, leave balance, net in figures and words, payment method,
  printable A4. (G49)
- [M] The registers deferred from 0055 and 0056: loan, PF, welfare, tax, TDS, bonus, increment,
  arrears. (§13)
- [M] **No statutory value is shipped** (D2, N-HR11). Bonus basis, increment percentage, PF interest
  and every tax band remain employer configuration; the product ships the mechanism empty.
- [M] Hard rule 4 throughout: a disbursement is never deleted, a written-off loan is a state and a
  reason, and a reversal is the only way to change a locked run. **(shipped — kept)**
- [M] Salary confidentiality (D6): every surface here is salary-bearing except the loan *request*,
  which an employee may raise for themselves.
- [S] Payroll calendar and cut-off. (G48)
- [S] Cost allocation by unit. (G50)

## Acceptance criteria

1. A loan approved and disbursed is recovered automatically by the next regular run, one installment
   at a time, and the installment appears on the payslip with its basis.
2. When recovery would drive net pay below the employer's floor, the installment is **deferred**, the
   loan says so, and the employee is never handed a payslip that owes money.
3. Foreclosing settles the balance in one movement; writing off requires approval and a reason, and
   neither deletes anything.
4. Every PF, welfare and tax deduction a run computes appears on the member's ledger, and the member
   statement's closing balance equals the sum of its movements.
5. The annual tax statement's total equals the sum of that year's monthly TDS entries.
6. A run cannot be approved until variance has been reviewed; a line outside tolerance cannot be
   cleared without a reason.
7. A held employee appears on the salary sheet **marked held, keeping their earned figures**, and
   their pay is not disbursed; releasing the hold before the next run restores them.
8. A locked run produces a disbursement batch whose bank-file total equals the sum of the bank-paid
   net amounts, and marking a line paid is attributable and irreversible.
9. A payslip prints on A4 with year-to-date figures, the leave balance, and the net in words.
10. A bonus sheet computes only for eligible employees, states why anyone was excluded, and pays
    through a run.
11. An increment run previews every affected person with old and new pay, and applying it
    effective-dates the change without touching history.
12. No statutory rate, slab or formula is embedded; every unconfigured policy is named on screen.
13. All guards pass, including `check-additive-migrations.sh`, plus the full test suite.

## Out of scope

| Deferred | Reason | Goes to |
|---|---|---|
| Expense claims (G56) | `[C]`, and §17 puts it in Phase 5. | 0059 |
| Employee-facing loan request (ESS) | The loan lifecycle is here; the employee's own door is ESS work. HR raises a loan on an employee's behalf today. | 0059 |
| PF interest posting run | The mechanism is here (a ledger movement of kind `pf` with a narration); an automated annual interest run over every member is its own decision, and the rate is the employer's. | later |
| Payslip delivery by email/SMS | `[C]`. | — |
| Cost allocation in the journal (G50) | `[S]`; the salary-cost-by-unit register is delivered, the journal split is not. | later |
| Payroll calendar and cut-off (G48) | `[S]`; attendance cut-off is really a Phase 4 concern once the device feed lands. | 0058 |
| PF interest posting, and the `hr.ledger.manage` permission §11 names for it | The member **statements** are here; ledger *operations* and withdrawals are not, and a permission that guards nothing is a grant that protects nothing — `check-lifecycle-traceability.sh` says so out loud. | later |

## What landed

| Area | Delivered |
|---|---|
| Schema | `HrMoney0057`: `bonus_sheet`, `bonus_line`, `compensation_run`, `compensation_line`, `salary_hold`, `disbursement`, `disbursement_line`, `variance_note`; new columns on `payroll_run` (`variance_reviewed_at/_by`, `disbursed_at`) and on `loan` (`purpose`, `approved_at/_by`, `disbursed_at`, `closed_at`, `write_off_reason`). Two new run states, `variance_reviewed` and `disbursed`. |
| Loans | `LoanService` — request, approve through the kernel engine, disburse, foreclose, write off. `PayrollService` recovers one installment per run under the net-pay floor and **defers** rather than forcing a negative net, writing a `LoanInstallment` either way. |
| Ledgers | The run now posts `EmployeeLedgerEntry` for provident fund (both shares), welfare and withheld tax. Every deduction the engine computes has a member balance behind it for the first time. |
| Tax | Annual computation statement and monthly TDS register, both read from the ledger the run writes. |
| Variance | `VarianceService` compares each line with the same employee's previous run; anything outside the employer's tolerance must carry a reason before the run can leave `variance_reviewed`. **Approve is not offered until it has.** |
| Hold | `salary_hold` with a reason; the run marks the line held and the disbursement skips it. The line keeps its figures — see the correction below. |
| Bonus | Sheet with eligibility by employment type and completed service, computed on a configurable basis, approved, attached to a run. |
| Increment & promotion | One `compensation_run` covering both: cohort by unit/grade/designation, policy by percent or amount, preview with old versus new, approve, apply effective-dated, issue letters through 0056's letter service. |
| Disbursement | A batch per locked run split by method, a bank file in a documented CSV layout, a cash sheet with a signature column, a cheque list, and a per-employee paid status that is attributable and one-way. |
| Payslip | A real document: letterhead, identification, earnings and deductions with basis, period and year-to-date, leave balance, net in words, payment method, A4. |
| Registers | Loan register, loan outstanding, PF/welfare/tax member statements, TDS register, annual tax statement, bonus register, increment register, disbursement register, arrears pending. |

**Two things the build corrected, both found by tests written for this spec:**

1. **A hold does not zero the net.** The first attempt set net pay to zero on a held line, and
   `ck_payroll_line_net` refused it — the constraint requires the arithmetic to add up, and it is
   right to. The person *did* earn the month; a hold withholds **payment**, and a payslip showing
   zero would tell the employee something false about what they are owed. The line is computed in
   full and marked; the disbursement batch skips held lines. Acceptance criterion 7 above is
   corrected accordingly.
2. **The global-unique number defect, fixed here rather than deferred.** Spec 0056 recorded it as a
   tracked cleanup: `hr.payroll_run.run_no`, `hr.payslip.payslip_no`, `hr.leave_application.
   application_no` and `hr.employee.employee_code` are all per-branch number series behind
   **globally** unique indexes, so a second branch's first run, payslip, leave application or hire
   would be rejected. This spec's tests create runs in several branches and it stopped being
   theoretical. `HrPerBranchNumbers0057` makes all four `(branch_id, …)`. Deferring a defect that
   blocks the work is not deferring, it is hiding.

**Verification:** 894 tests green (156 Kernel, 350 Web, 104 Architecture, 283 Integration, 1
PrintGolden). New pure tests for the recovery floor and its boundaries, the variance comparison and
the bank file; integration tests for the loan lifecycle, the member ledger the run now writes, the
variance gate, the hold, the disbursement split and the increment's effective dating. Four existing
payroll tests were updated to walk §9's new state path rather than around it. Every guard passes.

## Notes

**The ledger was the keystone.** Four of §5.6's gaps — PF statement, welfare statement, tax
statement, TDS register — are one missing write. The engine already computed all three deductions
correctly; it simply never told the member's balance. Adding the write made four reports possible
and made 0056's settlement able to pay a nominee.

**Deferral is not failure.** When a loan installment would breach the net-pay floor, the row is
written with `Deferred = true` rather than skipped. A skipped installment is invisible; a deferred
one is a fact the outstanding statement can explain.

**One run type for increment and promotion.** §5.6 lists them as G39 and G40, but a promotion is an
increment that also moves the designation and grade. Two tables would need a rule about which one is
authoritative for a pay change on the same date.
