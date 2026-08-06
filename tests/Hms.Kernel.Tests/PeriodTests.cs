using Hms.Kernel.Time;

namespace Hms.Kernel.Tests;

/// <summary>
/// Spec 0055 — the period standard (module PRD §12.3). Resolution is a pure function of
/// (query, today, calendar), which is exactly why the whole surface can be pinned here in
/// milliseconds instead of through a browser.
/// </summary>
public class PeriodTests
{
    // Thursday. Chosen so the Saturday-start week is unambiguous and the quarter is Q3.
    private static readonly DateOnly Today = new(2026, 8, 6);

    private static readonly PeriodCalendar JulyJuly = PeriodCalendar.Default;                  // leave & fin year from July
    private static readonly PeriodCalendar Jan = new(1, 1, DayOfWeek.Saturday, 400);           // calendar-year employer
    private static readonly PeriodCalendar AprilJuly = new(4, 7, DayOfWeek.Saturday, 400);     // April leave year, July fin year

    private static Period Resolve(PeriodQuery q, PeriodCalendar? cal = null)
        => PeriodResolver.Resolve(q, Today, cal ?? JulyJuly);

    // ---- presets -------------------------------------------------------------------------------

    [Theory]
    [InlineData("today", "2026-08-06", "2026-08-06")]
    [InlineData("yesterday", "2026-08-05", "2026-08-05")]
    [InlineData("this-week", "2026-08-01", "2026-08-07")]   // Saturday 1 Aug – Friday 7 Aug
    [InlineData("this-month", "2026-08-01", "2026-08-31")]
    [InlineData("last-month", "2026-07-01", "2026-07-31")]
    [InlineData("this-quarter", "2026-07-01", "2026-09-30")]
    [InlineData("this-year", "2026-01-01", "2026-12-31")]
    [InlineData("last-12-months", "2025-09-01", "2026-08-31")]
    [InlineData("this-leave-year", "2026-07-01", "2027-06-30")]
    [InlineData("this-financial-year", "2026-07-01", "2027-06-30")]
    public void Presets_resolve_to_the_ranges_the_prd_names(string preset, string from, string to)
    {
        var p = PeriodResolver.FromPreset(preset, Today, JulyJuly);
        Assert.Equal(DateOnly.Parse(from), p.Range.From);
        Assert.Equal(DateOnly.Parse(to), p.Range.To);
    }

    [Fact]
    public void The_leave_year_follows_the_employer_not_the_calendar()
    {
        // April start: 6 Aug 2026 falls in the year that began April 2026.
        var april = PeriodResolver.FromPreset("this-leave-year", Today, AprilJuly);
        Assert.Equal(new DateOnly(2026, 4, 1), april.Range.From);
        Assert.Equal(new DateOnly(2027, 3, 31), april.Range.To);

        // January start: it is simply the calendar year.
        var jan = PeriodResolver.FromPreset("this-leave-year", Today, Jan);
        Assert.Equal(new DateOnly(2026, 1, 1), jan.Range.From);
        Assert.Equal(new DateOnly(2026, 12, 31), jan.Range.To);
    }

    [Fact]
    public void The_leave_year_and_the_financial_year_can_differ()
    {
        var leave = PeriodResolver.FromPreset("this-leave-year", Today, AprilJuly);
        var fin = PeriodResolver.FromPreset("this-financial-year", Today, AprilJuly);

        Assert.Equal(new DateOnly(2026, 4, 1), leave.Range.From);
        Assert.Equal(new DateOnly(2026, 7, 1), fin.Range.From);
    }

    [Fact]
    public void A_date_before_the_year_start_belongs_to_the_previous_year()
    {
        // March 2027, July leave year → the year that began July 2026.
        var march = PeriodResolver.Of(PeriodGrain.LeaveYear, new DateOnly(2027, 3, 15), 1,
            JulyJuly, Today);
        Assert.Equal(new DateOnly(2026, 7, 1), march.Range.From);
        Assert.Equal("2026-27", march.YearLabel);
    }

    [Fact]
    public void A_january_year_is_labelled_with_a_plain_year()
        => Assert.Equal("2026", PeriodResolver.FromPreset("this-leave-year", Today, Jan).YearLabel);

