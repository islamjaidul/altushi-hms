using Hms.Kernel.Time;

namespace Hms.Kernel.Tests;

// Spec 0055. Before DateRange existed, the swap-if-inverted guard was hand-written in three page
// models and asserted in none of them.
public class DateRangeTests
{
    [Fact]
    public void Of_orders_an_inverted_pair()
    {
        var r = DateRange.Of(new DateOnly(2026, 7, 31), new DateOnly(2026, 7, 1));
        Assert.Equal(new DateOnly(2026, 7, 1), r.From);
        Assert.Equal(new DateOnly(2026, 7, 31), r.To);
    }

    [Fact]
    public void A_single_day_is_one_day_not_zero()
        => Assert.Equal(1, DateRange.OneDay(new DateOnly(2026, 8, 6)).Days);

    [Fact]
    public void Days_is_inclusive_of_both_ends()
        => Assert.Equal(31, DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)).Days);

    [Theory]
    [InlineData(2026, 6, 30, false)]
    [InlineData(2026, 7, 1, true)]
    [InlineData(2026, 7, 31, true)]
    [InlineData(2026, 8, 1, false)]
    public void Contains_is_inclusive(int y, int m, int d, bool expected)
        => Assert.Equal(expected,
            DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31))
                .Contains(new DateOnly(y, m, d)));

    [Fact]
    public void Overlaps_counts_a_shared_endpoint()
    {
        var july = DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        Assert.True(july.Overlaps(DateRange.Of(new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 15))));
        Assert.False(july.Overlaps(DateRange.Of(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 15))));
    }

    // The one place the Dhaka offset enters a query. Four files hand-wrote TimeSpan.FromHours(6)
    // before this; nothing pinned the value. Dhaka is UTC+6 with no DST, so midnight local is 18:00Z
    // on the previous day.
    [Fact]
    public void Instants_are_the_half_open_utc_interval_of_a_dhaka_day()
    {
        var (start, end) = DateRange.OneDay(new DateOnly(2026, 7, 15)).Instants();

        Assert.Equal(new DateTimeOffset(2026, 7, 14, 18, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 18, 0, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void Instants_of_adjacent_days_meet_exactly_once()
    {
        var (_, firstEnd) = DateRange.OneDay(new DateOnly(2026, 7, 15)).Instants();
        var (secondStart, _) = DateRange.OneDay(new DateOnly(2026, 7, 16)).Instants();

        // Half-open: an instant at the boundary belongs to the second day only, never to both.
        Assert.Equal(firstEnd, secondStart);
    }

    [Fact]
    public void Instants_span_the_whole_range()
    {
        var (start, end) = DateRange.Of(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)).Instants();
        Assert.Equal(new DateTimeOffset(2026, 6, 30, 18, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 18, 0, 0, TimeSpan.Zero), end);
    }
}
