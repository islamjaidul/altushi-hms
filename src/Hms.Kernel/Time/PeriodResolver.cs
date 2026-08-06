using System.Globalization;

namespace Hms.Kernel.Time;

/// <summary>
/// Turns what a URL carries into a resolved <see cref="Period"/>, and back again.
/// <para>
/// Every method is a pure function of its arguments — no clock, no DI, no I/O. <c>today</c> and the
/// <see cref="PeriodCalendar"/> arrive as parameters precisely so that the whole period surface can
/// be tested in milliseconds without a database or a host, which is where nearly all of this
/// module's correctness risk lives.
/// </para>
/// <para>
/// <b>Round-tripping is load-bearing.</b> <c>Resolve(ToQuery(p)) == p</c> must hold for every grain,
/// because the URL <i>is</i> the sharing mechanism (§12.3: "it is in the URL, so a period is a link
/// an HR officer can send to the MD"). A period that does not survive serialisation is a link that
/// means something different for the recipient.
/// </para>
/// </summary>
public static class PeriodResolver
{
    /// <summary>The §12.3 preset list, in the order the PRD gives them.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> Presets =
    [
        ("today", "Today"),
        ("yesterday", "Yesterday"),
        ("this-week", "This week"),
        ("this-month", "This month"),
        ("last-month", "Last month"),
        ("this-quarter", "This quarter"),
        ("this-year", "This year"),
        ("last-12-months", "Last 12 months"),
        ("this-leave-year", "This leave year"),
        ("this-financial-year", "This financial year"),
    ];

    public static Period Resolve(PeriodQuery q, DateOnly today, PeriodCalendar cal,
        PeriodGrain fallback = PeriodGrain.Month)
    {
        // A preset always wins over a grain+anchor, because it is only ever sent by a preset link.
        if (!string.IsNullOrWhiteSpace(q.Preset) && TryPreset(q.Preset, today, cal, out var preset))
            return Step(WithCompare(preset, q.Compare, cal), q.Step, cal, today);

        var grain = ParseGrain(q.Grain) ?? (HasCustomDates(q) ? PeriodGrain.Custom : fallback);

        if (grain == PeriodGrain.Custom)
        {
            var from = FlexibleDate.Parse(q.From);
            var to = FlexibleDate.Parse(q.To);
            // A half-filled custom range is not an error: the operator has typed one end so far.
            var range = (from, to) switch
            {
                ({ } f, { } t) => DateRange.Of(f, t),
                ({ } f, null) => DateRange.Of(f, today),
                (null, { } t) => DateRange.Of(t, t),
                _ => DateRange.OneDay(today),
            };
            var custom = Build(PeriodGrain.Custom, range.From, 1, range, "", cal, today);
            return Step(WithCompare(custom, q.Compare, cal), q.Step, cal, today);
        }

        var anchor = ParseAnchor(q.Anchor, grain, cal) ?? today;
        var units = q.Units < 1 ? 1 : q.Units;
        var resolved = Of(grain, anchor, units, cal, today);
        return Step(WithCompare(resolved, q.Compare, cal), q.Step, cal, today);
    }

    /// <summary>Resolves a period from a grain and any day inside it.</summary>
    public static Period Of(PeriodGrain grain, DateOnly anyDayInIt, int units, PeriodCalendar cal,
        DateOnly today)
    {
        var anchor = NormaliseAnchor(grain, anyDayInIt, cal);
        return Build(grain, anchor, units, RangeOf(grain, anchor, units), "", cal, today);
    }

    public static Period FromPreset(string preset, DateOnly today, PeriodCalendar cal)
        => TryPreset(preset, today, cal, out var p) ? p : Of(PeriodGrain.Month, today, 1, cal, today);

