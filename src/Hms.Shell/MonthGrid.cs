namespace Hms.Shell;

/// <summary>
/// One day in a month grid: a letter, a tone, and what it means in words.
/// <para>
/// The letter is what a Bangladeshi HR office already writes on paper — P for present, A for absent,
/// L for leave — so a printed grid reads the same as the one on the wall it replaces.
/// </para>
/// </summary>
public sealed record GridCell(
    DateOnly OnDate, string Letter, string Tone, string Title, string? Href = null)
{
    /// <summary>A day outside the month, rendered as a hole rather than a blank the eye skips.</summary>
    public static GridCell Blank(DateOnly on) => new(on, "", "blank", "");
}

/// <summary>One labelled row of a grid — an employee, a unit, or the calendar itself.</summary>
public sealed record GridRow(string Label, string? Sub, IReadOnlyList<GridCell> Cells, string? Href = null);

/// <summary>
/// A month laid out as a grid (module PRD §7.4's attendance calendar, §7.6's leave calendar, §7.2's
/// holiday calendar).
/// <para>
/// <b>Built once, on purpose.</b> Spec 0055 deferred the leave calendar as "calendar-shaped"; spec
/// 0058 deferred the attendance calendar and the holiday calendar for the same reason. Three specs
/// deferring the same component is the component telling you it should exist once — and the three
/// views then differ only in what fills a cell, so an operator who learns one has learned all three
/// (§7 U9 applied to a component rather than to a report).
/// </para>
/// <para>
/// The week starts on the employer's configured day (spec 0058), not on a hard-coded Sunday.
/// </para>
/// </summary>
public sealed record MonthGrid(
    DateOnly Month,
    DayOfWeek WeekStartsOn,
    IReadOnlyList<GridRow> Rows,
    IReadOnlyList<DateOnly> Days,
    string? EmptyReason = null)
{
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Every day of the month, in order. The column headings of a matrix view.</summary>
    public static IReadOnlyList<DateOnly> DaysOf(DateOnly month)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var count = DateTime.DaysInMonth(month.Year, month.Month);
        return [.. Enumerable.Range(0, count).Select(first.AddDays)];
    }

    /// <summary>
    /// The month as calendar weeks, padded to whole rows. Used by the single-subject views — one
    /// employee's attendance, the holiday calendar — where a wall-calendar shape reads better than
    /// a strip of 31 columns.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<DateOnly>> WeeksOf(DateOnly month, DayOfWeek weekStartsOn)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);

        // How far back to the most recent week-start on or before the 1st.
        var lead = ((int)first.DayOfWeek - (int)weekStartsOn + 7) % 7;
        var cursor = first.AddDays(-lead);

        var weeks = new List<IReadOnlyList<DateOnly>>();
        while (cursor <= last)
        {
            weeks.Add([.. Enumerable.Range(0, 7).Select(cursor.AddDays)]);
            cursor = cursor.AddDays(7);
        }
        return weeks;
    }

    /// <summary>
    /// Whether a day belongs to the grid's month. A week row spills into the neighbouring months at
    /// both ends, and those cells are holes rather than days somebody might click.
    /// </summary>
    public bool InMonth(DateOnly day) => day.Month == Month.Month && day.Year == Month.Year;

    /// <summary>The weekday headings, starting on the employer's day.</summary>
    public IReadOnlyList<DayOfWeek> Weekdays =>
        [.. Enumerable.Range(0, 7).Select(i => (DayOfWeek)(((int)WeekStartsOn + i) % 7))];

    public static MonthGrid Empty(DateOnly month, DayOfWeek weekStartsOn, string reason) =>
        new(month, weekStartsOn, [], DaysOf(month), reason);
}
