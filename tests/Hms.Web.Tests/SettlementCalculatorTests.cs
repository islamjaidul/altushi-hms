using Hms.Hr;
using Hms.Hr.Data;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0056 — the final settlement's arithmetic (module PRD §7.1, G19).
/// <para>
/// The calculator is pure, so the whole of §7.1's list is pinned here in milliseconds. The cases
/// that matter most are the ones where a policy is <b>missing</b>: ADR-0027 forbids inventing an
/// end-of-service formula, and a zero on a settlement statement reads to a departing employee as
/// "you earned nothing" rather than "your employer never configured this".
/// </para>
/// </summary>
public class SettlementCalculatorTests
{
    /// <summary>
    /// A five-year permanent employee resigning at the end of a month, paid to the end of the
    /// previous one. 30,000 gross over a 30-day divisor is a clean 1,000/day, so every figure below
    /// is checkable by hand — which is the point of a statement that states its basis.
    /// </summary>
    private static SettlementInput Standard(Action<SettlementInputBuilder>? tweak = null)
    {
        var b = new SettlementInputBuilder();
        tweak?.Invoke(b);
        return b.Build();
    }

    private sealed class SettlementInputBuilder
    {
        public DateOnly JoinedOn = new(2021, 7, 1);
        public DateOnly LastWorkingDay = new(2026, 8, 31);
        public string Kind = SeparationKind.Resignation;
        public DateOnly NoticeOn = new(2026, 6, 1);
        public int NoticeDays;
        public bool AdjustNoticePay;
        public long Gross = 30_000;
        public int Denominator = 30;
        public DateOnly? PaidThrough = new(2026, 7, 31);
        public bool EncashLeave;
        public string? EncashableLeaveTypeName;
        public int EncashableBalanceBp;
        public GratuityInput? Gratuity;
        public long LoanOutstanding;
        public long ClearanceDues;

        public SettlementInput Build() => new()
        {
            JoinedOn = JoinedOn,
            LastWorkingDay = LastWorkingDay,
            Kind = Kind,
            NoticeOn = NoticeOn,
            NoticeDays = NoticeDays,
            AdjustNoticePay = AdjustNoticePay,
            MonthlyGrossTaka = Gross,
            DayCountDenominator = Denominator,
            PaidThrough = PaidThrough,
            EncashLeave = EncashLeave,
            EncashableLeaveTypeName = EncashableLeaveTypeName,
            EncashableBalanceBp = EncashableBalanceBp,
            Gratuity = Gratuity,
            LoanOutstandingTaka = LoanOutstanding,
            ClearanceDuesTaka = ClearanceDues,
        };
    }

    private static long AmountOf(SettlementDraft d, string source) =>
        d.Lines.Where(l => l.Source == source).Sum(l => l.AmountTaka);

    // ---- unpaid salary ------------------------------------------------------------------------

    [Fact]
    public void Unpaid_salary_runs_from_the_day_after_the_last_paid_period()
    {
        var draft = SettlementCalculator.Compute(Standard());

        // 1–31 August is 31 days at 1,000/day.
        Assert.Equal(31_000, AmountOf(draft, SettlementSource.UnpaidSalary));
    }

