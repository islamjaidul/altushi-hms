using Hms.Hr;
using Hms.Hr.Data;
using Hms.Hr.Web;
using Hms.Kernel.Approvals;
using Hms.Kernel.Audit;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0057 — what happens to the money after the payslip is computed, against a real database.
/// <para>
/// The cases that matter are the ones the payroll engine only does once: the loan installment it
/// recovers (and the one it defers), the member-ledger movement behind a PF deduction it had always
/// been computing and never recording, the variance gate on approval, and the disbursement whose
/// split has to add up.
/// </para>
/// </summary>
[Collection("postgres")]
public sealed class HrMoneyTests(PostgresFixture pg)
{
    private static long _branchSeed = 8600;
    private static long NextBranch() => Interlocked.Increment(ref _branchSeed);

    private static AuditWriter Audit() => new(TimeProvider.System);
    private static ApprovalEngine Approvals() => new(Audit(), TimeProvider.System);
    private static FiscalCalendar Fiscal() => new(7);
    private static PolicyResolver Policies() => new(Audit(), TimeProvider.System);
    private static LoanService Loans() =>
        new(new NumberSeriesService(), Approvals(), Audit(), Fiscal(), TimeProvider.System);
    private static DisbursementService Money() =>
        new(new NumberSeriesService(), Audit(), Fiscal(), TimeProvider.System);
    private static PayrollService Payroll() =>
        new(new NumberSeriesService(), Audit(), Approvals(), Policies(), Fiscal(), TimeProvider.System);
    private static CompensationService Compensation() =>
        new(new EmployeeService(new NumberSeriesService(), Audit(), TimeProvider.System),
            Approvals(), Audit(), new NumberSeriesService(), Fiscal(), TimeProvider.System);

    private static readonly DateOnly Period = new(2026, 3, 1);

    // ---- the loan lifecycle (G41) ------------------------------------------------------------

