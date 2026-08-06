using Hms.Hr;
using Hms.Shell;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0059 — the leave-year close arithmetic (module PRD §7.6, G33).
/// <para>
/// Order matters and is the whole point: carry up to the cap, encash out of the carry, and
/// everything still standing lapses. Lapsing first would silently destroy days the employer meant
/// to carry.
/// </para>
/// </summary>
public class LeaveCloseTests
{
    private const int Day = 10_000;

    [Fact]
    public void A_balance_inside_the_cap_carries_whole()
    {
        var m = LeaveYearService.Close(
            closingBalanceBp: 5 * Day, entitlementBp: 20 * Day, maxCarryBp: 10 * Day,
            encashable: false, maxEncashBp: 0);

        Assert.Equal(5 * Day, m.CarryBp);
        Assert.Equal(0, m.LapseBp);
        Assert.Equal(20 * Day, m.AccrueBp);
    }

    [Fact]
    public void Everything_above_the_cap_lapses()
    {
        var m = LeaveYearService.Close(15 * Day, 20 * Day, 10 * Day, false, 0);

        Assert.Equal(10 * Day, m.CarryBp);
        Assert.Equal(5 * Day, m.LapseBp);
    }

    [Fact]
    public void Encashment_comes_out_of_the_carry_not_out_of_thin_air()
    {
        var m = LeaveYearService.Close(15 * Day, 20 * Day, maxCarryBp: 10 * Day,
            encashable: true, maxEncashBp: 4 * Day);

        Assert.Equal(4 * Day, m.EncashBp);
        Assert.Equal(6 * Day, m.CarryBp);
        Assert.Equal(5 * Day, m.LapseBp);
        // The three always add back up to what was there.
        Assert.Equal(15 * Day, m.CarryBp + m.LapseBp + m.EncashBp);
    }

    [Fact]
    public void An_unencashable_type_encashes_nothing_however_large_the_cap()
    {
        var m = LeaveYearService.Close(15 * Day, 20 * Day, 10 * Day,
            encashable: false, maxEncashBp: 99 * Day);

        Assert.Equal(0, m.EncashBp);
        Assert.Equal(10 * Day, m.CarryBp);
    }

    [Fact]
    public void A_zero_carry_cap_lapses_everything_and_still_accrues_the_new_year()
    {
        var m = LeaveYearService.Close(15 * Day, 20 * Day, maxCarryBp: 0, encashable: false,
            maxEncashBp: 0);

        Assert.Equal(0, m.CarryBp);
        Assert.Equal(15 * Day, m.LapseBp);
        Assert.Equal(20 * Day, m.AccrueBp);
    }

    [Fact]
    public void An_empty_balance_carries_nothing_and_says_so()
    {
        var m = LeaveYearService.Close(0, 20 * Day, 10 * Day, true, 5 * Day);

        Assert.Equal(0, m.CarryBp);
        Assert.Equal(0, m.LapseBp);
        Assert.Equal(0, m.EncashBp);
        Assert.Equal(20 * Day, m.AccrueBp);
        Assert.Contains("nothing left to carry", m.Basis);
    }

    [Fact]
    public void A_negative_balance_is_treated_as_empty_rather_than_inverted()
    {
        // The database refuses an overdrawn balance, but the close must not turn one into a
        // negative carry if it ever saw one.
        var m = LeaveYearService.Close(-5 * Day, 20 * Day, 10 * Day, true, 5 * Day);

        Assert.Equal(0, m.CarryBp);
        Assert.Equal(0, m.LapseBp);
    }

    [Fact]
    public void Every_movement_states_its_own_arithmetic()
    {
        var m = LeaveYearService.Close(15 * Day, 20 * Day, 10 * Day, true, 4 * Day);

        Assert.Contains("15 day(s) at year end", m.Basis);
        Assert.Contains("6 carried", m.Basis);
        Assert.Contains("4 encashed", m.Basis);
        Assert.Contains("5 lapsed", m.Basis);
    }