    [Fact]
    public void Unpaid_salary_starts_at_joining_when_nothing_has_ever_been_paid()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.PaidThrough = null;
            b.JoinedOn = new DateOnly(2026, 8, 20);
        }));

        // 20–31 August inclusive is 12 days.
        Assert.Equal(12_000, AmountOf(draft, SettlementSource.UnpaidSalary));
    }

    [Fact]
    public void Salary_already_paid_past_the_last_day_produces_a_note_not_a_negative_line()
    {
        var draft = SettlementCalculator.Compute(Standard(b => b.PaidThrough = new DateOnly(2026, 9, 30)));

        Assert.Equal(0, AmountOf(draft, SettlementSource.UnpaidSalary));
        Assert.Contains(draft.Notes, n => n.Contains("already paid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void No_pay_structure_computes_nothing_and_says_so()
    {
        // The dangerous case: a statement of zero looks like a computed answer.
        var draft = SettlementCalculator.Compute(Standard(b => b.Gross = 0));

        Assert.Empty(draft.Lines);
        Assert.Contains(draft.Notes, n => n.Contains("No pay structure is in force"));
    }

    // ---- gratuity (D2 / ADR-0027) --------------------------------------------------------------

    [Fact]
    public void An_unconfigured_gratuity_rule_produces_no_line_and_a_sentence()
    {
        var draft = SettlementCalculator.Compute(Standard());

        Assert.Equal(0, AmountOf(draft, SettlementSource.Gratuity));
        Assert.Contains(draft.Notes, n => n.Contains("No gratuity rule is configured"));
        // Never a silent zero: the note must tell the operator what to do about it.
        Assert.Contains(draft.Notes, n => n.Contains("Policies"));
    }

    [Fact]
    public void Gratuity_is_completed_years_times_the_employers_days_per_year()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
            b.Gratuity = new GratuityInput(7, MinimumServiceMonths: 12, DaysPerYearBp: 300_000,
                BaseMonthlyTaka: 30_000)));

        // 1 Jul 2021 → 31 Aug 2026 is 5 completed years; 30 days of pay per year at 1,000/day.
        Assert.Equal(5 * 30 * 1_000, AmountOf(draft, SettlementSource.Gratuity));
    }

    [Fact]
    public void Gratuity_names_the_rule_and_the_years_it_used()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
            b.Gratuity = new GratuityInput(7, 12, 300_000, 30_000)));

        var line = draft.Lines.Single(l => l.Source == SettlementSource.Gratuity);
        Assert.Contains("5 completed years", line.Basis);
        Assert.Contains("rule #7", line.Basis);
    }

    [Fact]
    public void Service_under_the_employers_minimum_earns_no_gratuity_and_states_both_numbers()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.JoinedOn = new DateOnly(2026, 1, 1);
            b.Gratuity = new GratuityInput(7, MinimumServiceMonths: 60, DaysPerYearBp: 300_000,
                BaseMonthlyTaka: 30_000);
        }));

        Assert.Equal(0, AmountOf(draft, SettlementSource.Gratuity));
        var note = Assert.Single(draft.Notes, n => n.Contains("gratuity rule requires"));
        Assert.Contains("7 months", note);
        Assert.Contains("60", note);
    }

    [Fact]
    public void Gratuity_falls_back_to_gross_when_the_rule_names_no_base_component()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
            b.Gratuity = new GratuityInput(7, 12, 300_000, BaseMonthlyTaka: 0)));

        Assert.Equal(5 * 30 * 1_000, AmountOf(draft, SettlementSource.Gratuity));
    }

    // ---- leave encashment ----------------------------------------------------------------------

    [Fact]
    public void Leave_is_not_encashed_unless_the_employer_says_so()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.EncashLeave = false;
            b.EncashableLeaveTypeName = "Earned leave";
            b.EncashableBalanceBp = 15 * 10_000;
        }));

        Assert.Equal(0, AmountOf(draft, SettlementSource.LeaveEncashment));
        Assert.Contains(draft.Notes, n => n.Contains("does not encash leave"));
    }

    [Fact]
    public void Encashment_is_the_balance_in_days_at_the_day_rate()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.EncashLeave = true;
            b.EncashableLeaveTypeName = "Earned leave";
            b.EncashableBalanceBp = 15 * 10_000;
        }));

        Assert.Equal(15_000, AmountOf(draft, SettlementSource.LeaveEncashment));
    }

    [Fact]
    public void Encashment_handles_a_half_day_balance_without_losing_it()
    {
        // 12.5 days is a real balance — leave is basis points of a day precisely so it can be.
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.EncashLeave = true;
            b.EncashableLeaveTypeName = "Earned leave";
            b.EncashableBalanceBp = 125_000;
        }));

        Assert.Equal(12_500, AmountOf(draft, SettlementSource.LeaveEncashment));
    }

    [Fact]
    public void Encashment_switched_on_with_no_leave_type_configured_says_so()
    {
        var draft = SettlementCalculator.Compute(Standard(b => b.EncashLeave = true));

        Assert.Equal(0, AmountOf(draft, SettlementSource.LeaveEncashment));
        Assert.Contains(draft.Notes, n => n.Contains("no encashable leave type is configured"));
    }

    // ---- notice, either way (§7.1) --------------------------------------------------------------

    [Fact]
    public void An_unconfigured_notice_period_adjusts_nothing_and_says_so()
    {
        var draft = SettlementCalculator.Compute(Standard());

        Assert.Equal(0, AmountOf(draft, SettlementSource.NoticePay));
        Assert.Contains(draft.Notes, n => n.Contains("No notice period is configured"));
    }

    [Fact]
    public void A_resignation_short_of_notice_has_the_shortfall_recovered()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.NoticeDays = 120;
            b.AdjustNoticePay = true;
            b.NoticeOn = new DateOnly(2026, 6, 1);          // 91 days served to 31 Aug
        }));

        var line = draft.Lines.Single(l => l.Source == SettlementSource.NoticePay);
        Assert.Equal(PayComponentKind.Deduction, line.Kind);
        Assert.Equal(29_000, line.AmountTaka);
    }

    [Fact]
    public void A_termination_short_of_notice_is_paid_in_lieu()
    {
        // §7.1's "either way": the side that cut the notice short is the side that pays.
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.Kind = SeparationKind.Termination;
            b.NoticeDays = 120;
            b.AdjustNoticePay = true;
        }));

        var line = draft.Lines.Single(l => l.Source == SettlementSource.NoticePay);
        Assert.Equal(PayComponentKind.Earning, line.Kind);
        Assert.Equal(29_000, line.AmountTaka);
    }

    [Fact]
    public void Notice_fully_served_adjusts_nothing()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.NoticeDays = 30;
            b.AdjustNoticePay = true;
        }));

        Assert.Equal(0, AmountOf(draft, SettlementSource.NoticePay));
        Assert.Contains(draft.Notes, n => n.Contains("days of notice were served"));
    }

    [Fact]
    public void Notice_is_not_adjusted_when_the_employer_has_switched_it_off()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.NoticeDays = 120;
            b.AdjustNoticePay = false;
        }));

        Assert.Equal(0, AmountOf(draft, SettlementSource.NoticePay));
    }

    // ---- recoveries and the net ------------------------------------------------------------------

    [Fact]
    public void Loans_and_clearance_dues_are_deductions()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.LoanOutstanding = 5_000;
            b.ClearanceDues = 2_000;
        }));

        Assert.Equal(5_000, AmountOf(draft, SettlementSource.LoanRecovery));
        Assert.Equal(2_000, AmountOf(draft, SettlementSource.ClearanceDues));
        Assert.All(
            draft.Lines.Where(l => l.Source is SettlementSource.LoanRecovery or SettlementSource.ClearanceDues),
            l => Assert.Equal(PayComponentKind.Deduction, l.Kind));
    }

    [Fact]
    public void No_outstanding_loan_is_stated_rather_than_left_silent()
    {
        // hr.loan has no writer until spec 0057; the settlement reads it and legitimately recovers
        // nothing. Saying so is what stops "no loan line" reading as "loans were not checked".
        var draft = SettlementCalculator.Compute(Standard());

        Assert.Contains(draft.Notes, n => n.Contains("No loan or advance is outstanding"));
    }

    [Fact]
    public void The_net_is_earnings_less_deductions()
    {
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.Gratuity = new GratuityInput(1, 12, 300_000, 30_000);
            b.EncashLeave = true;
            b.EncashableLeaveTypeName = "Earned leave";
            b.EncashableBalanceBp = 10 * 10_000;
            b.LoanOutstanding = 20_000;
            b.ClearanceDues = 3_000;
        }));

        // 31,000 salary + 10,000 encashment + 150,000 gratuity − 20,000 loan − 3,000 dues
        Assert.Equal(168_000, draft.NetPayableTaka);

        var earnings = draft.Lines.Where(l => l.Kind == PayComponentKind.Earning).Sum(l => l.AmountTaka);
        var deductions = draft.Lines.Where(l => l.Kind == PayComponentKind.Deduction).Sum(l => l.AmountTaka);
        Assert.Equal(draft.NetPayableTaka, earnings - deductions);
    }

    [Fact]
    public void Every_line_states_its_basis()
    {
        // §7.1: the statement is handed to a departing employee and defended line by line.
        var draft = SettlementCalculator.Compute(Standard(b =>
        {
            b.Gratuity = new GratuityInput(1, 12, 300_000, 30_000);
            b.EncashLeave = true;
            b.EncashableLeaveTypeName = "Earned leave";
            b.EncashableBalanceBp = 10 * 10_000;
            b.NoticeDays = 120;
            b.AdjustNoticePay = true;
            b.LoanOutstanding = 1_000;
            b.ClearanceDues = 500;
        }));

        Assert.All(draft.Lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Basis)));
        Assert.Equal(draft.Lines.Count, draft.Lines.Select(l => l.Ordinal).Distinct().Count());
    }

    [Fact]
    public void A_zero_day_count_divisor_falls_back_rather_than_dividing_by_zero()
    {
        var draft = SettlementCalculator.Compute(Standard(b => b.Denominator = 0));

        Assert.Equal(31_000, AmountOf(draft, SettlementSource.UnpaidSalary));
    }

    // ---- completed months ------------------------------------------------------------------------

    [Theory]
    // The gratuity boundary: a day short of the anniversary is not another year of service.
    [InlineData("2021-07-01", "2026-06-30", 59)]
    [InlineData("2021-07-01", "2026-07-01", 60)]
    [InlineData("2021-07-31", "2021-08-30", 0)]
    [InlineData("2021-07-31", "2021-08-31", 1)]
    // A February hire, where "the same day next month" does not exist.
    [InlineData("2024-02-29", "2025-02-28", 11)]
    [InlineData("2024-01-31", "2024-03-01", 1)]
    [InlineData("2026-08-31", "2026-08-01", 0)]
    public void Completed_months_counts_the_anniversary_day(string from, string to, int expected)
    {
        Assert.Equal(expected,
            SettlementCalculator.CompletedMonths(DateOnly.Parse(from), DateOnly.Parse(to)));
    }
}
