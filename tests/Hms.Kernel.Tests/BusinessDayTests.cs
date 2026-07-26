using Hms.Kernel.Time;

namespace Hms.Kernel.Tests;

// ADR-0004 / P2: business day boundary is configurable, default 00:00 Asia/Dhaka.
// Counter sessions are attributed to the day on which they OPENED (02 §2.6).
public class BusinessDayTests
{
    private static readonly BusinessDayCalendar Default = new(boundary: TimeOnly.MinValue);

    [Fact]
    public void Utc_instant_maps_to_dhaka_date()
    {
        // 2026-07-26 19:30 UTC = 2026-07-27 01:30 Asia/Dhaka (+06:00, no DST)
        var utc = new DateTimeOffset(2026, 7, 26, 19, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 7, 27), Default.BusinessDayOf(utc));
    }

    [Fact]
    public void Before_midnight_dhaka_stays_previous_day()
    {
        // 2026-07-26 17:59 UTC = 23:59 Dhaka
        var utc = new DateTimeOffset(2026, 7, 26, 17, 59, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 7, 26), Default.BusinessDayOf(utc));
    }

    [Fact]
    public void Configured_boundary_shifts_attribution()
    {
        // Boundary 06:00: 01:30 Dhaka still belongs to the previous business day (edge 16).
        var sixAm = new BusinessDayCalendar(boundary: new TimeOnly(6, 0));
        var utc = new DateTimeOffset(2026, 7, 26, 19, 30, 0, TimeSpan.Zero); // 01:30 Dhaka on the 27th
        Assert.Equal(new DateOnly(2026, 7, 26), sixAm.BusinessDayOf(utc));
    }
}