    // ---- arrows --------------------------------------------------------------------------------

    [Fact]
    public void Stepping_a_month_lands_on_the_whole_month_whatever_its_length()
    {
        var march = PeriodResolver.Of(PeriodGrain.Month, new DateOnly(2026, 3, 31), 1, JulyJuly, Today);

        var back = PeriodResolver.Step(march, -1, JulyJuly, Today);
        Assert.Equal(new DateOnly(2026, 2, 1), back.Range.From);
        Assert.Equal(new DateOnly(2026, 2, 28), back.Range.To);   // 2026 is not a leap year

        var forward = PeriodResolver.Step(march, +1, JulyJuly, Today);
        Assert.Equal(new DateOnly(2026, 4, 1), forward.Range.From);
        Assert.Equal(new DateOnly(2026, 4, 30), forward.Range.To);
    }

    [Fact]
    public void Stepping_a_month_crosses_the_year_boundary()
    {
        var december = PeriodResolver.Of(PeriodGrain.Month, new DateOnly(2026, 12, 10), 1, JulyJuly, Today);
        var next = PeriodResolver.Step(december, +1, JulyJuly, Today);
        Assert.Equal(new DateOnly(2027, 1, 1), next.Range.From);
        Assert.Equal(new DateOnly(2027, 1, 31), next.Range.To);
    }

    [Fact]
    public void Stepping_february_of_a_leap_year_keeps_the_extra_day()
    {
        var feb2028 = PeriodResolver.Of(PeriodGrain.Month, new DateOnly(2028, 2, 10), 1, JulyJuly, Today);
        Assert.Equal(new DateOnly(2028, 2, 29), feb2028.Range.To);

        var yearBack = PeriodResolver.Step(feb2028, -12, JulyJuly, Today);
        Assert.Equal(new DateOnly(2027, 2, 28), yearBack.Range.To);
    }

    [Fact]
    public void Stepping_the_fourth_quarter_lands_on_the_first_of_the_next_year()
    {
        var q4 = PeriodResolver.Of(PeriodGrain.Quarter, new DateOnly(2026, 11, 5), 1, JulyJuly, Today);
        Assert.Equal(new DateOnly(2026, 10, 1), q4.Range.From);
        Assert.Equal(new DateOnly(2026, 12, 31), q4.Range.To);

        var q1 = PeriodResolver.Step(q4, +1, JulyJuly, Today);
        Assert.Equal(new DateOnly(2027, 1, 1), q1.Range.From);
        Assert.Equal(new DateOnly(2027, 3, 31), q1.Range.To);
    }

    // This is the whole reason leave year is a grain and not a preset: the arrow must move a year,
    // not 365 days, or an employer whose year starts in April drifts by a day every leap year.
    [Fact]
    public void Stepping_a_leave_year_moves_a_whole_leave_year()
    {
        var current = PeriodResolver.FromPreset("this-leave-year", Today, AprilJuly);
        var previous = PeriodResolver.Step(current, -1, AprilJuly, Today);

        Assert.Equal(new DateOnly(2025, 4, 1), previous.Range.From);
        Assert.Equal(new DateOnly(2026, 3, 31), previous.Range.To);
    }

    [Fact]
    public void Stepping_a_twelve_month_window_moves_one_month_and_stays_twelve_long()
    {
        var last12 = PeriodResolver.FromPreset("last-12-months", Today, JulyJuly);
        var back = PeriodResolver.Step(last12, -1, JulyJuly, Today);

        Assert.Equal(new DateOnly(2025, 8, 1), back.Range.From);
        Assert.Equal(new DateOnly(2026, 7, 31), back.Range.To);
        Assert.Equal(12, back.Units);
    }

    [Fact]
    public void Stepping_a_custom_range_moves_it_by_its_own_length()
    {
        var custom = Resolve(new PeriodQuery(Grain: "custom", From: "01 Jul 2026", To: "10 Jul 2026"));
        Assert.Equal(10, custom.Range.Days);

        var back = PeriodResolver.Step(custom, -1, JulyJuly, Today);
        Assert.Equal(new DateOnly(2026, 6, 21), back.Range.From);
        Assert.Equal(new DateOnly(2026, 6, 30), back.Range.To);
    }