    /// <summary>
    /// Moves the window <paramref name="units"/> whole units forward (or back). This is the arrow,
    /// and it is why grains exist: stepping a leave year moves to the previous leave year, not back
    /// 365 days.
    /// </summary>
    public static Period Step(Period p, int units, PeriodCalendar cal, DateOnly today)
    {
        if (units == 0) return p;

        if (p.Grain == PeriodGrain.Custom)
        {
            var shift = p.Range.Days * units;
            var moved = DateRange.Of(p.Range.From.AddDays(shift), p.Range.To.AddDays(shift));
            return WithCompare(
                Build(PeriodGrain.Custom, moved.From, 1, moved, "", cal, today), p.Compare, cal);
        }

        var anchor = ShiftAnchor(p.Grain, p.Anchor, units);
        // Stepping leaves the preset behind — the operator is no longer on "this month".
        return WithCompare(
            Build(p.Grain, anchor, p.Units, RangeOf(p.Grain, anchor, p.Units), "", cal, today),
            p.Compare, cal);
    }

    /// <summary>The canonical query string form. Short, stable, and it round-trips.</summary>
    public static PeriodQuery ToQuery(Period p) => new(
        Grain: GrainKey(p.Grain),
        Anchor: p.Grain == PeriodGrain.Custom ? null : FormatAnchor(p.Grain, p.Anchor),
        From: p.Grain == PeriodGrain.Custom ? p.Range.From.ToString("dd MMM yyyy") : null,
        To: p.Grain == PeriodGrain.Custom ? p.Range.To.ToString("dd MMM yyyy") : null,
        Preset: null,
        Compare: CompareKey(p.Compare),
        Units: p.Units);

    // ---- grain vocabulary ---------------------------------------------------------------------

    public static string GrainKey(PeriodGrain g) => g switch
    {
        PeriodGrain.Day => "day",
        PeriodGrain.Week => "week",
        PeriodGrain.Month => "month",
        PeriodGrain.Quarter => "quarter",
        PeriodGrain.Year => "year",
        PeriodGrain.LeaveYear => "leaveyear",
        PeriodGrain.FinancialYear => "finyear",
        _ => "custom",
    };

    public static PeriodGrain? ParseGrain(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "day" => PeriodGrain.Day,
        "week" => PeriodGrain.Week,
        "month" => PeriodGrain.Month,
        "quarter" => PeriodGrain.Quarter,
        "year" => PeriodGrain.Year,
        "leaveyear" => PeriodGrain.LeaveYear,
        "finyear" => PeriodGrain.FinancialYear,
        "custom" => PeriodGrain.Custom,
        _ => null,
    };

    /// <summary>The word an arrow's tooltip uses: "Previous month".</summary>
    public static string UnitWord(PeriodGrain g) => g switch
    {
        PeriodGrain.Day => "day",
        PeriodGrain.Week => "week",
        PeriodGrain.Month => "month",
        PeriodGrain.Quarter => "quarter",
        PeriodGrain.Year => "year",
        PeriodGrain.LeaveYear => "leave year",
        PeriodGrain.FinancialYear => "financial year",
        _ => "period",
    };

    public static string? CompareKey(PeriodCompare c) => c switch
    {
        PeriodCompare.PreviousPeriod => "prev",
        PeriodCompare.SameLastYear => "yoy",
        _ => null,
    };

    public static PeriodCompare ParseCompare(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "prev" => PeriodCompare.PreviousPeriod,
        "yoy" => PeriodCompare.SameLastYear,
        _ => PeriodCompare.None,
    };

    // ---- internals ----------------------------------------------------------------------------

    private static bool HasCustomDates(PeriodQuery q)
        => !string.IsNullOrWhiteSpace(q.From) || !string.IsNullOrWhiteSpace(q.To);

    private static bool TryPreset(string? key, DateOnly today, PeriodCalendar cal, out Period period)
    {
        period = null!;
        var p = key?.Trim().ToLowerInvariant() switch
        {
            "today" => Of(PeriodGrain.Day, today, 1, cal, today),
            "yesterday" => Of(PeriodGrain.Day, today.AddDays(-1), 1, cal, today),
            "this-week" => Of(PeriodGrain.Week, today, 1, cal, today),
            "this-month" => Of(PeriodGrain.Month, today, 1, cal, today),
            "last-month" => Of(PeriodGrain.Month, today.AddMonths(-1), 1, cal, today),
            "this-quarter" => Of(PeriodGrain.Quarter, today, 1, cal, today),
            "this-year" => Of(PeriodGrain.Year, today, 1, cal, today),
            "last-12-months" => Of(PeriodGrain.Month, today, 12, cal, today),
            "this-leave-year" => Of(PeriodGrain.LeaveYear, today, 1, cal, today),
            "this-financial-year" => Of(PeriodGrain.FinancialYear, today, 1, cal, today),
            _ => null,
        };
        if (p is null) return false;
        period = p with { Preset = key!.Trim().ToLowerInvariant() };
        return true;
    }

