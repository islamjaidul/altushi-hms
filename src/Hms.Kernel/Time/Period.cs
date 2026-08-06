namespace Hms.Kernel.Time;

/// <summary>
/// How a period is measured. Grains, not presets — the difference matters for the arrows.
/// <para>
/// If "this leave year" were a preset that flattened to a custom range, stepping back would move by
/// the range's length (365 days) rather than to the previous leave year, which is wrong the moment
/// an employer's leave year does not start in January. Making them grains keeps <c>Step</c> a single
/// switch and keeps every arrow meaningful.
/// </para>
/// </summary>
public enum PeriodGrain
{
    Day,
    Week,
    Month,
    Quarter,
    Year,

    /// <summary>The employer's leave year (module PRD §12.3). Start month is configuration.</summary>
    LeaveYear,

    /// <summary>The employer's financial year. Start month is configuration (ADR-0004 P1: July).</summary>
    FinancialYear,

    Custom,
}

/// <summary>What a period is being compared against, where a surface offers comparison (§12.3).</summary>
public enum PeriodCompare
{
    None,
    PreviousPeriod,
    SameLastYear,
}

/// <summary>
/// Whether a resolved period can safely be queried. Computed once during resolution so that no
/// caller has to remember to check — the report base reads this and decides whether to run at all.
/// </summary>
public enum PeriodHealth
{
    Ok,

    /// <summary>Ends after today. The query runs; the screen says figures stop at today.</summary>
    PartlyFuture,

    /// <summary>Starts after today. §12.3: an empty result with a sentence, never an error.</summary>
    WhollyFuture,

    /// <summary>Longer than the configured maximum. §12.3: warn before running rather than time out.</summary>
    TooLong,
}

/// <summary>
/// The employer configuration a period needs in order to resolve. A plain data record supplied by
/// the caller, so that <see cref="PeriodResolver"/> stays a pure function and the Kernel never
/// learns what a payroll policy is.
/// </summary>
/// <param name="LeaveYearStartMonth">From <c>hr.payroll_policy</c>. Never assumed — see the source.</param>
/// <param name="FinancialYearStartMonth">From numbering configuration (ADR-0004 P1).</param>
/// <param name="WeekStartsOn">
/// Saturday in Bangladesh — the weekend is Friday/Saturday. Deliberately <b>not</b> derived from
/// <c>CultureInfo</c>, which under the invariant culture returns Sunday and would be quietly wrong
/// on every weekly report. Becomes employer configuration when the Shifts &amp; weekly-offs setup
/// screen lands.
/// </param>
/// <param name="MaxRangeDays">
/// Beyond this a period warns before running (§12.3, N-HR2). 400 is a year plus slack, chosen
/// against the deployment ceiling: at 1,000 employees a year of attendance is ~365,000 rows.
/// </param>
public sealed record PeriodCalendar(
    int LeaveYearStartMonth,
    int FinancialYearStartMonth,
    DayOfWeek WeekStartsOn,
    int MaxRangeDays)
{
    public static readonly PeriodCalendar Default = new(7, 7, DayOfWeek.Saturday, 400);

    /// <summary>True when the employer has configured nothing and these are our defaults.</summary>
    public bool IsDefault { get; init; }
}

/// <summary>
/// A resolved period: what the operator chose, what dates it means, and whether it can be queried.
/// <para>
/// Immutable. <c>Grain</c> + <c>Anchor</c> + <c>Units</c> determine <c>Range</c> for every grain
/// except <see cref="PeriodGrain.Custom"/>, which carries its range directly. The redundancy is
/// deliberate: the range is what queries need, and the anchor is what the arrows need.
/// </para>
/// </summary>
public sealed record Period
{
    public required PeriodGrain Grain { get; init; }

    /// <summary>First day of the <b>last</b> unit in the window.</summary>
    public required DateOnly Anchor { get; init; }

    /// <summary>How many units the window spans. "Last 12 months" is Month with Units = 12.</summary>
    public int Units { get; init; } = 1;

    public required DateRange Range { get; init; }

    /// <summary>
    /// The preset this came from, if any — "this-month", "last-12-months". Kept because the cookie
    /// stores the preset rather than the dates, so an operator who left on "This month" and returns
    /// in September gets September rather than July.
    /// </summary>
    public string Preset { get; init; } = "";

    public PeriodCompare Compare { get; init; } = PeriodCompare.None;

    public DateRange? CompareRange { get; init; }

    public required PeriodHealth Health { get; init; }

    /// <summary>"2026-27" for a leave or financial year, otherwise empty.</summary>
    public string YearLabel { get; init; } = "";

    /// <summary>True when the employer has configured no leave year and the default is in use.</summary>
    public bool CalendarIsDefault { get; init; }

    /// <summary>Whether a query should run at all. False only when the period is wholly in the future.</summary>
    public bool Queryable => Health != PeriodHealth.WhollyFuture;
}

/// <summary>
/// The raw, unresolved period as it arrives from a query string. Strings, because that is what a URL
/// carries and because a malformed value must degrade to a default rather than throw.
/// </summary>
public readonly record struct PeriodQuery(
    string? Grain = null,
    string? Anchor = null,
    string? From = null,
    string? To = null,
    string? Preset = null,
    string? Compare = null,
    int Units = 1,
    int Step = 0);
