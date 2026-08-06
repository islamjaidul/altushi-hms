using Hms.Kernel.Time;

namespace Hms.Kernel.Tests;

// ADR-0004: fiscal year is configuration, default July–June (P1). Labels are "FY2026-27" style.
public class FiscalCalendarTests
{
    private static readonly FiscalCalendar JulyJune = new(startMonth: 7);

    [Theory]
    [InlineData(2026, 7, 1, "2026-27")]   // first day of FY
    [InlineData(2027, 6, 30, "2026-27")]  // last day of FY
    [InlineData(2026, 6, 30, "2025-26")]  // day before rollover
    [InlineData(2027, 1, 15, "2026-27")]  // mid-year
    public void July_june_labels(int y, int m, int d, string expected)
        => Assert.Equal(expected, JulyJune.FiscalYearOf(new DateOnly(y, m, d)));

    [Theory]
    [InlineData(2026, 1, 1, "2026")]
    [InlineData(2026, 12, 31, "2026")]
    public void Calendar_year_config_labels_plain_year(int y, int m, int d, string expected)
        => Assert.Equal(expected, new FiscalCalendar(startMonth: 1).FiscalYearOf(new DateOnly(y, m, d)));

    [Fact]
    public void Invalid_start_month_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new FiscalCalendar(13));

    // Spec 0055 added ranges for the reporting period standard. Numbering depends on the *label*,
    // so the two must never disagree about which year a date falls in.
    [Theory]
    [InlineData(2026, 7, 1)]
    [InlineData(2027, 6, 30)]
    [InlineData(2026, 6, 30)]
    [InlineData(2027, 1, 15)]
    public void The_range_and_the_label_agree_about_which_year_a_date_is_in(int y, int m, int d)
    {
        var date = new DateOnly(y, m, d);
        var range = JulyJune.RangeOf(date);

        Assert.True(range.Contains(date));
        Assert.Equal(JulyJune.FiscalYearOf(date), JulyJune.FiscalYearOf(range.From));
        Assert.Equal(JulyJune.FiscalYearOf(date), JulyJune.FiscalYearOf(range.To));
    }

    [Fact]
    public void A_july_year_runs_july_to_june()
    {
        var r = JulyJune.RangeStartingIn(2026);
        Assert.Equal(new DateOnly(2026, 7, 1), r.From);
        Assert.Equal(new DateOnly(2027, 6, 30), r.To);
    }

    [Fact]
    public void A_calendar_year_config_runs_january_to_december()
    {
        var r = new FiscalCalendar(startMonth: 1).RangeStartingIn(2026);
        Assert.Equal(new DateOnly(2026, 1, 1), r.From);
        Assert.Equal(new DateOnly(2026, 12, 31), r.To);
    }
}