    private static Period Build(PeriodGrain grain, DateOnly anchor, int units, DateRange range,
        string preset, PeriodCalendar cal, DateOnly today) => new()
        {
            Grain = grain,
            Anchor = anchor,
            Units = units < 1 ? 1 : units,
            Range = range,
            Preset = preset,
            Health = HealthOf(range, today, cal.MaxRangeDays),
            YearLabel = YearLabelOf(grain, anchor, cal),
            CalendarIsDefault = cal.IsDefault,
        };

    private static Period WithCompare(Period p, string? compareKey, PeriodCalendar cal)
        => WithCompare(p, ParseCompare(compareKey), cal);

    private static Period WithCompare(Period p, PeriodCompare compare, PeriodCalendar cal)
    {
        if (compare == PeriodCompare.None) return p with { Compare = compare, CompareRange = null };

        DateRange? against = compare switch
        {
            PeriodCompare.PreviousPeriod when p.Grain == PeriodGrain.Custom
                => DateRange.Of(p.Range.From.AddDays(-p.Range.Days), p.Range.To.AddDays(-p.Range.Days)),
            PeriodCompare.PreviousPeriod
                => RangeOf(p.Grain, ShiftAnchor(p.Grain, p.Anchor, -1), p.Units),
            PeriodCompare.SameLastYear when p.Grain == PeriodGrain.Custom
                => DateRange.Of(p.Range.From.AddYears(-1), p.Range.To.AddYears(-1)),
            PeriodCompare.SameLastYear
                => RangeOf(p.Grain, NormaliseAnchor(p.Grain, p.Anchor.AddYears(-1), cal), p.Units),
            _ => null,
        };
        return p with { Compare = compare, CompareRange = against };
    }

    private static PeriodHealth HealthOf(DateRange range, DateOnly today, int maxDays)
    {
        // Order matters: wholly-future short-circuits everything (no query need run at all), and a
        // too-long range must be caught before the milder partly-future note.
        if (range.From > today) return PeriodHealth.WhollyFuture;
        if (range.Days > maxDays) return PeriodHealth.TooLong;
        if (range.To > today) return PeriodHealth.PartlyFuture;
        return PeriodHealth.Ok;
    }

    private static DateOnly NormaliseAnchor(PeriodGrain grain, DateOnly day, PeriodCalendar cal)
        => grain switch
        {
            PeriodGrain.Day => day,
            PeriodGrain.Week => StartOfWeek(day, cal.WeekStartsOn),
            PeriodGrain.Month => new DateOnly(day.Year, day.Month, 1),
            PeriodGrain.Quarter => new DateOnly(day.Year, ((day.Month - 1) / 3 * 3) + 1, 1),
            PeriodGrain.Year => new DateOnly(day.Year, 1, 1),
            PeriodGrain.LeaveYear => StartOfYearWindow(day, cal.LeaveYearStartMonth),
            PeriodGrain.FinancialYear => StartOfYearWindow(day, cal.FinancialYearStartMonth),
            _ => day,
        };

    private static DateRange RangeOf(PeriodGrain grain, DateOnly anchor, int units)
    {
        var n = units < 1 ? 1 : units;
        return grain switch
        {
            PeriodGrain.Day => DateRange.Of(anchor.AddDays(-(n - 1)), anchor),
            PeriodGrain.Week => DateRange.Of(anchor.AddDays(-7 * (n - 1)), anchor.AddDays(6)),
            PeriodGrain.Month => DateRange.Of(anchor.AddMonths(-(n - 1)), anchor.AddMonths(1).AddDays(-1)),
            PeriodGrain.Quarter => DateRange.Of(anchor.AddMonths(-3 * (n - 1)), anchor.AddMonths(3).AddDays(-1)),
            // A leave or financial year's anchor is already its first day, so all three years behave alike.
            _ => DateRange.Of(anchor.AddYears(-(n - 1)), anchor.AddYears(1).AddDays(-1)),
        };
    }

