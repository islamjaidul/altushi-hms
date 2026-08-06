namespace Hms.Kernel.Time;

/// <summary>
/// Maps an instant to the hospital's business day (ADR-0004, edge 16).
/// The boundary time is configuration (P2 default 00:00); the wall clock is Asia/Dhaka (no DST).
/// Sessions are attributed to the day they opened (02 §2.6) — callers pass the opening instant.
/// </summary>
public sealed class BusinessDayCalendar(TimeOnly boundary)
{
    public TimeOnly Boundary { get; } = boundary;

    public DateOnly BusinessDayOf(DateTimeOffset instant)
    {
        var local = Dhaka.Local(instant);
        var date = DateOnly.FromDateTime(local.DateTime);
        // Before the boundary, the instant still belongs to the previous business day.
        return TimeOnly.FromDateTime(local.DateTime) < Boundary ? date.AddDays(-1) : date;
    }
}
