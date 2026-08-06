using Hms.Hr.Data;
using Hms.Kernel.Money;

namespace Hms.Hr;

/// <summary>
/// What a gratuity computation needs, or null when the employer has configured no rule. Null is a
/// first-class answer here: ADR-0027 forbids inventing an end-of-service formula, so an unconfigured
/// employer gets <b>no gratuity line and a sentence saying why</b> — never a silent zero, which
/// reads on a statement as "you have earned nothing".
/// </summary>
public sealed record GratuityInput(
    long RuleId, int MinimumServiceMonths, int DaysPerYearBp, long BaseMonthlyTaka);

/// <summary>
/// Everything a final settlement is computed from, gathered by <see cref="SeparationService"/> and
/// handed here as plain values. Keeping the arithmetic separate from the loading is what lets the
/// whole of §7.1's settlement list be tested against fixtures in milliseconds — including the cases
/// that matter most, which are the ones where a policy is missing.
/// </summary>
public sealed record SettlementInput
{
    public required DateOnly JoinedOn { get; init; }
    public required DateOnly LastWorkingDay { get; init; }
    public required string Kind { get; init; }               // SeparationKind
    public required DateOnly NoticeOn { get; init; }
    public int NoticeDays { get; init; }
    public bool AdjustNoticePay { get; init; }

    /// <summary>Gross monthly earnings from the structure in force on the last working day.</summary>
    public long MonthlyGrossTaka { get; init; }

    /// <summary>The employer's day-count divisor — the same one payroll prorates by.</summary>
    public int DayCountDenominator { get; init; }

    /// <summary>Last day already covered by a locked or posted run; null when nothing has been paid.</summary>
    public DateOnly? PaidThrough { get; init; }

    public bool EncashLeave { get; init; }
    public string? EncashableLeaveTypeName { get; init; }
    /// <summary>Available balance of the encashable type, in basis points of a day.</summary>
    public int EncashableBalanceBp { get; init; }

    public GratuityInput? Gratuity { get; init; }
    public long LoanOutstandingTaka { get; init; }
    public long ClearanceDuesTaka { get; init; }
}

public sealed record SettlementDraftLine(
    int Ordinal, string Label, string Kind, long AmountTaka, string Source, string Basis);

/// <summary>
/// The computed statement: the lines, the net, and the sentences explaining what could <em>not</em>
/// be computed. The notes are not decoration — an employer whose gratuity rule is unconfigured must
/// see that on the statement, before it is approved, not discover it when the employee asks.
/// </summary>
public sealed record SettlementDraft(
    IReadOnlyList<SettlementDraftLine> Lines, long NetPayableTaka, IReadOnlyList<string> Notes);

/// <summary>
/// Module PRD §7.1's final settlement: "unpaid salary to the last working day, leave encashment,
/// gratuity per the employer's rule, bonus proration, loan and advance recovery, notice-pay
/// adjustment either way, asset recovery, PF settlement — netted to one payable".
/// <para>
/// Every line carries its <c>Basis</c>: the "why this number" string
/// <see cref="PayrollComponentLine"/> already carries, and the reason this statement can be handed
/// to a departing employee and defended line by line.
/// </para>
/// <para>
/// Pure and static. No clock, no database, no DI.
/// </para>
/// </summary>
public static class SettlementCalculator
{
    public static SettlementDraft Compute(SettlementInput input)
    {
        var lines = new List<SettlementDraftLine>();
        var notes = new List<string>();
        var ordinal = 0;

        var denominator = input.DayCountDenominator > 0 ? input.DayCountDenominator : 30;
        var dayRate = input.MonthlyGrossTaka / denominator;

        void Earn(string label, long amount, string source, string basis)
        {
            if (amount <= 0) return;
            lines.Add(new SettlementDraftLine(
                ++ordinal, label, PayComponentKind.Earning, amount, source, basis));
        }

        void Deduct(string label, long amount, string source, string basis)
        {
            if (amount <= 0) return;
            lines.Add(new SettlementDraftLine(
                ++ordinal, label, PayComponentKind.Deduction, amount, source, basis));
        }

        // ---- 1. salary earned and not yet paid -----------------------------------------------
        if (input.MonthlyGrossTaka <= 0)
        {
            notes.Add("No pay structure is in force on the last working day, so unpaid salary, "
                      + "leave encashment and gratuity could not be computed.");
        }
        else
        {
            var from = input.PaidThrough is { } paid && paid.AddDays(1) > input.JoinedOn
                ? paid.AddDays(1)
                : input.JoinedOn;
            var unpaidDays = input.LastWorkingDay.DayNumber - from.DayNumber + 1;

            if (unpaidDays > 0)
            {
                Earn("Unpaid salary", dayRate * unpaidDays, SettlementSource.UnpaidSalary,
                    $"{unpaidDays} day{(unpaidDays == 1 ? "" : "s")} "
                    + $"({from:dd MMM yyyy} – {input.LastWorkingDay:dd MMM yyyy}) at "
                    + $"{dayRate:N0}/day — gross {input.MonthlyGrossTaka:N0} ÷ {denominator}");
            }
            else
            {
                notes.Add($"Salary is already paid to {input.PaidThrough:dd MMM yyyy}, "
                          + "on or after the last working day, so nothing is outstanding.");
            }

            // ---- 2. leave encashment ---------------------------------------------------------
            if (!input.EncashLeave)
            {
                notes.Add("The employer's policy does not encash leave at settlement, "
                          + "so no encashment was computed.");
            }
            else if (input.EncashableLeaveTypeName is null)
            {
                notes.Add("Leave encashment is switched on but no encashable leave type is "
                          + "configured, so no encashment was computed.");
            }
            else if (input.EncashableBalanceBp <= 0)
            {
                notes.Add($"No {input.EncashableLeaveTypeName} balance remains to encash.");
            }
            else
            {
                var days = input.EncashableBalanceBp / (decimal)Taka.Bp;
                Earn("Leave encashment", Taka.ApplyBp(dayRate, input.EncashableBalanceBp),
                    SettlementSource.LeaveEncashment,
                    $"{days:0.##} day{(days == 1 ? "" : "s")} of {input.EncashableLeaveTypeName} "
                    + $"at {dayRate:N0}/day");
            }

            // ---- 3. gratuity -----------------------------------------------------------------
            AddGratuity(input, denominator, notes, Earn);
        }

        // ---- 4. notice-pay adjustment, either way ---------------------------------------------
        AddNoticeAdjustment(input, dayRate, notes, Earn, Deduct);

        // ---- 5. recoveries ---------------------------------------------------------------------
        if (input.LoanOutstandingTaka > 0)
        {
            Deduct("Loan and advance recovery", input.LoanOutstandingTaka,
                SettlementSource.LoanRecovery, "Outstanding balance on the employee's loan ledger");
        }
        else
        {
            notes.Add("No loan or advance is outstanding.");
        }

        if (input.ClearanceDuesTaka > 0)
        {
            Deduct("Clearance dues", input.ClearanceDuesTaka, SettlementSource.ClearanceDues,
                "Recoverable amounts recorded by the clearing departments");
        }

        var net = lines.Where(l => l.Kind == PayComponentKind.Earning).Sum(l => l.AmountTaka)
                  - lines.Where(l => l.Kind == PayComponentKind.Deduction).Sum(l => l.AmountTaka);

        return new SettlementDraft(lines, net, notes);
    }

