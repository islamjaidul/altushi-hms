using Hms.Hr;
using Hms.Hr.Data;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0057 — the loan recovery floor, expressed as a pure decision so every boundary is pinned in
/// milliseconds (module PRD §7.9, G41).
/// <para>
/// The rule that carries the weight: an installment that would take net pay below the employer's
/// minimum is <b>deferred, not skipped</b>. A skipped installment is invisible; a deferred one is a
/// row the outstanding statement can explain.
/// </para>
/// </summary>
public class LoanRecoveryTests
{
    private static Loan Loan(
        long principal, long installment, long recovered = 0,
        string state = LoanState.Disbursed, int startMonth = 1, long id = 1) => new()
    {
        Id = id,
        LoanNo = $"LN-{id:D4}",
        EmployeeId = 1,
        Kind = "loan",
        PrincipalTaka = principal,
        InstallmentTaka = installment,
        InstallmentCount = 12,
        StartPeriod = new DateOnly(2026, startMonth, 1),
        State = state,
        RecoveredTaka = recovered,
        CreatedByName = "Farid",
    };

    private static readonly DateOnly March = new(2026, 3, 1);

    [Fact]
    public void An_installment_within_the_room_above_the_floor_is_taken()
    {
        var plan = LoanService.Plan([Loan(12_000, 1_000)], March, netBeforeRecovery: 20_000,
            minimumNetPay: 5_000);

        var r = Assert.Single(plan);
        Assert.False(r.Deferred);
        Assert.Equal(1_000, r.AmountTaka);
        Assert.Contains("1,000 of 12,000 outstanding", r.Basis);
    }

    [Fact]
    public void An_installment_that_would_breach_the_floor_is_deferred_not_skipped()
    {
        var plan = LoanService.Plan([Loan(12_000, 1_000)], March, netBeforeRecovery: 5_500,
            minimumNetPay: 5_000);

        var r = Assert.Single(plan);
        Assert.True(r.Deferred);
        Assert.Equal(0, r.AmountTaka);
        Assert.Contains("deferred", r.Basis);
        Assert.Contains("floor", r.Basis);
    }

    [Fact]
    public void An_installment_exactly_equal_to_the_room_is_taken()
    {
        // The boundary: room is 1,000 and the installment is 1,000. Taking it lands the employee
        // exactly on the floor, which is where the floor says they may land.
        var plan = LoanService.Plan([Loan(12_000, 1_000)], March, 6_000, 5_000);

        Assert.False(Assert.Single(plan).Deferred);
    }

    [Fact]
    public void The_last_installment_never_takes_more_than_is_outstanding()
    {
        // 11,500 of a 12,000 loan recovered; the schedule says 1,000 but only 500 is owed.
        var plan = LoanService.Plan([Loan(12_000, 1_000, recovered: 11_500)], March, 20_000, 5_000);

        Assert.Equal(500, Assert.Single(plan).AmountTaka);
    }

    [Fact]
    public void A_loan_that_has_not_started_is_left_alone()
    {
        var plan = LoanService.Plan([Loan(12_000, 1_000, startMonth: 6)], March, 20_000, 5_000);

        Assert.Empty(plan);
    }

    [Theory]
    [InlineData(LoanState.Requested)]
    [InlineData(LoanState.Approved)]
    [InlineData(LoanState.Closed)]
    [InlineData(LoanState.WrittenOff)]
    [InlineData(LoanState.Foreclosed)]
    public void Only_a_disbursed_loan_recovers(string state)
    {
        // Money that has not gone out cannot come back, and money already settled must not be
        // taken twice.
        var plan = LoanService.Plan([Loan(12_000, 1_000, state: state)], March, 20_000, 5_000);

        Assert.Empty(plan);
    }

    [Fact]
    public void A_fully_recovered_loan_produces_nothing()
    {
        var plan = LoanService.Plan([Loan(12_000, 1_000, recovered: 12_000)], March, 20_000, 5_000);

        Assert.Empty(plan);
    }

    /// <summary>
    /// Oldest first, so the loan closest to closing finishes rather than every loan crawling. With
    /// room for one installment and two loans open, the older one is taken and the newer deferred.
    /// </summary>
    [Fact]
    public void Loans_are_taken_oldest_first_and_the_room_runs_out_in_order()
    {
        var older = Loan(12_000, 1_000, startMonth: 1, id: 1);
        var newer = Loan(12_000, 1_000, startMonth: 2, id: 2);

        var plan = LoanService.Plan([newer, older], March, netBeforeRecovery: 6_000,
            minimumNetPay: 5_000);

        Assert.Equal(2, plan.Count);
        Assert.Equal(older.Id, plan[0].Loan.Id);
        Assert.False(plan[0].Deferred);
        Assert.True(plan[1].Deferred);
    }

    [Fact]
    public void No_room_at_all_defers_everything()
    {
        var plan = LoanService.Plan([Loan(12_000, 1_000)], March, netBeforeRecovery: 4_000,
            minimumNetPay: 5_000);

        Assert.True(Assert.Single(plan).Deferred);
    }
}

/// <summary>Spec 0057 — the variance comparison (module PRD §7.7, G47).</summary>
public class VarianceTests
{
    [Theory]
    [InlineData(10_000, 10_000, 0)]
    [InlineData(10_000, 11_000, 1_000)]        // +10%
    [InlineData(10_000, 20_000, 10_000)]       // doubled
    [InlineData(10_000, 5_000, -5_000)]        // halved
    [InlineData(10_000, 0, -10_000)]           // paid nothing
    public void The_movement_is_basis_points_of_the_previous_net(long before, long now, int expected)
    {
        Assert.Equal(expected, DisbursementService.DeltaBp(before, now));
    }

