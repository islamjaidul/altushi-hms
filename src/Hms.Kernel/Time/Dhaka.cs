namespace Hms.Kernel.Time;

/// <summary>
/// The product's one timezone (§C: Asia/Dhaka, no DST). Every conversion between an operator's wall
/// clock and stored UTC goes through here.
/// <para>
/// This exists because the same fact was written four different ways: <c>Ui.Local</c> and
/// <c>Ui.DhakaMidnightUtc</c> in the Shell, a private field in <see cref="BusinessDayCalendar"/>,
/// and a bare <c>TimeSpan.FromHours(6)</c> in <c>Ipd/Reports</c>, <c>AttendanceService</c> and
/// <c>CsvPunchSource</c>. The literal offset is the dangerous one — it is right today only because
/// Bangladesh has no DST, and it hides the assumption rather than stating it.
/// </para>
/// <para>
/// It lives in the Kernel rather than the Shell so that <see cref="DateRange"/> can convert a span
/// of business days to a UTC interval without the Kernel depending on a web assembly.
/// </para>
/// </summary>
public static class Dhaka
{
    public const string ZoneName = "Asia/Dhaka";

    public static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(ZoneName);

    /// <summary>Everything an operator reads is Dhaka wall-clock; storage stays UTC.</summary>
    public static DateTimeOffset Local(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    /// <summary>The Dhaka calendar date an instant falls on.</summary>
    public static DateOnly DateOf(DateTimeOffset instant) => DateOnly.FromDateTime(Local(instant).DateTime);

    /// <summary>Today in Dhaka. Takes the clock as a parameter so callers stay testable.</summary>
    public static DateOnly Today(TimeProvider clock) => DateOf(clock.GetUtcNow());

    /// <summary>
    /// Start of a Dhaka day as a UTC instant. Queries compare against <c>timestamptz</c>, which
    /// Npgsql binds in UTC only — so the conversion happens here, once, rather than per query.
    /// </summary>
    public static DateTimeOffset MidnightUtc(DateOnly day)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        var offset = Zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
