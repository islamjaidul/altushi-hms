using System.Globalization;

namespace Hms.Kernel.Time;

/// <summary>
/// Says what a period actually means, in words. §12.3 is explicit that the resolved range is always
/// stated — "never only a control state" — because a dropdown reading "Month" tells an operator
/// nothing about which month they are looking at, and a printed report with no period on it is
/// worthless the day after it is printed.
/// <para>
/// <b>Two date formats live in this product on purpose.</b> The period sentence uses
/// <c>"1 – 31 July 2026"</c> (§12.3's own example — natural prose, no leading zero). Data cells use
/// <c>Ui.DateLong</c>'s <c>"06 Aug 2026"</c> (§7 U13 — fixed width, so a column of dates aligns).
/// Both are unambiguous. Do not "fix" one into the other.
/// </para>
/// </summary>
public static class PeriodWords
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The full sentence, with the timezone: "1 – 31 July 2026 · Asia/Dhaka".</summary>
    public static string Describe(Period p)
    {
        var range = Range(p.Range);
        var label = p.Grain switch
        {
            PeriodGrain.LeaveYear => $"Leave year {p.YearLabel} · ",
            PeriodGrain.FinancialYear => $"Financial year {p.YearLabel} · ",
            _ => "",
        };
        return $"{label}{range} · {Dhaka.ZoneName}";
    }

    /// <summary>The range alone, without the timezone — for a column header or a filter chip.</summary>
    public static string Range(DateRange r)
    {
        if (r.From == r.To) return Day(r.From);
        if (r.From.Year == r.To.Year && r.From.Month == r.To.Month)
            return $"{r.From.Day} – {r.To.Day} {r.From.ToString("MMMM yyyy", Inv)}";
        if (r.From.Year == r.To.Year)
            return $"{r.From.Day} {r.From.ToString("MMMM", Inv)} – {r.To.Day} {r.To.ToString("MMMM yyyy", Inv)}";
        return $"{Day(r.From)} – {Day(r.To)}";
    }

    public static string Day(DateOnly d) => $"{d.Day} {d.ToString("MMMM yyyy", Inv)}";

    /// <summary>"vs 1 – 30 June 2026", or null when nothing is being compared.</summary>
    public static string? CompareWords(Period p) => p.CompareRange is { } c
        ? $"vs {Range(c)}{(p.Compare == PeriodCompare.SameLastYear ? " (same period last year)" : "")}"
        : null;

    /// <summary>
    /// The sentence a screen shows instead of data, or above it. Null when the period is fine.
    /// §12.3: a future period "returns an empty result with a sentence, never an error", and an
    /// over-long range "warns before running rather than timing out".
    /// </summary>
    public static string? Notice(Period p, DateOnly today, int maxDays) => p.Health switch
    {
        PeriodHealth.WhollyFuture =>
            $"{Range(p.Range)} has not happened yet — there is nothing to show. "
            + $"Choose a period on or before {Day(today)}.",

        PeriodHealth.PartlyFuture =>
            $"This period runs to {Day(p.Range.To)}; the figures cover only up to today, {Day(today)}.",

        PeriodHealth.TooLong =>
            $"{p.Range.Days:N0} days is a longer period than this report is meant to cover "
            + $"(the limit is {maxDays:N0} days). It may take a while and may not finish. "
            + "Run it anyway, or choose a shorter period.",

        _ => null,
    };
}
