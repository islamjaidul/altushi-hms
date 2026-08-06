using System.Globalization;
using Hms.Kernel.Time;
using Microsoft.AspNetCore.Http;

namespace Hms.Shell.Reporting;

/// <summary>
/// Everything <c>_PeriodBar.cshtml</c> renders. Built by the page <i>after</i> it has resolved the
/// period, so there is exactly one resolution per request — the data query and the control can never
/// disagree about which month is on screen.
/// </summary>
public sealed record PeriodBar
{
    public required Period Period { get; init; }

    public required string Words { get; init; }

    public string? Notice { get; init; }

    public string? CompareWords { get; init; }

    public required string PrevHref { get; init; }

    public required string NextHref { get; init; }

    /// <summary>Where "Run it anyway" points when a period is over the length limit.</summary>
    public required string RunAnywayHref { get; init; }

    public required string UnitWord { get; init; }

    public string CustomFrom { get; init; } = "";

    public string CustomTo { get; init; } = "";

    public required IReadOnlyList<GrainOption> Grains { get; init; }

    public required IReadOnlyList<PresetOption> Presets { get; init; }

    /// <summary>Non-period query parameters, carried through the GET form as hidden inputs so that
    /// changing the period never silently drops the filters.</summary>
    public required IReadOnlyList<KeyValuePair<string, string>> CarryForward { get; init; }

    public sealed record GrainOption(string Key, string Label, bool Selected);

    public sealed record PresetOption(string Key, string Label, string Href, bool Selected);

    public static PeriodBar For(Period period, HttpRequest request, PeriodCalendar calendar,
        DateOnly today)
    {
        var carry = request.Query
            .Where(kv => !FilterSet.PeriodKeys.Contains(kv.Key)
                         && !string.Equals(kv.Key, "handler", StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value,
                (kv, v) => new KeyValuePair<string, string>(kv.Key, v ?? ""))
            .ToList();

        return new PeriodBar
        {
            Period = period,
            Words = PeriodWords.Describe(period),
            Notice = PeriodWords.Notice(period, today, calendar.MaxRangeDays),
            CompareWords = PeriodWords.CompareWords(period),
            PrevHref = PeriodBinding.UrlFor(request, PeriodResolver.Step(period, -1, calendar, today)),
            NextHref = PeriodBinding.UrlFor(request, PeriodResolver.Step(period, +1, calendar, today)),
            RunAnywayHref = PeriodBinding.UrlFor(request, period, [new("run", "1")]),
            UnitWord = PeriodResolver.UnitWord(period.Grain),
            CustomFrom = period.Grain == PeriodGrain.Custom ? Format(period.Range.From) : "",
            CustomTo = period.Grain == PeriodGrain.Custom ? Format(period.Range.To) : "",
            Grains = GrainOptions(period.Grain),
            Presets = PresetOptions(period, request, calendar, today),
            CarryForward = carry,
        };
    }

    private static string Format(DateOnly d) => d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);

    private static IReadOnlyList<GrainOption> GrainOptions(PeriodGrain selected)
    {
        PeriodGrain[] order =
        [
            PeriodGrain.Day, PeriodGrain.Week, PeriodGrain.Month, PeriodGrain.Quarter,
            PeriodGrain.Year, PeriodGrain.LeaveYear, PeriodGrain.FinancialYear, PeriodGrain.Custom,
        ];
        return [.. order.Select(g => new GrainOption(
            PeriodResolver.GrainKey(g), Label(g), g == selected))];
    }

    private static string Label(PeriodGrain g) => g switch
    {
        PeriodGrain.Day => "Day",
        PeriodGrain.Week => "Week",
        PeriodGrain.Month => "Month",
        PeriodGrain.Quarter => "Quarter",
        PeriodGrain.Year => "Year",
        PeriodGrain.LeaveYear => "Leave year",
        PeriodGrain.FinancialYear => "Financial year",
        _ => "Custom range",
    };

    private static IReadOnlyList<PresetOption> PresetOptions(Period current, HttpRequest request,
        PeriodCalendar calendar, DateOnly today)
    {
        var options = new List<PresetOption>();
        foreach (var (key, label) in PeriodResolver.Presets)
        {
            var resolved = PeriodResolver.FromPreset(key, today, calendar);

            // Highlight by what the period *is*, not by how it was reached — a canonical URL has
            // dropped the preset name by design, and the chip must still show where you are.
            var selected = resolved.Range == current.Range && resolved.Grain == current.Grain;

            options.Add(new PresetOption(key, Decorate(key, label, resolved, calendar),
                PeriodBinding.PresetUrl(request, key), selected));
        }
        return options;
    }

    /// <summary>
    /// Spells out the year presets, because §12.3 insists the leave and financial years come from
    /// the employer's configuration and are "never assumed". When nothing is configured the label
    /// says so, so an operator can see they are looking at our default rather than their policy.
    /// </summary>
    private static string Decorate(string key, string label, Period resolved, PeriodCalendar calendar)
    {
        if (key is not ("this-leave-year" or "this-financial-year")) return label;

        var span = $"{resolved.Range.From.ToString("MMM yyyy", CultureInfo.InvariantCulture)}"
                   + $" – {resolved.Range.To.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
        return calendar.IsDefault ? $"{label} ({span}, default)" : $"{label} ({span})";
    }
}
