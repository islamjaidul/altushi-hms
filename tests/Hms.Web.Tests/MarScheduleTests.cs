using Hms.Emr;
using Hms.Emr.Data;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0041: the Medicine Chart schedule is generated from the indoor prescription's free-text
/// frequency ("1+0+1", "8 hourly") and duration ("7 days"). This is clinical arithmetic — the
/// payroll lesson (spec 0039) is that untested arithmetic is the top defect class — so the
/// expansion lives in <see cref="MarSchedule"/>, a pure static class with no clock and no I/O,
/// and every rule is pinned here.
///
/// The two rules that matter most:
///  - anything the parser does not positively recognise returns <c>false</c> — a wrong guess at
///    a drug time is worse than asking the nurse to schedule by hand;
///  - day one drops slots at or before <c>notBefore</c> — a generated dose must never be born
///    overdue.
/// </summary>
public class MarScheduleTests
{
    // A fixed local calendar day; expansion is pure so the date carries no timezone meaning.
    private static readonly DateOnly Day1 = new(2026, 8, 3);

    private static IReadOnlyList<(DateOnly Day, TimeOnly Time)> Expand(
        string? frequency, string? duration, TimeOnly? notBefore = null)
    {
        var ok = MarSchedule.TryExpand(frequency, duration, Day1, notBefore,
            out var slots, out _);
        Assert.True(ok, $"'{frequency}' should parse but was rejected.");
        return slots;
    }

    // ----- a+b+c: the Bangla prescription staple ---------------------------------------------

    [Fact]
    public void Morning_night_for_seven_days_is_fourteen_doses()
    {
        var slots = Expand("1+0+1", "7 days");
        Assert.Equal(14, slots.Count);
        Assert.All(slots, s => Assert.Contains(s.Time, new[]
        {
            new TimeOnly(8, 0), new TimeOnly(20, 0)
        }));
        Assert.Equal(Day1, slots.Min(s => s.Day));
        Assert.Equal(Day1.AddDays(6), slots.Max(s => s.Day));
    }

    [Fact]
    public void Three_part_frequency_uses_morning_midday_night_slots()
    {
        var slots = Expand("1+1+1", "7 days");
        Assert.Equal(21, slots.Count);
        Assert.Equal(
            new[] { new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(20, 0) },
            slots.Where(s => s.Day == Day1).Select(s => s.Time).Order().ToArray());
    }

    [Fact]
    public void Four_part_frequency_uses_the_four_hourly_slots()
    {
        var slots = Expand("1+1+1+1", "2 days");
        Assert.Equal(8, slots.Count);
        Assert.Equal(
            new[]
            {
                new TimeOnly(8, 0), new TimeOnly(12, 0),
                new TimeOnly(16, 0), new TimeOnly(20, 0)
            },
            slots.Where(s => s.Day == Day1).Select(s => s.Time).Order().ToArray());
    }

    [Fact]
    public void Zero_count_positions_emit_no_slot()
    {
        // "0+0+1" is a real prescription (night only) — position decides the slot, the
        // count decides whether it exists.
        var slots = Expand("0+0+1", "3 days");
        Assert.Equal(3, slots.Count);
        Assert.All(slots, s => Assert.Equal(new TimeOnly(20, 0), s.Time));
    }

    [Fact]
    public void All_zero_frequency_parses_to_an_empty_schedule()
    {
        // "0+0+0" is syntactically valid and clinically empty. It must parse (so the caller
        // does not report it as unreadable) and produce nothing.
        var ok = MarSchedule.TryExpand("0+0+0", "5 days", Day1, null, out var slots, out _);
        Assert.True(ok);
        Assert.Empty(slots);
    }

    // ----- N hourly --------------------------------------------------------------------------

    [Theory]
    [InlineData("4 hourly", new[] { 6, 10, 14, 18, 22 })]
    [InlineData("6 hourly", new[] { 6, 12, 18 })]
    [InlineData("8 hourly", new[] { 6, 14, 22 })]
    [InlineData("12 hourly", new[] { 6, 18 })]
    [InlineData("24 hourly", new[] { 6 })]
    public void N_hourly_walks_from_six_am(string frequency, int[] hours)
    {
        var slots = Expand(frequency, "1 day");
        Assert.Equal(
            hours.Select(h => new TimeOnly(h, 0)).ToArray(),
            slots.Select(s => s.Time).Order().ToArray());
    }

    [Theory]
    [InlineData("5 hourly")]     // not a slot pattern the ward runs
    [InlineData("0 hourly")]
    [InlineData("hourly")]
    public void Unknown_hourly_intervals_are_refused(string frequency)
    {
        Assert.False(MarSchedule.TryExpand(frequency, "3 days", Day1, null, out _, out _),
            $"'{frequency}' is not a recognised interval and must fall back to manual scheduling.");
    }