    private static DateOnly ShiftAnchor(PeriodGrain grain, DateOnly anchor, int units) => grain switch
    {
        PeriodGrain.Day => anchor.AddDays(units),
        PeriodGrain.Week => anchor.AddDays(7 * units),
        PeriodGrain.Month => anchor.AddMonths(units),
        PeriodGrain.Quarter => anchor.AddMonths(3 * units),
        _ => anchor.AddYears(units),
    };

    private static DateOnly StartOfWeek(DateOnly day, DayOfWeek startsOn)
        => day.AddDays(-(((int)day.DayOfWeek - (int)startsOn + 7) % 7));

    /// <summary>First day of the year-window containing <paramref name="day"/> for a year starting in
    /// <paramref name="startMonth"/>. A July year containing March 2027 starts July 2026.</summary>
    private static DateOnly StartOfYearWindow(DateOnly day, int startMonth)
    {
        var m = startMonth is >= 1 and <= 12 ? startMonth : 1;
        var year = day.Month >= m ? day.Year : day.Year - 1;
        return new DateOnly(year, m, 1);
    }

    private static string YearLabelOf(PeriodGrain grain, DateOnly anchor, PeriodCalendar cal)
    {
        var startMonth = grain switch
        {
            PeriodGrain.LeaveYear => cal.LeaveYearStartMonth,
            PeriodGrain.FinancialYear => cal.FinancialYearStartMonth,
            _ => 0,
        };
        if (startMonth == 0) return "";
        if (startMonth == 1) return anchor.Year.ToString(CultureInfo.InvariantCulture);
        return $"{anchor.Year}-{(anchor.Year + 1) % 100:D2}";
    }

    private static string FormatAnchor(PeriodGrain grain, DateOnly anchor) => grain switch
    {
        PeriodGrain.Day or PeriodGrain.Week => anchor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        PeriodGrain.Month => anchor.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        PeriodGrain.Quarter => $"{anchor.Year}-Q{((anchor.Month - 1) / 3) + 1}",
        _ => anchor.Year.ToString(CultureInfo.InvariantCulture),
    };

    private static DateOnly? ParseAnchor(string? raw, PeriodGrain grain, PeriodCalendar cal)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();

        // Year-shaped grains: a bare year is the common case and means the year that *starts* then.
        if (grain is PeriodGrain.Year or PeriodGrain.LeaveYear or PeriodGrain.FinancialYear
            && s.Length == 4 && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            && y is >= 1900 and <= 2999)
        {
            var month = grain switch
            {
                PeriodGrain.LeaveYear => cal.LeaveYearStartMonth,
                PeriodGrain.FinancialYear => cal.FinancialYearStartMonth,
                _ => 1,
            };
            return new DateOnly(y, month is >= 1 and <= 12 ? month : 1, 1);
        }

        if (grain == PeriodGrain.Quarter)
        {
            var parts = s.Split('-', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].Length == 2
                && (parts[1][0] is 'Q' or 'q')
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var qy)
                && int.TryParse(parts[1][1..], NumberStyles.None, CultureInfo.InvariantCulture, out var q)
                && q is >= 1 and <= 4)
            {
                return new DateOnly(qy, ((q - 1) * 3) + 1, 1);
            }
        }

        if (grain == PeriodGrain.Month && s.Length == 7
            && DateOnly.TryParseExact($"{s}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var month1))
        {
            return month1;
        }

        // Anything else: the product-wide forgiving contract (ADR-0020), then normalise to the grain.
        return FlexibleDate.TryParse(s, out var d) ? NormaliseAnchor(grain, d, cal) : null;
    }
}
