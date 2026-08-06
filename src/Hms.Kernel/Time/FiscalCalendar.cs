namespace Hms.Kernel.Time;

/// <summary>
/// Fiscal-year labeling for number series (ADR-0004). Start month is configuration
/// (P1 default: July). A July–June year is labeled "2026-27"; a calendar year "2026".
/// </summary>
public sealed class FiscalCalendar(int startMonth)
{
    public int StartMonth { get; } = startMonth is >= 1 and <= 12
        ? startMonth
        : throw new ArgumentOutOfRangeException(nameof(startMonth), startMonth, "Month must be 1–12.");

    public string FiscalYearOf(DateOnly date)
    {
        if (StartMonth == 1) return date.Year.ToString();
        var startYear = date.Month >= StartMonth ? date.Year : date.Year - 1;
        var endYy = (startYear + 1) % 100;
        return $"{startYear}-{endYy:D2}";
    }

    /// <summary>
    /// The dates of the fiscal year containing <paramref name="date"/>. Added for the reporting
    /// period standard (spec 0055), which needs a range where numbering only ever needed a label.
    /// <para>
    /// <see cref="FiscalYearOf"/> is deliberately untouched: ADR-0004 number series are scoped by
    /// its string, so changing it would renumber history.
    /// </para>
    /// </summary>
    public DateRange RangeOf(DateOnly date)
        => RangeStartingIn(date.Month >= StartMonth ? date.Year : date.Year - 1);

    /// <summary>The fiscal year that begins in <paramref name="startYear"/>.</summary>
    public DateRange RangeStartingIn(int startYear)
    {
        var from = new DateOnly(startYear, StartMonth, 1);
        return DateRange.Of(from, from.AddYears(1).AddDays(-1));
    }
}
