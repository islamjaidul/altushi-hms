namespace Hms.Hr.Data;

// ADR-0027: every rate, threshold, slab and multiplier below is a row the customer enters, carrying
// an effective date. There is no Bangladeshi statutory constant anywhere in this module's C#, and
// the product ships with these tables EMPTY rather than with fabricated numbers — Labour Act
// entitlements and NBR slabs appear in no document this project can cite, and rule 3 forbids
// asserting a regulation we have not verified. P26 owns getting them sourced.
//
// The engine knows shapes: a progressive slab set, a percentage of a base, a multiplier on minutes.
// It does not know what any Bangladeshi number is.

public static class DayCountConvention
{
    /// <summary>Prorate over the actual number of days in the payroll month.</summary>
    public const string CalendarDays = "calendar_days";
    /// <summary>Prorate over a fixed 30 regardless of month length.</summary>
    public const string Fixed30 = "fixed_30";
    /// <summary>Prorate over the month's working days only.</summary>
    public const string WorkingDays = "working_days";
}

/// <summary>
/// The per-employer payroll rules that are not per-component: proration, rounding floor, whether a
/// negative net is permitted. Effective-dated like everything else.
/// </summary>
public class PayrollPolicy
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>Mid-month joiners and leavers are prorated by this. Employer choice, not a constant.</summary>
    public required string DayCountConvention { get; set; }
    /// <summary>Net pay may not fall below this; the remainder carries on the loan ledger.</summary>
    public long MinimumNetPayTaka { get; set; }
    /// <summary>Which earning component absorbs integer rounding residue so components sum to the total.</summary>
    public long? RoundingResidueComponentId { get; set; }
    /// <summary>Month the leave year starts (1–12). Employers differ; the fiscal default is July here.</summary>
    public int LeaveYearStartMonth { get; set; } = 7;

    /// <summary>
    /// Spec 0057 (G47): how far a person's net pay may move from last month before the change has
    /// to be explained, in basis points. 2000 = 20%. Zero means every change needs a reason, which
    /// is a defensible choice for a small employer and a punishing one for a large one — so it is
    /// theirs to set, not ours.
    /// </summary>
    public int VarianceTolerationBp { get; set; } = 2_000;
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>
/// One band of a progressive tax table. The engine applies bands in order; it does not know what the
/// bands are. Empty table = the income-tax component computes zero and says so.
/// </summary>
public class TaxSlab
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>Optional category label the employer uses (e.g. a different table per class of employee).</summary>
    public string? Category { get; set; }
    public int Ordinal { get; set; }
    public long UpToTaka { get; set; }          // long.MaxValue-ish for the top band
    public long RateBp { get; set; }            // basis points
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>Provident-fund contribution rule. Rates are the employer's, including zero.</summary>
public class PfPolicy
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public long EmployeeShareBp { get; set; }
    public long EmployerShareBp { get; set; }
    /// <summary>Months of service before membership starts.</summary>
    public int EligibilityMonths { get; set; }
    public long? MonthlyCapTaka { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>Gratuity / end-of-service rule, expressed as days of pay per completed year.</summary>
public class GratuityRule
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int MinimumServiceMonths { get; set; }
    /// <summary>Days of pay per completed year of service, in basis points of a day.</summary>
    public int DaysPerYearBp { get; set; }
    /// <summary>Which component the day-rate is computed from (usually basic).</summary>
    public long? BasedOnComponentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>Overtime multiplier and eligibility. §5A-16's OT bank consumes the same rule.</summary>
public class OvertimeRule
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public long? GradeId { get; set; }
    /// <summary>Minutes past the shift before OT starts to count.</summary>
    public int ThresholdMinutes { get; set; }
    /// <summary>Multiplier on the ordinary minute rate, in basis points (10000 = 1.00x).</summary>
    public long MultiplierBp { get; set; }
    public int MaxMinutesPerMonth { get; set; }
    /// <summary>Which component the hourly rate derives from.</summary>
    public long? BasedOnComponentId { get; set; }
    /// <summary>Bank the minutes as comp-off instead of paying them (§5A-16b).</summary>
    public bool BankInsteadOfPay { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>Late-arrival tolerance and what exceeding it costs.</summary>
public class GraceTimeRule
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public int GraceMinutes { get; set; }
    /// <summary>Late arrivals allowed per month before a deduction applies.</summary>
    public int FreeLateCountPerMonth { get; set; }
    /// <summary>Days of pay deducted per excess late, in basis points of a day.</summary>
    public int DeductionPerLateBp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>What working on a holiday or weekly off pays.</summary>
public class HolidayPayPolicy
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public long MultiplierBp { get; set; }
    public bool GrantsCompOff { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>What an unpaid absence costs. Separate from grace-time so employers can tune each.</summary>
public class DeductionRule
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>Days of pay deducted per absent day, in basis points (10000 = one full day).</summary>
    public int PerAbsentDayBp { get; set; } = 10000;
    public int PerLeaveWithoutPayDayBp { get; set; } = 10000;
    /// <summary>Which component the day rate derives from; null means gross earnings.</summary>
    public long? BasedOnComponentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}
