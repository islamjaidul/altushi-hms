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
}