    // ----- anything else: never guess --------------------------------------------------------

    [Theory]
    [InlineData("PRN")]
    [InlineData("as needed")]
    [InlineData("before meals")]
    [InlineData("1-0-1")]        // dash dialect: real, but not recognised in v1 — manual
    [InlineData("1+0")]          // two positions is not a pattern we know
    [InlineData("1+0+1+1+1")]    // five is not either
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Unrecognised_frequency_falls_back_to_manual(string? frequency)
    {
        Assert.False(MarSchedule.TryExpand(frequency, "3 days", Day1, null, out _, out _),
            $"'{frequency}' must be rejected — a guessed drug time is a clinical error, "
            + "a manual fallback is a minor chore.");
    }

    // ----- duration --------------------------------------------------------------------------

    [Theory]
    [InlineData("7 days", 7)]
    [InlineData("3", 3)]
    [InlineData(null, 3)]        // prescriber left it blank: default course
    [InlineData("", 3)]
    [InlineData("till review", 3)]
    [InlineData("45 days", 10)]  // clamp: rows on a 3 GB box, and regeneration is cheap
    [InlineData("0 days", 1)]
    public void Duration_takes_the_first_integer_with_default_and_clamp(
        string? duration, int expectedDays)
    {
        Assert.True(MarSchedule.TryExpand("1+0+0", duration, Day1, null, out var slots, out var days));
        Assert.Equal(expectedDays, days);
        Assert.Equal(expectedDays, slots.Count);   // one slot per day makes the count the days
    }

    // ----- day-1 truncation: no dose is born overdue -----------------------------------------

    [Fact]
    public void Slots_already_past_on_the_first_day_are_dropped()
    {
        var slots = Expand("1+1+1", "2 days", notBefore: new TimeOnly(14, 30));
        // Day 1 keeps only 20:00; day 2 keeps all three.
        Assert.Equal(4, slots.Count);
        Assert.Equal(new[] { new TimeOnly(20, 0) },
            slots.Where(s => s.Day == Day1).Select(s => s.Time).ToArray());
        Assert.Equal(3, slots.Count(s => s.Day == Day1.AddDays(1)));
    }

    [Fact]
    public void A_slot_exactly_at_notBefore_is_dropped_too()
    {
        // <= not <: a dose generated for this very minute is already due, and "due at the
        // moment of creation" is exactly the born-overdue state the rule exists to prevent.
        var slots = Expand("1+1+1", "1 day", notBefore: new TimeOnly(8, 0));
        Assert.DoesNotContain(slots, s => s.Time == new TimeOnly(8, 0));
        Assert.Equal(2, slots.Count);
    }

    [Fact]
    public void Truncation_only_applies_to_the_first_day()
    {
        var slots = Expand("1+0+1", "3 days", notBefore: new TimeOnly(23, 59));
        // Day 1 fully dropped, days 2–3 intact.
        Assert.Equal(4, slots.Count);
        Assert.DoesNotContain(slots, s => s.Day == Day1);
    }

    // ----- whitespace and case tolerance -----------------------------------------------------

    [Theory]
    [InlineData(" 1 + 0 + 1 ")]
    [InlineData("8 HOURLY")]
    [InlineData("8  hourly")]
    public void Operator_typing_noise_is_tolerated(string frequency)
    {
        Assert.True(MarSchedule.TryExpand(frequency, "1 day", Day1, null, out var slots, out _),
            $"'{frequency}' differs from a recognised pattern only by spacing or case.");
        Assert.NotEmpty(slots);
    }

    // ----- overdue: the LC-NUR-06 computation ------------------------------------------------

    private static readonly DateTimeOffset ScheduledAt =
        new(2026, 8, 3, 4, 0, 0, TimeSpan.Zero);   // 10:00 Dhaka, stored UTC

    [Fact]
    public void A_scheduled_dose_past_grace_is_overdue()
    {
        Assert.True(MarSchedule.IsOverdue(
            DoseState.Scheduled, ScheduledAt, ScheduledAt.AddMinutes(31)));
    }

    [Fact]
    public void The_grace_boundary_itself_is_not_overdue()
    {
        // "older than 30 minutes", strictly: at exactly 30 the nurse is still inside the
        // window, and a badge that flickers on at second zero reads as an accusation.
        Assert.False(MarSchedule.IsOverdue(
            DoseState.Scheduled, ScheduledAt, ScheduledAt.AddMinutes(30)));
    }

    [Theory]
    [InlineData(DoseState.Given)]
    [InlineData(DoseState.Missed)]
    [InlineData(DoseState.Refused)]
    public void A_recorded_dose_is_never_overdue(string state)
    {
        // Missed is already visible as missed; overdue is only the not-yet-actioned surface.
        Assert.False(MarSchedule.IsOverdue(state, ScheduledAt, ScheduledAt.AddHours(6)));
    }
}