    [Fact]
    public void A_half_day_survives_the_close()
    {
        // Leave is basis points of a day precisely so half days are real.
        var m = LeaveYearService.Close(15_000, 20 * Day, 10 * Day, false, 0);

        Assert.Equal(15_000, m.CarryBp);
    }
}

/// <summary>
/// Spec 0059 — the one month grid (module PRD §7.4/§7.6/§7.2). Three specs deferred this component;
/// its layout is pinned here so all three views can rely on it.
/// </summary>
public class MonthGridTests
{
    private static readonly DateOnly March = new(2026, 3, 1);

    [Fact]
    public void A_month_has_its_own_days_in_order()
    {
        var days = MonthGrid.DaysOf(March);

        Assert.Equal(31, days.Count);
        Assert.Equal(new DateOnly(2026, 3, 1), days[0]);
        Assert.Equal(new DateOnly(2026, 3, 31), days[^1]);
    }

    [Fact]
    public void February_in_a_leap_year_has_twenty_nine()
    {
        Assert.Equal(29, MonthGrid.DaysOf(new DateOnly(2028, 2, 1)).Count);
        Assert.Equal(28, MonthGrid.DaysOf(new DateOnly(2026, 2, 1)).Count);
    }

    [Fact]
    public void Weeks_start_on_the_employers_day_not_on_sunday()
    {
        // Saturday for Bangladesh. CultureInfo returns Sunday under the invariant culture, which is
        // the mistake spec 0058 made configurable.
        var weeks = MonthGrid.WeeksOf(March, DayOfWeek.Saturday);

        Assert.All(weeks, w => Assert.Equal(DayOfWeek.Saturday, w[0].DayOfWeek));
        Assert.All(weeks, w => Assert.Equal(7, w.Count));
    }

    [Fact]
    public void Weeks_cover_every_day_of_the_month()
    {
        var weeks = MonthGrid.WeeksOf(March, DayOfWeek.Saturday);
        var covered = weeks.SelectMany(w => w).ToHashSet();

        foreach (var day in MonthGrid.DaysOf(March))
            Assert.Contains(day, covered);
    }

    [Fact]
    public void A_month_starting_on_the_week_start_needs_no_lead()
    {
        // 1 August 2026 is a Saturday.
        var weeks = MonthGrid.WeeksOf(new DateOnly(2026, 8, 1), DayOfWeek.Saturday);

        Assert.Equal(new DateOnly(2026, 8, 1), weeks[0][0]);
    }

    [Fact]
    public void Days_outside_the_month_are_holes_rather_than_days()
    {
        var grid = MonthGrid.Empty(March, DayOfWeek.Saturday, "nothing");

        Assert.True(grid.InMonth(new DateOnly(2026, 3, 15)));
        Assert.False(grid.InMonth(new DateOnly(2026, 2, 28)));
        Assert.False(grid.InMonth(new DateOnly(2026, 4, 1)));
        // Same day, different year.
        Assert.False(grid.InMonth(new DateOnly(2025, 3, 15)));
    }

    [Fact]
    public void The_weekday_headings_start_on_the_employers_day()
    {
        var grid = MonthGrid.Empty(March, DayOfWeek.Saturday, "nothing");

        Assert.Equal(DayOfWeek.Saturday, grid.Weekdays[0]);
        Assert.Equal(DayOfWeek.Friday, grid.Weekdays[^1]);
        Assert.Equal(7, grid.Weekdays.Count);
    }

    [Fact]
    public void An_empty_grid_carries_its_reason()
    {
        var grid = MonthGrid.Empty(March, DayOfWeek.Saturday, "Nobody is on leave this month.");

        Assert.True(grid.IsEmpty);
        Assert.Equal("Nobody is on leave this month.", grid.EmptyReason);
        // It still knows its own days, so a header renders even with no rows.
        Assert.Equal(31, grid.Days.Count);
    }
}