    /// <summary>
    /// A first appearance is reported as new, not as an infinite rise. Treating a joiner as a
    /// variance would fill this register with noise every month a hire lands, and the register only
    /// works if an operator reads all of it.
    /// </summary>
    [Fact]
    public void Somebody_the_previous_run_did_not_pay_is_new_rather_than_infinite()
    {
        Assert.Equal(int.MaxValue, DisbursementService.DeltaBp(0, 25_000));
        Assert.Equal(0, DisbursementService.DeltaBp(0, 0));
    }

    [Fact]
    public void A_new_appearance_always_needs_a_reason()
    {
        Assert.True(DisbursementService.NeedsReason(int.MaxValue, toleranceBp: 100_000));
    }

    [Theory]
    [InlineData(1_500, 2_000, false)]          // 15% move, 20% tolerance
    [InlineData(2_000, 2_000, false)]          // exactly at tolerance
    [InlineData(2_001, 2_000, true)]
    [InlineData(-2_001, 2_000, true)]          // a fall is as interesting as a rise
    public void Only_a_move_beyond_the_tolerance_needs_explaining(int delta, int tolerance, bool needed)
    {
        Assert.Equal(needed, DisbursementService.NeedsReason(delta, tolerance));
    }

    [Fact]
    public void A_zero_tolerance_makes_every_change_explainable()
    {
        // A defensible choice for a small employer, and the reason the tolerance is theirs to set.
        Assert.True(DisbursementService.NeedsReason(1, 0));
        Assert.False(DisbursementService.NeedsReason(0, 0));
    }
}

/// <summary>Spec 0057 — the bank file (module PRD §7.7, G45).</summary>
public class BankFileTests
{
    private static Disbursement Batch() => new()
    {
        Id = 1, BranchId = 1, RunId = 1, BatchNo = "DB-2026-27-0001",
        ValueDate = new DateOnly(2026, 9, 1), CreatedByName = "Farid",
    };

    private static DisbursementLine Line(
        string code, string name, string method, long amount, string? account = "1234567890") => new()
    {
        BranchId = 1, DisbursementId = 1, PayrollLineId = 1, EmployeeId = 1,
        EmployeeCode = code, EmployeeName = name, Method = method, AmountTaka = amount,
        BankName = "Sonali Bank", BankAccountNo = account, BankRoutingNo = "200270435",
    };

    private static string Text(byte[] bytes) =>
        System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿');

    [Fact]
    public void Only_bank_lines_reach_the_bank_file()
    {
        var bytes = DisbursementService.BankFile(Batch(),
        [
            Line("E1", "Nasrin Akter", PaymentMethod.Bank, 25_000),
            Line("E2", "Kamal Hossain", PaymentMethod.Cash, 18_000),
            Line("E3", "Rafiq Uddin", PaymentMethod.Cheque, 30_000),
        ]);

        var text = Text(bytes);
        Assert.Contains("Nasrin Akter", text);
        Assert.DoesNotContain("Kamal Hossain", text);
        Assert.DoesNotContain("Rafiq Uddin", text);
    }

    /// <summary>
    /// The BOM, for the same reason the report exporter carries one: without it Excel on a
    /// Bangladeshi desktop mojibakes every Bangla name in the account-name column.
    /// </summary>
    [Fact]
    public void The_file_carries_a_utf8_bom()
    {
        var bytes = DisbursementService.BankFile(Batch(), [Line("E1", "নাসরিন আক্তার", PaymentMethod.Bank, 1)]);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3));
        Assert.Contains("নাসরিন আক্তার", Text(bytes));
    }

    [Fact]
    public void A_comma_in_a_name_is_quoted_rather_than_breaking_the_row()
    {
        var bytes = DisbursementService.BankFile(Batch(),
            [Line("E1", "Rahman, Md. Abdul", PaymentMethod.Bank, 25_000)]);

        Assert.Contains("\"Rahman, Md. Abdul\"", Text(bytes));
    }

    [Fact]
    public void A_leading_equals_is_defused_rather_than_evaluated()
    {
        // Same treatment ReportCsv gives every cell: a name a bank returns starting with = is a
        // formula to Excel, and a bank file is opened in Excel more often than not.
        var bytes = DisbursementService.BankFile(Batch(),
            [Line("E1", "=cmd|calc", PaymentMethod.Bank, 1)]);

        Assert.Contains("'=cmd|calc", Text(bytes));
    }

    [Fact]
    public void Every_row_carries_a_reference_the_bank_can_return()
    {
        var bytes = DisbursementService.BankFile(Batch(), [Line("E1", "Nasrin", PaymentMethod.Bank, 1)]);

        Assert.Contains("DB-2026-27-0001/E1", Text(bytes));
        Assert.Contains("2026-09-01", Text(bytes));
    }

    [Fact]
    public void The_header_names_every_column_the_upload_needs()
    {
        var bytes = DisbursementService.BankFile(Batch(), [Line("E1", "Nasrin", PaymentMethod.Bank, 1)]);
        var header = Text(bytes).Split("\r\n")[0];

        Assert.Equal("account_no,account_name,routing_no,bank,amount_taka,reference,value_date", header);
    }

    [Fact]
    public void An_empty_bank_split_is_a_header_and_nothing_else()
    {
        // A batch paid entirely in cash still produces a well-formed file rather than an error.
        var bytes = DisbursementService.BankFile(Batch(),
            [Line("E1", "Kamal", PaymentMethod.Cash, 18_000)]);

        Assert.Equal(2, Text(bytes).Split("\r\n").Length);
    }
}