    [Fact]
    public void Stepping_leaves_the_preset_behind()
    {
        var thisMonth = PeriodResolver.FromPreset("this-month", Today, JulyJuly);
        Assert.Equal("this-month", thisMonth.Preset);

        // Once you have stepped, you are no longer on "this month" and the chip must not say so.
        Assert.Equal("", PeriodResolver.Step(thisMonth, -1, JulyJuly, Today).Preset);
    }

    // ---- health --------------------------------------------------------------------------------

    [Theory]
    [InlineData("2026-08-06", PeriodHealth.Ok)]           // ends today
    [InlineData("2026-08-05", PeriodHealth.Ok)]           // wholly past
    [InlineData("2026-08-07", PeriodHealth.PartlyFuture)] // starts today, ends tomorrow
    public void Health_of_a_range_starting_today(string to, PeriodHealth expected)
    {
        var p = Resolve(new PeriodQuery(Grain: "custom", From: "05 Aug 2026", To: to));
        Assert.Equal(expected, p.Health);
    }

    [Fact]
    public void A_period_starting_tomorrow_is_wholly_future_and_is_not_queryable()
    {
        var p = Resolve(new PeriodQuery(Grain: "custom", From: "07 Aug 2026", To: "31 Aug 2026"));
        Assert.Equal(PeriodHealth.WhollyFuture, p.Health);
        Assert.False(p.Queryable);
    }

    [Fact]
    public void Too_long_is_measured_against_the_configured_maximum()
    {
        var cal = JulyJuly with { MaxRangeDays = 31 };

        var exactly31 = PeriodResolver.Resolve(
            new PeriodQuery(Grain: "custom", From: "01 Jul 2026", To: "31 Jul 2026"), Today, cal);
        Assert.Equal(PeriodHealth.Ok, exactly31.Health);

        var thirtyTwo = PeriodResolver.Resolve(
            new PeriodQuery(Grain: "custom", From: "30 Jun 2026", To: "31 Jul 2026"), Today, cal);
        Assert.Equal(PeriodHealth.TooLong, thirtyTwo.Health);
    }

    [Fact]
    public void A_future_period_is_reported_as_future_even_when_it_is_also_too_long()
    {
        // Wholly-future wins: there is no point warning about cost for a query that will not run.
        var cal = JulyJuly with { MaxRangeDays = 10 };
        var p = PeriodResolver.Resolve(
            new PeriodQuery(Grain: "custom", From: "01 Jan 2027", To: "31 Dec 2027"), Today, cal);
        Assert.Equal(PeriodHealth.WhollyFuture, p.Health);
    }

    // ---- parsing and defaults ------------------------------------------------------------------

    [Fact]
    public void An_empty_query_falls_back_to_the_screens_default_grain()
    {
        var p = PeriodResolver.Resolve(default, Today, JulyJuly, PeriodGrain.Month);
        Assert.Equal(PeriodGrain.Month, p.Grain);
        Assert.Equal(new DateOnly(2026, 8, 1), p.Range.From);
    }

    [Fact]
    public void Dates_alone_mean_a_custom_period()
    {
        // ADR-0020 rule 4: an explicit date always wins, and a submitted range silently ignored is
        // a banned defect class.
        var p = Resolve(new PeriodQuery(From: "01 Mar 2026", To: "15 Mar 2026"));
        Assert.Equal(PeriodGrain.Custom, p.Grain);
        Assert.Equal(new DateOnly(2026, 3, 1), p.Range.From);
    }

    [Fact]
    public void A_half_filled_custom_range_is_not_an_error()
    {
        var openEnded = Resolve(new PeriodQuery(Grain: "custom", From: "01 Aug 2026"));
        Assert.Equal(new DateOnly(2026, 8, 1), openEnded.Range.From);
        Assert.Equal(Today, openEnded.Range.To);
    }

    [Fact]
    public void Garbage_degrades_to_a_default_rather_than_throwing()
    {
        var p = Resolve(new PeriodQuery(Grain: "fortnight", Anchor: "not a date"));
        Assert.Equal(PeriodGrain.Month, p.Grain);
        Assert.Equal(new DateOnly(2026, 8, 1), p.Range.From);
    }

