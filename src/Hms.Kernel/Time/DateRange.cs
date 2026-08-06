namespace Hms.Kernel.Time;

/// <summary>
/// An inclusive span of business days — the type the reporting layer did not have.
/// <para>
/// The swap-if-inverted guard lives here, once. Before this type it was written out in
/// <c>Billing/Reports</c>, <c>Pharmacy/Reports</c> and <c>Ot/Register</c>, so a fourth report was a
/// fourth chance to forget it. The private constructor makes <see cref="Of"/> the only door, which
/// means an inverted range is not merely discouraged — it is unrepresentable.
/// </para>
/// </summary>
public readonly record struct DateRange
{
    private DateRange(DateOnly from, DateOnly to)
    {
        From = from;
        To = to;
    }

    public DateOnly From { get; }

    public DateOnly To { get; }

    /// <summary>Orders the pair, so a From/To the operator typed backwards still means something.</summary>
    public static DateRange Of(DateOnly a, DateOnly b) => a <= b ? new(a, b) : new(b, a);

    public static DateRange OneDay(DateOnly day) => new(day, day);

    /// <summary>Inclusive, so a single day is 1 rather than 0.</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly day) => day >= From && day <= To;

    public bool Overlaps(DateRange other) => From <= other.To && other.From <= To;

    /// <summary>
    /// The half-open UTC interval this range covers: <c>[From 00:00 Dhaka, To+1 00:00 Dhaka)</c>.
    /// The one place the Dhaka offset enters a query. Callers compare
    /// <c>&gt;= Start &amp;&amp; &lt; EndExclusive</c> — half-open, so an instant at midnight on the
    /// last day belongs to the next range, not to both.
    /// </summary>
    public (DateTimeOffset Start, DateTimeOffset EndExclusive) Instants()
        => (Dhaka.MidnightUtc(From), Dhaka.MidnightUtc(To.AddDays(1)));

    public override string ToString() => From == To
        ? From.ToString("dd MMM yyyy")
        : $"{From:dd MMM yyyy} – {To:dd MMM yyyy}";
}