    private static void AddGratuity(
        SettlementInput input, int denominator, List<string> notes,
        Action<string, long, string, string> earn)
    {
        if (input.Gratuity is not { } rule)
        {
            // ADR-0027 / D2: an unconfigured employer gets a sentence, never an invented formula
            // and never a zero that reads as "you earned none".
            notes.Add("No gratuity rule is configured for the last working day, "
                      + "so no gratuity was computed. Set one on Policies if it is due.");
            return;
        }

        var months = CompletedMonths(input.JoinedOn, input.LastWorkingDay);
        if (months < rule.MinimumServiceMonths)
        {
            notes.Add($"Service is {months} month{(months == 1 ? "" : "s")}; the employer's gratuity "
                      + $"rule requires {rule.MinimumServiceMonths}. No gratuity was computed.");
            return;
        }

        var years = months / 12;
        if (years == 0)
        {
            notes.Add("Service is under one completed year, so no gratuity was computed.");
            return;
        }

        var baseMonthly = rule.BaseMonthlyTaka > 0 ? rule.BaseMonthlyTaka : input.MonthlyGrossTaka;
        var gratuityDayRate = baseMonthly / denominator;
        var amount = Taka.ApplyBp(gratuityDayRate * years, rule.DaysPerYearBp);
        var days = rule.DaysPerYearBp / (decimal)Taka.Bp;

        earn("Gratuity", amount, SettlementSource.Gratuity,
            $"{years} completed year{(years == 1 ? "" : "s")} × {days:0.##} day{(days == 1 ? "" : "s")} "
            + $"of pay at {gratuityDayRate:N0}/day (rule #{rule.RuleId})");
    }

    private static void AddNoticeAdjustment(
        SettlementInput input, long dayRate, List<string> notes,
        Action<string, long, string, string> earn, Action<string, long, string, string> deduct)
    {
        if (!input.AdjustNoticePay || input.NoticeDays <= 0)
        {
            if (input.NoticeDays <= 0)
                notes.Add("No notice period is configured, so no notice adjustment was computed.");
            return;
        }

        var served = input.LastWorkingDay.DayNumber - input.NoticeOn.DayNumber;
        var shortfall = input.NoticeDays - served;
        if (shortfall <= 0)
        {
            notes.Add($"{served} days of notice were served against {input.NoticeDays} required — "
                      + "no adjustment.");
            return;
        }

        var amount = dayRate * shortfall;
        var basis = $"{shortfall} day{(shortfall == 1 ? "" : "s")} of the {input.NoticeDays}-day "
                    + $"notice unserved, at {dayRate:N0}/day";

        // §7.1's "either way": the side that cut the notice short is the side that pays for it.
        if (SeparationKind.EmployerInitiated(input.Kind))
            earn("Pay in lieu of notice", amount, SettlementSource.NoticePay, basis);
        else
            deduct("Notice shortfall recovery", amount, SettlementSource.NoticePay, basis);
    }

    /// <summary>
    /// Whole months of service. Counts the anniversary day, so someone who joined on 1 July and left
    /// on 30 June has served 11 months and 30 days — eleven completed months, not twelve. Gratuity
    /// turns on this boundary, so it is arithmetic rather than an approximation of a year.
    /// </summary>
    public static int CompletedMonths(DateOnly from, DateOnly to)
    {
        if (to < from) return 0;
        var months = (to.Year - from.Year) * 12 + to.Month - from.Month;
        if (to.Day < from.Day) months--;
        return Math.Max(0, months);
    }
}