    [Fact]
    public void Dates_are_read_day_first_per_the_product_contract()
    {
        // ADR-0020 / §7 U13: "03/04/2026" is 3 April, never 4 March.
        var p = Resolve(new PeriodQuery(Grain: "custom", From: "03/04/2026", To: "03/04/2026"));
        Assert.Equal(new DateOnly(2026, 4, 3), p.Range.From);
    }

    [Fact]
    public void Any_day_inside_a_week_resolves_to_the_same_week()
    {
        var saturday = PeriodResolver.Of(PeriodGrain.Week, new DateOnly(2026, 8, 1), 1, JulyJuly, Today);
        var friday = PeriodResolver.Of(PeriodGrain.Week, new DateOnly(2026, 8, 7), 1, JulyJuly, Today);
        Assert.Equal(saturday.Range, friday.Range);
        Assert.Equal(new DateOnly(2026, 8, 1), saturday.Range.From);
    }

    // ---- comparison ----------------------------------------------------------------------------

    [Fact]
    public void Comparing_to_the_previous_period_uses_the_previous_whole_unit()
    {
        var p = Resolve(new PeriodQuery(Grain: "month", Anchor: "2026-08", Compare: "prev"));
        Assert.Equal(PeriodCompare.PreviousPeriod, p.Compare);
        Assert.Equal(new DateOnly(2026, 7, 1), p.CompareRange!.Value.From);
        Assert.Equal(new DateOnly(2026, 7, 31), p.CompareRange!.Value.To);
    }

    [Fact]
    public void Comparing_year_on_year_uses_the_same_unit_a_year_earlier()
    {
        var p = Resolve(new PeriodQuery(Grain: "month", Anchor: "2026-02", Compare: "yoy"));
        Assert.Equal(new DateOnly(2025, 2, 1), p.CompareRange!.Value.From);
        Assert.Equal(new DateOnly(2025, 2, 28), p.CompareRange!.Value.To);
    }

    [Fact]
    public void No_comparison_carries_no_range()
        => Assert.Null(Resolve(new PeriodQuery(Grain: "month", Anchor: "2026-08")).CompareRange);

    // ---- round-trip: the URL is the sharing mechanism -------------------------------------------

    public static TheoryData<string, string, int, string> RoundTrips()
    {
        var data = new TheoryData<string, string, int, string>();
        var anchors = new Dictionary<string, string>
        {
            ["day"] = "2026-07-15",
            ["week"] = "2026-08-01",
            ["month"] = "2026-07",
            ["quarter"] = "2026-Q3",
            ["year"] = "2026",
            ["leaveyear"] = "2026",
            ["finyear"] = "2026",
        };
        foreach (var (grain, anchor) in anchors)
            foreach (var units in new[] { 1, 3, 12 })
                foreach (var cmp in new[] { "", "prev", "yoy" })
                    data.Add(grain, anchor, units, cmp);
        return data;
    }

    /// <summary>
    /// §12.3: "It is in the URL, so a period is a link an HR officer can send to the MD." A period
    /// that does not survive serialisation is a link that means something else for the recipient.
    /// </summary>
    [Theory]
    [MemberData(nameof(RoundTrips))]
    public void A_period_survives_the_query_string(string grain, string anchor, int units, string cmp)
    {
        foreach (var cal in new[] { JulyJuly, Jan, AprilJuly })
        {
            var original = PeriodResolver.Resolve(
                new PeriodQuery(Grain: grain, Anchor: anchor, Units: units,
                    Compare: cmp.Length == 0 ? null : cmp), Today, cal);

            var again = PeriodResolver.Resolve(PeriodResolver.ToQuery(original), Today, cal);

            Assert.Equal(original.Grain, again.Grain);
            Assert.Equal(original.Anchor, again.Anchor);
            Assert.Equal(original.Units, again.Units);
            Assert.Equal(original.Range, again.Range);
            Assert.Equal(original.Compare, again.Compare);
            Assert.Equal(original.CompareRange, again.CompareRange);
            Assert.Equal(original.Health, again.Health);
        }
    }

