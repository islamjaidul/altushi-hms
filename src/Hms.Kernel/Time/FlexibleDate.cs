using System.Globalization;

namespace Hms.Kernel.Time;

/// <summary>
/// The product-wide forgiving date contract (ADR-0020, §7 U13): every hand-typed date field
/// accepts the formats an operator would naturally write — day-first per Bangladeshi
/// convention, ISO, and the <c>dd MMM yyyy</c> the app itself displays — and the UI echoes
/// back <c>dd MMM yyyy</c>. Native browser date inputs are banned (they render the browser's
/// locale, not the operator's convention).
/// </summary>
public static class FlexibleDate
{
    // Day-first formats first: "12/03/1980" is 12 March, never December 3 (U13).
    private static readonly string[] Formats =
    [
        "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "dd-MM-yyyy", "yyyy-MM-dd",
        "d/M/yy", "dd/MM/yy",
        "d MMM yyyy", "dd MMM yyyy", "d MMMM yyyy", "dd MMMM yyyy",
    ];

    public static bool TryParse(string? input, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(input)) return false;
        return DateOnly.TryParseExact(input.Trim(), Formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);
    }

    public static DateOnly? Parse(string? input) => TryParse(input, out var d) ? d : null;
}