    [Fact]
    public async Task A_loan_is_not_recovered_until_it_is_approved_and_disbursed()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000);

        var loan = await tx.RunAsync(s => Loans().RequestAsync(
            s.Hr, s.Kernel, branch, employeeId, "loan", 12_000, 12, Period, "roof repair",
            1, "Farid"));

        // Requested only: the run must not touch it.
        await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));

        await using var hr = HrContext();
        Assert.Empty(await hr.LoanInstallments.Where(i => i.LoanId == loan.Id).ToListAsync());
        Assert.Equal(0, (await hr.Loans.SingleAsync(l => l.Id == loan.Id)).RecoveredTaka);
    }

    [Fact]
    public async Task A_disbursed_loan_is_recovered_by_the_run_and_shows_on_the_payslip()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000);
        await SeedLoanComponentAsync(tx, branch);

        var loan = await ApprovedLoanAsync(tx, branch, employeeId, 12_000, 12);

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));

        await using var hr = HrContext();
        var installment = await hr.LoanInstallments.SingleAsync(i => i.LoanId == loan.Id);
        Assert.Equal(1_000, installment.AmountTaka);
        Assert.False(installment.Deferred);
        Assert.Equal(run.Id, installment.PayrollRunId);
        Assert.Equal(1_000, (await hr.Loans.SingleAsync(l => l.Id == loan.Id)).RecoveredTaka);

        // And the employee can see why their pay moved.
        var line = await hr.PayrollLines.SingleAsync(l => l.RunId == run.Id && l.EmployeeId == employeeId);
        var component = await hr.PayrollComponentLines
            .SingleAsync(c => c.PayrollLineId == line.Id && c.ComponentCode == "LOAN");
        Assert.Equal(1_000, component.AmountTaka);
        Assert.Contains("outstanding", component.Basis);
        Assert.Equal(29_000, line.NetPayTaka);
    }

    /// <summary>
    /// The rule the whole design turns on. A skipped installment is invisible; a deferred one is a
    /// row the outstanding statement can explain, and the employee is never paid below the floor.
    /// </summary>
    [Fact]
    public async Task An_installment_that_would_breach_the_floor_is_deferred_and_recorded()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        // Gross 6,000 against a 5,500 floor leaves 500 of room; the installment is 1,000.
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 6_000, minimumNet: 5_500);
        await SeedLoanComponentAsync(tx, branch);

        var loan = await ApprovedLoanAsync(tx, branch, employeeId, 12_000, 12);

        await tx.RunAsync(s => Payroll().GenerateAsync(s.Hr, s.Kernel, branch, Period, 1, "Farid"));

        await using var hr = HrContext();
        var installment = await hr.LoanInstallments.SingleAsync(i => i.LoanId == loan.Id);
        Assert.True(installment.Deferred);
        Assert.Equal(0, installment.AmountTaka);
        Assert.Equal(0, (await hr.Loans.SingleAsync(l => l.Id == loan.Id)).RecoveredTaka);

        var line = await hr.PayrollLines.SingleAsync(l => l.EmployeeId == employeeId);
        Assert.Equal(6_000, line.NetPayTaka);
        Assert.Contains("deferred", line.Note);
    }

    [Fact]
    public async Task Foreclosing_settles_the_balance_and_leaves_a_movement_behind_it()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000);
        var loan = await ApprovedLoanAsync(tx, branch, employeeId, 12_000, 12);

        await tx.RunAsync(s => Loans().ForecloseAsync(
            s.Hr, s.Kernel, branch, loan.Id, Period, 1, "Farid"));

        await using var hr = HrContext();
        var after = await hr.Loans.SingleAsync(l => l.Id == loan.Id);
        Assert.Equal(LoanState.Foreclosed, after.State);
        Assert.Equal(12_000, after.RecoveredTaka);
        Assert.Equal(12_000, (await hr.LoanInstallments.SingleAsync(i => i.LoanId == loan.Id)).AmountTaka);
    }

    [Fact]
    public async Task A_write_off_keeps_the_principal_the_recovery_and_the_reason()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000);
        var loan = await ApprovedLoanAsync(tx, branch, employeeId, 12_000, 12);

        await tx.RunAsync(s => Loans().WriteOffAsync(
            s.Hr, s.Kernel, branch, loan.Id, "employee died in service", 1, "Farid"));

        await using var hr = HrContext();
        var after = await hr.Loans.SingleAsync(l => l.Id == loan.Id);
        Assert.Equal(LoanState.WrittenOff, after.State);
        // Hard rule 4: forgiven on the record, never erased from it.
        Assert.Equal(12_000, after.PrincipalTaka);
        Assert.Equal("employee died in service", after.WriteOffReason);
    }

    [Fact]
    public async Task A_write_off_needs_a_reason()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000);
        var loan = await ApprovedLoanAsync(tx, branch, employeeId, 12_000, 12);

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Loans().WriteOffAsync(s.Hr, s.Kernel, branch, loan.Id, "", 1, "Farid")));

        Assert.Contains("reason", e.Message);
    }

    // ---- the member ledger (G42, G43, G44) -----------------------------------------------------

    /// <summary>
    /// The keystone of the whole spec: the engine had always computed the provident-fund deduction
    /// and put it on the payslip, and had never told the member's balance. The money was taken every
    /// month and the balance it was taken <em>into</em> did not exist.
    /// </summary>
    [Fact]
    public async Task A_provident_fund_deduction_posts_a_member_ledger_entry()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000, pfComponent: true);

        await tx.RunAsync(s => Policies().SetPfAsync(
            s.Hr, s.Kernel, branch, new DateOnly(2020, 1, 1),
            employeeShareBp: 1_000, employerShareBp: 1_000, eligibilityMonths: 0,
            monthlyCapTaka: null, 1, "Farid"));

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));

        await using var hr = HrContext();
        var entry = await hr.LedgerEntries
            .SingleAsync(e => e.EmployeeId == employeeId && e.Kind == LedgerKind.ProvidentFund);

        Assert.Equal(run.Id, entry.PayrollRunId);
        Assert.Equal(3_000, entry.EmployeeShareTaka);      // 10% of 30,000
        Assert.Equal(3_000, entry.EmployerShareTaka);
        Assert.Contains("March 2026", entry.Narration);

        // The payslip and the ledger must agree to the taka, or the statement is fiction.
        var line = await hr.PayrollLines.SingleAsync(l => l.RunId == run.Id);
        var deduction = await hr.PayrollComponentLines
            .SingleAsync(c => c.PayrollLineId == line.Id && c.ComponentCode == "PF");
        Assert.Equal(entry.EmployeeShareTaka, deduction.AmountTaka);
    }

    [Fact]
    public async Task No_provident_fund_policy_writes_no_ledger_entry()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        await SeedPayableAsync(tx, branch, gross: 30_000, pfComponent: true);

        await tx.RunAsync(s => Payroll().GenerateAsync(s.Hr, s.Kernel, branch, Period, 1, "Farid"));

        await using var hr = HrContext();
        // D2: an unconfigured employer has no provident fund, and the honest record is no rows.
        Assert.Empty(await hr.LedgerEntries.Where(e => e.Kind == LedgerKind.ProvidentFund).ToListAsync());
    }

    // ---- the variance gate (G47, §9) -------------------------------------------------------------

    [Fact]
    public async Task A_run_cannot_be_approved_before_variance_is_reviewed()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        await SeedPayableAsync(tx, branch, gross: 30_000);

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));
        await tx.RunAsync(s => Payroll().ReviewAsync(s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Payroll().ApproveAsync(s.Hr, s.Kernel, branch, run.Id, 2, "Kamal")));

        Assert.Contains("variance", e.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reviewing_exceptions_builds_the_comparison()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000);

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));
        await tx.RunAsync(s => Payroll().ReviewAsync(s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));

        await using var hr = HrContext();
        var note = await hr.VarianceNotes.SingleAsync(v => v.RunId == run.Id);
        Assert.Equal(employeeId, note.EmployeeId);
        Assert.Equal(0, note.PreviousNetTaka);
        // First month for this branch: nobody was paid before, so this is "new", not an infinite rise.
        Assert.Equal(int.MaxValue, note.DeltaBp);
    }

    [Fact]
    public async Task An_unexplained_new_line_blocks_the_variance_review()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        await SeedPayableAsync(tx, branch, gross: 30_000);

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));
        await tx.RunAsync(s => Payroll().ReviewAsync(s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Payroll().ReviewVarianceAsync(s.Hr, s.Kernel, branch, run.Id, 1, "Farid")));

        Assert.Contains("needs a reason", e.Message);
    }

    [Fact]
    public async Task Explaining_every_out_of_tolerance_line_lets_the_run_through()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        await SeedPayableAsync(tx, branch, gross: 30_000);

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));
        await tx.RunAsync(s => Payroll().ReviewAsync(s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));

        await using (var read = HrContext())
        {
            foreach (var note in await read.VarianceNotes.Where(v => v.RunId == run.Id).ToListAsync())
            {
                await tx.RunAsync(s => Money().ExplainAsync(
                    s.Hr, s.Kernel, branch, note.Id, "first month on the payroll", 1, "Farid"));
            }
        }

        await tx.RunAsync(s => Payroll().ReviewVarianceAsync(s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));

        await using var hr = HrContext();
        var after = await hr.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        Assert.Equal(PayrollRunState.VarianceReviewed, after.State);
        Assert.NotNull(after.VarianceReviewedAt);
    }

    // ---- salary hold (G46) --------------------------------------------------------------------------

    /// <summary>
    /// A hold withholds <em>payment</em>. It does not un-earn a month's work, so the line keeps its
    /// figures and is marked — a payslip showing zero would tell the employee something false about
    /// what they are owed, and the run's own arithmetic constraint refuses it anyway.
    /// </summary>
    [Fact]
    public async Task A_held_employee_is_on_the_sheet_marked_held_and_keeps_their_figures()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000);

        await tx.RunAsync(s => Money().HoldAsync(
            s.Hr, s.Kernel, branch, employeeId, "under investigation", 1, "Farid"));

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));

        await using var hr = HrContext();
        var line = await hr.PayrollLines.SingleAsync(l => l.RunId == run.Id);

        // Present, not absent: the headcount stays true and the reason is on the record.
        Assert.Equal(1, run.EmployeeCount);
        Assert.True(line.Held);
        Assert.Equal("under investigation", line.HoldReason);
        Assert.Equal(30_000, line.GrossEarningsTaka);
        Assert.Equal(30_000, line.NetPayTaka);
    }

    [Fact]
    public async Task Only_one_live_hold_per_person()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000);

        await tx.RunAsync(s => Money().HoldAsync(
            s.Hr, s.Kernel, branch, employeeId, "first", 1, "Farid"));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Money().HoldAsync(s.Hr, s.Kernel, branch, employeeId, "second", 1, "Farid")));

        Assert.Contains("already held", e.Message);
    }

    [Fact]
    public async Task Releasing_a_hold_restores_the_next_run()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000);

        var hold = await tx.RunAsync(s => Money().HoldAsync(
            s.Hr, s.Kernel, branch, employeeId, "under investigation", 1, "Farid"));
        await tx.RunAsync(s => Money().ReleaseAsync(
            s.Hr, s.Kernel, branch, hold.Id, "cleared", 1, "Farid"));

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));

        await using var hr = HrContext();
        var line = await hr.PayrollLines.SingleAsync(l => l.RunId == run.Id);
        Assert.False(line.Held);
        Assert.Equal(30_000, line.NetPayTaka);
    }

    // ---- disbursement (G45) ---------------------------------------------------------------------------

    [Fact]
    public async Task A_batch_splits_by_method_and_the_split_adds_up()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        await SeedPayableAsync(tx, branch, gross: 30_000, code: "BANK", method: PaymentMethod.Bank);
        await SeedPayableAsync(tx, branch, gross: 20_000, code: "CASH", method: PaymentMethod.Cash,
            skipPolicy: true);

        var run = await LockedRunAsync(tx, branch);

        var batch = await tx.RunAsync(s => Money().PrepareAsync(
            s.Hr, s.Kernel, branch, run.Id, new DateOnly(2026, 4, 1), 1, "Farid"));

        await using var hr = HrContext();
        Assert.Equal(2, batch.LineCount);
        Assert.Equal(50_000, batch.TotalTaka);
        Assert.Equal(30_000, batch.BankTaka);
        Assert.Equal(20_000, batch.CashTaka);
        // The database refuses a split that does not add up; this proves it never gets the chance.
        Assert.Equal(batch.TotalTaka, batch.BankTaka + batch.CashTaka + batch.ChequeTaka);
    }

    [Fact]
    public async Task Somebody_marked_for_bank_with_no_account_goes_on_the_cash_sheet()
    {
        // Rather than into a bank file with a blank account number, which the bank rejects for the
        // whole batch rather than the one row.
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        await SeedPayableAsync(tx, branch, gross: 30_000, method: PaymentMethod.Bank, bankAccount: null);

        var run = await LockedRunAsync(tx, branch);
        var batch = await tx.RunAsync(s => Money().PrepareAsync(
            s.Hr, s.Kernel, branch, run.Id, new DateOnly(2026, 4, 1), 1, "Farid"));

        await using var hr = HrContext();
        var line = await hr.DisbursementLines.SingleAsync(l => l.DisbursementId == batch.Id);
        Assert.Equal(PaymentMethod.Cash, line.Method);
        Assert.Equal(30_000, batch.CashTaka);
        Assert.Equal(0, batch.BankTaka);
    }

    [Fact]
    public async Task A_held_employee_is_not_disbursed()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000);
        await tx.RunAsync(s => Money().HoldAsync(
            s.Hr, s.Kernel, branch, employeeId, "under investigation", 1, "Farid"));

        var run = await LockedRunAsync(tx, branch);

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Money().PrepareAsync(s.Hr, s.Kernel, branch, run.Id, new DateOnly(2026, 4, 1), 1, "Farid")));

        Assert.Contains("nobody to pay", e.Message);
    }

    [Fact]
    public async Task Paying_the_last_line_disburses_the_run()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        await SeedPayableAsync(tx, branch, gross: 30_000);

        var run = await LockedRunAsync(tx, branch, post: true);
        var batch = await tx.RunAsync(s => Money().PrepareAsync(
            s.Hr, s.Kernel, branch, run.Id, new DateOnly(2026, 4, 1), 1, "Farid"));

        await using (var read = HrContext())
        {
            foreach (var line in await read.DisbursementLines
                         .Where(l => l.DisbursementId == batch.Id).ToListAsync())
            {
                await tx.RunAsync(s => Money().MarkPaidAsync(
                    s.Hr, s.Kernel, branch, line.Id, "TXN-1", 1, "Farid"));
            }
        }

        await using var hr = HrContext();
        Assert.Equal(DisbursementState.Paid,
            (await hr.Disbursements.SingleAsync(d => d.Id == batch.Id)).State);
        // §9's terminal state, reached only when the last person actually has their money.
        var after = await hr.PayrollRuns.SingleAsync(r => r.Id == run.Id);
        Assert.Equal(PayrollRunState.Disbursed, after.State);
        Assert.NotNull(after.DisbursedAt);
    }

    [Fact]
    public async Task Nobody_is_paid_twice()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        await SeedPayableAsync(tx, branch, gross: 30_000);
        var run = await LockedRunAsync(tx, branch);
        var batch = await tx.RunAsync(s => Money().PrepareAsync(
            s.Hr, s.Kernel, branch, run.Id, new DateOnly(2026, 4, 1), 1, "Farid"));

        long lineId;
        await using (var read = HrContext())
            lineId = (await read.DisbursementLines.FirstAsync(l => l.DisbursementId == batch.Id)).Id;

        await tx.RunAsync(s => Money().MarkPaidAsync(s.Hr, s.Kernel, branch, lineId, "TXN-1", 1, "Farid"));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Money().MarkPaidAsync(s.Hr, s.Kernel, branch, lineId, "TXN-2", 1, "Farid")));

        Assert.Contains("already marked paid", e.Message);
    }

    [Fact]
    public async Task One_batch_per_run()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        await SeedPayableAsync(tx, branch, gross: 30_000);
        var run = await LockedRunAsync(tx, branch);

        await tx.RunAsync(s => Money().PrepareAsync(
            s.Hr, s.Kernel, branch, run.Id, new DateOnly(2026, 4, 1), 1, "Farid"));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Money().PrepareAsync(s.Hr, s.Kernel, branch, run.Id, new DateOnly(2026, 4, 1), 1, "Farid")));

        Assert.Contains("already has batch", e.Message);
    }

    [Fact]
    public async Task An_unlocked_run_is_not_disbursed()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        await SeedPayableAsync(tx, branch, gross: 30_000);

        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Money().PrepareAsync(s.Hr, s.Kernel, branch, run.Id, new DateOnly(2026, 4, 1), 1, "Farid")));

        Assert.Contains("only a locked run", e.Message);
    }

    // ---- bonus (G38) --------------------------------------------------------------------------------

    [Fact]
    public async Task A_bonus_sheet_lists_the_excluded_with_a_reason_rather_than_dropping_them()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (senior, _) = await SeedPayableAsync(tx, branch, gross: 30_000, code: "OLD",
            joinedOn: new DateOnly(2020, 1, 1));
        var (junior, _) = await SeedPayableAsync(tx, branch, gross: 20_000, code: "NEW",
            joinedOn: new DateOnly(2026, 2, 1), skipPolicy: true);

        long componentId;
        await using (var read = HrContext())
            componentId = (await read.PayComponents.FirstAsync(c => c.Code == $"BASIC{branch}")).Id;

        var sheet = await tx.RunAsync(s => Compensation().DraftBonusAsync(
            s.Hr, s.Kernel, branch, new BonusSheet
            {
                Kind = BonusKind.Festival,
                Title = "Eid 2026",
                Period = Period,
                BasedOnComponentId = componentId,
                PercentBp = 10_000,                 // one month of basic
                MinimumServiceMonths = 12,
                SheetNo = "",
                CreatedByName = "Farid",
            }, 1, "Farid"));

        await using var hr = HrContext();
        Assert.Equal(1, sheet.EligibleCount);
        Assert.Equal(1, sheet.ExcludedCount);
        Assert.Equal(30_000, sheet.TotalTaka);

        var eligible = await hr.BonusLines.SingleAsync(l => l.SheetId == sheet.Id && l.EmployeeId == senior);
        Assert.True(eligible.Eligible);
        Assert.Equal(30_000, eligible.AmountTaka);

        var excluded = await hr.BonusLines.SingleAsync(l => l.SheetId == sheet.Id && l.EmployeeId == junior);
        Assert.False(excluded.Eligible);
        Assert.Contains("12 required", excluded.ExclusionReason);
    }

    [Fact]
    public async Task A_bonus_with_neither_a_base_nor_an_amount_is_refused()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Compensation().DraftBonusAsync(s.Hr, s.Kernel, branch, new BonusSheet
            {
                Kind = BonusKind.AdHoc, Title = "Nothing", Period = Period,
                SheetNo = "", CreatedByName = "Farid",
            }, 1, "Farid")));

        Assert.Contains("percentage of a component or a flat amount", e.Message);
    }

    // ---- increment (G39) -------------------------------------------------------------------------------

    [Fact]
    public async Task An_increment_previews_old_and_new_and_applies_effective_dated()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var (employeeId, _) = await SeedPayableAsync(tx, branch, gross: 30_000,
            joinedOn: new DateOnly(2020, 1, 1));

        var run = await tx.RunAsync(s => Compensation().ProposeAsync(
            s.Hr, s.Kernel, branch, new CompensationRun
            {
                Kind = CompensationRunKind.Increment,
                Title = "Annual increment 2026",
                EffectiveFrom = new DateOnly(2026, 7, 1),
                PercentBp = 1_000,                    // 10%
                RunNo = "",
                CreatedByName = "Farid",
            }, 1, "Farid"));

        await tx.RunAsync(s => Compensation().PreviewAsync(
            s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));

        await using (var read = HrContext())
        {
            var line = await read.CompensationLines.SingleAsync(l => l.RunId == run.Id);
            Assert.Equal(30_000, line.OldGrossTaka);
            Assert.Equal(33_000, line.NewGrossTaka);
            Assert.Contains("10% of 30,000", line.Basis);
            Assert.False(line.Applied);
        }

        // Approve through the engine, then apply.
        await tx.RunAsync(s => Compensation().RaiseRunApprovalAsync(
            s.Hr, s.Kernel, branch, run.Id, 1, "HR Officer"));
        await using (var read = HrContext())
        {
            var after = await read.CompensationRuns.SingleAsync(r => r.Id == run.Id);
            if (after.State != CompensationRunState.Approved)
            {
                await tx.RunAsync(async s =>
                {
                    var request = await s.Kernel.ApprovalRequests
                        .SingleAsync(r => r.Id == after.ApprovalRequestId);
                    await Approvals().DecideAsync(s.Kernel, request.Id, true, 2, "Kamal", "ok");
                });
                await tx.RunAsync(s => Compensation().ApplyRunApprovalAsync(
                    s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));
            }
        }

        await tx.RunAsync(s => Compensation().ApplyAsync(s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));

        await using var hr = HrContext();
        // Effective-dated, not overwritten: the old structure closes the day before.
        var structures = await hr.PayStructures
            .Where(p => p.EmployeeId == employeeId).OrderBy(p => p.EffectiveFrom).ToListAsync();
        Assert.Equal(2, structures.Count);
        Assert.Equal(new DateOnly(2026, 6, 30), structures[0].EffectiveTo);
        Assert.Equal(new DateOnly(2026, 7, 1), structures[1].EffectiveFrom);
        Assert.Equal("increment", structures[1].Reason);

        var newAmount = await hr.PayStructureComponents
            .Where(c => c.PayStructureId == structures[1].Id).SumAsync(c => c.AmountTaka);
        Assert.Equal(33_000, newAmount);

        Assert.True(await hr.EmploymentEvents
            .AnyAsync(e => e.EmployeeId == employeeId && e.Kind == EmploymentEventKind.Increment));
    }

    [Fact]
    public async Task An_increment_that_touches_nobody_cannot_be_sent_for_approval()
    {
        var branch = NextBranch();
        BranchScope.Current = branch;
        var tx = await TxAsync();

        var run = await tx.RunAsync(s => Compensation().ProposeAsync(
            s.Hr, s.Kernel, branch, new CompensationRun
            {
                Kind = CompensationRunKind.Increment, Title = "Empty",
                EffectiveFrom = new DateOnly(2026, 7, 1), PercentBp = 1_000,
                RunNo = "", CreatedByName = "Farid",
            }, 1, "Farid"));
        await tx.RunAsync(s => Compensation().PreviewAsync(s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));

        var e = await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(s =>
            Compensation().RaiseRunApprovalAsync(s.Hr, s.Kernel, branch, run.Id, 1, "HR Officer")));

        Assert.Contains("Nobody in the cohort qualifies", e.Message);
    }

    // ---- fixtures ---------------------------------------------------------------------------------

    private async Task<Loan> ApprovedLoanAsync(
        HrTx tx, long branch, long employeeId, long principal, int installments)
    {
        var loan = await tx.RunAsync(s => Loans().RequestAsync(
            s.Hr, s.Kernel, branch, employeeId, "loan", principal, installments, Period,
            "test", 1, "Farid"));

        await tx.RunAsync(async s =>
        {
            var row = await s.Hr.Loans.SingleAsync(l => l.Id == loan.Id);
            row.State = LoanState.Approved;
            row.ApprovedAt = DateTimeOffset.UtcNow;
            row.ApprovedBy = 1;
            await s.Hr.SaveChangesAsync();
        });

        await tx.RunAsync(s => Loans().DisburseAsync(s.Hr, s.Kernel, branch, loan.Id, 1, "Farid"));
        return loan;
    }

    private async Task<PayrollRun> LockedRunAsync(HrTx tx, long branch, bool post = false)
    {
        var run = await tx.RunAsync(s => Payroll().GenerateAsync(
            s.Hr, s.Kernel, branch, Period, 1, "Farid"));
        await tx.RunAsync(s => Payroll().ReviewAsync(s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));

        await using (var read = HrContext())
        {
            foreach (var note in await read.VarianceNotes.Where(v => v.RunId == run.Id).ToListAsync())
            {
                await tx.RunAsync(s => Money().ExplainAsync(
                    s.Hr, s.Kernel, branch, note.Id, "first month", 1, "Farid"));
            }
        }

        await tx.RunAsync(s => Payroll().ReviewVarianceAsync(s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));
        await tx.RunAsync(s => Payroll().ApproveAsync(s.Hr, s.Kernel, branch, run.Id, 2, "Kamal"));
        await tx.RunAsync(s => Payroll().LockAsync(s.Hr, s.Kernel, branch, run.Id, 1, "Farid"));

        if (post)
        {
            await tx.RunAsync(async s =>
            {
                var row = await s.Hr.PayrollRuns.SingleAsync(r => r.Id == run.Id);
                row.State = PayrollRunState.Posted;
                await s.Hr.SaveChangesAsync();
            });
        }

        await using var hr = HrContext();
        return await hr.PayrollRuns.SingleAsync(r => r.Id == run.Id);
    }

    /// <summary>An employee with a pay structure, and a payroll policy for the branch.</summary>
    private async Task<(long Id, string Code)> SeedPayableAsync(
        HrTx tx, long branch, long gross, long minimumNet = 0, string code = "M-001",
        DateOnly? joinedOn = null, string method = PaymentMethod.Bank,
        string? bankAccount = "1234567890", bool pfComponent = false, bool skipPolicy = false)
        => await tx.RunAsync(async s =>
        {
            if (!skipPolicy && !await s.Hr.PayrollPolicies.AnyAsync(p => p.BranchId == branch))
            {
                s.Hr.PayrollPolicies.Add(new PayrollPolicy
                {
                    BranchId = branch,
                    EffectiveFrom = new DateOnly(2020, 1, 1),
                    DayCountConvention = DayCountConvention.Fixed30,
                    MinimumNetPayTaka = minimumNet,
                    CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
                });
            }

            var basic = await s.Hr.PayComponents.FirstOrDefaultAsync(c => c.Code == $"BASIC{branch}");
            if (basic is null)
            {
                basic = new PayComponent
                {
                    BranchId = branch, Code = $"BASIC{branch}", Name = "Basic",
                    Kind = PayComponentKind.Earning, CalcMethod = PayComponentCalc.Fixed,
                    DisplayOrder = 1, Active = true, PfApplicable = true, Taxable = true,
                };
                s.Hr.PayComponents.Add(basic);
            }

            if (pfComponent && !await s.Hr.PayComponents.AnyAsync(c => c.Code == "PF"))
            {
                s.Hr.PayComponents.Add(new PayComponent
                {
                    BranchId = branch, Code = "PF", Name = "Provident fund",
                    Kind = PayComponentKind.Deduction, CalcMethod = PayComponentCalc.Computed,
                    ComputedKind = ComputedComponent.ProvidentFund,
                    DisplayOrder = 50, Active = true,
                });
            }
            await s.Hr.SaveChangesAsync();

            var employee = new Employee
            {
                BranchId = branch,
                EmployeeCode = $"M{branch}-{code}",
                FullName = "Money Subject " + code,
                JoinedOn = joinedOn ?? new DateOnly(2021, 1, 1),
                PaymentMethod = method,
                BankName = "Sonali Bank",
                BankAccountNo = bankAccount,
                BankRoutingNo = "200270435",
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.Employees.Add(employee);
            await s.Hr.SaveChangesAsync();

            // An assignment, because a compensation run selects its cohort by unit, grade and
            // designation — somebody with no assignment is in no cohort.
            var unit = await s.Hr.OrgUnits.FirstOrDefaultAsync(u => u.BranchId == branch);
            if (unit is null)
            {
                unit = new OrgUnit { BranchId = branch, Code = $"U{branch}", Name = "Unit", Active = true };
                s.Hr.OrgUnits.Add(unit);
                s.Hr.Designations.Add(new Designation
                {
                    BranchId = branch, Code = $"D{branch}", Name = "Officer", Active = true,
                });
                s.Hr.Grades.Add(new Grade
                {
                    BranchId = branch, Code = $"G{branch}", Name = "Grade 1", Rank = 1, Active = true,
                });
                await s.Hr.SaveChangesAsync();
            }
            var designation = await s.Hr.Designations.FirstAsync(d => d.BranchId == branch);
            var grade = await s.Hr.Grades.FirstAsync(g => g.BranchId == branch);

            s.Hr.Assignments.Add(new EmployeeAssignment
            {
                BranchId = branch, EmployeeId = employee.Id,
                OrgUnitId = unit.Id, DesignationId = designation.Id, GradeId = grade.Id,
                EffectiveFrom = employee.JoinedOn, Reason = "joining",
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            });
            await s.Hr.SaveChangesAsync();

            var structure = new EmployeePayStructure
            {
                BranchId = branch, EmployeeId = employee.Id,
                EffectiveFrom = employee.JoinedOn, Reason = "joining",
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.PayStructures.Add(structure);
            await s.Hr.SaveChangesAsync();

            s.Hr.PayStructureComponents.Add(new EmployeePayComponent
            {
                PayStructureId = structure.Id, ComponentId = basic.Id, AmountTaka = gross,
            });
            await s.Hr.SaveChangesAsync();

            return (employee.Id, employee.EmployeeCode);
        });

    private async Task SeedLoanComponentAsync(HrTx tx, long branch)
        => await tx.RunAsync(async s =>
        {
            if (await s.Hr.PayComponents.AnyAsync(c => c.Code == "LOAN")) return;
            s.Hr.PayComponents.Add(new PayComponent
            {
                BranchId = branch, Code = "LOAN", Name = "Loan installment",
                Kind = PayComponentKind.Deduction, CalcMethod = PayComponentCalc.Computed,
                ComputedKind = ComputedComponent.LoanInstallment,
                DisplayOrder = 60, Active = true,
            });
            await s.Hr.SaveChangesAsync();
        });

    private async Task<HrTx> TxAsync()
    {
        await MigrateAsync();
        return new HrTx(Config());
    }

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Hms"] = pg.ConnectionString,
        })
        .Build();

    private HrDbContext HrContext() => new(Options<HrDbContext>("hr"));

    private DbContextOptions<T> Options<T>(string schema) where T : DbContext
        => new DbContextOptionsBuilder<T>()
            .UseNpgsql(pg.ConnectionString, o => o.MigrationsHistoryTable("__ef_migrations", schema))
            .UseSnakeCaseNamingConvention()
            .Options;

    private async Task MigrateAsync()
    {
        await using var kernel = new KernelDbContext(Options<KernelDbContext>("kernel"));
        await kernel.Database.MigrateAsync();
        await using var auth = new AuthDbContext(Options<AuthDbContext>("adm"));
        await auth.Database.MigrateAsync();
        await using var hr = new HrDbContext(Options<HrDbContext>("hr"));
        await hr.Database.MigrateAsync();
    }
}