    [Fact]
    public void A_custom_period_survives_the_query_string()
    {
        var original = Resolve(new PeriodQuery(Grain: "custom", From: "03 Feb 2026", To: "17 Mar 2026"));
        var again = Resolve(PeriodResolver.ToQuery(original));

        Assert.Equal(PeriodGrain.Custom, again.Grain);
        Assert.Equal(original.Range, again.Range);
    }

    [Fact]
    public void A_preset_normalises_to_a_concrete_period()
    {
        // The canonical form carries no preset, which is what makes the address bar always state a
        // real period rather than a relative one.
        var q = PeriodResolver.ToQuery(PeriodResolver.FromPreset("this-month", Today, JulyJuly));
        Assert.Equal("month", q.Grain);
        Assert.Equal("2026-08", q.Anchor);
        Assert.Null(q.Preset);
    }

    // ---- words ---------------------------------------------------------------------------------

    [Fact]
    public void The_period_sentence_is_the_one_the_prd_prints()
    {
        var july = PeriodResolver.Of(PeriodGrain.Month, new DateOnly(2026, 7, 10), 1, JulyJuly, Today);
        Assert.Equal("1 – 31 July 2026 · Asia/Dhaka", PeriodWords.Describe(july));
    }

    [Theory]
    [InlineData("2026-08-06", "2026-08-06", "6 August 2026")]
    [InlineData("2026-07-01", "2026-07-31", "1 – 31 July 2026")]
    [InlineData("2026-07-01", "2026-08-15", "1 July – 15 August 2026")]
    [InlineData("2026-07-01", "2027-01-15", "1 July 2026 – 15 January 2027")]
    public void Ranges_read_as_prose(string from, string to, string expected)
        => Assert.Equal(expected,
            PeriodWords.Range(DateRange.Of(DateOnly.Parse(from), DateOnly.Parse(to))));

    [Fact]
    public void A_leave_year_says_so_and_carries_its_label()
    {
        var p = PeriodResolver.FromPreset("this-leave-year", Today, JulyJuly);
        Assert.Equal("Leave year 2026-27 · 1 July 2026 – 30 June 2027 · Asia/Dhaka",
            PeriodWords.Describe(p));
    }

    [Fact]
    public void A_healthy_period_has_nothing_to_say()
    {
        var p = PeriodResolver.Of(PeriodGrain.Month, new DateOnly(2026, 7, 1), 1, JulyJuly, Today);
        Assert.Null(PeriodWords.Notice(p, Today, 400));
    }

    [Fact]
    public void A_future_period_explains_itself_and_names_the_last_usable_day()
    {
        var p = Resolve(new PeriodQuery(Grain: "custom", From: "01 Jan 2027", To: "31 Jan 2027"));
        var notice = PeriodWords.Notice(p, Today, 400);

        Assert.NotNull(notice);
        Assert.Contains("has not happened yet", notice);
        Assert.Contains("6 August 2026", notice);
    }

    [Fact]
    public void A_partly_future_period_says_where_the_figures_stop()
    {
        var p = Resolve(new PeriodQuery(Grain: "month", Anchor: "2026-08"));
        Assert.Equal(PeriodHealth.PartlyFuture, p.Health);
        Assert.Contains("only up to today", PeriodWords.Notice(p, Today, 400)!);
    }

    [Fact]
    public void An_over_long_period_offers_a_way_out_rather_than_an_error()
    {
        var cal = JulyJuly with { MaxRangeDays = 31 };
        var p = PeriodResolver.Resolve(
            new PeriodQuery(Grain: "custom", From: "01 Jan 2025", To: "31 Dec 2025"), Today, cal);

        var notice = PeriodWords.Notice(p, Today, cal.MaxRangeDays);
        Assert.Contains("365 days", notice);
        Assert.Contains("shorter period", notice);
    }

    [Fact]
    public void Comparison_words_name_the_range_being_compared_against()
    {
        var p = Resolve(new PeriodQuery(Grain: "month", Anchor: "2026-08", Compare: "prev"));
        Assert.Equal("vs 1 – 31 July 2026", PeriodWords.CompareWords(p));
    }
}
