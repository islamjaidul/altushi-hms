using System.Globalization;
using Hms.Emr.Data;

namespace Hms.Emr;

/// <summary>
/// Spec 0041: turns a prescription's free-text frequency and duration into dated dose slots for
/// the Medicine Chart. Pure — no clock, no database — so the arithmetic can be pinned by
/// <c>MarScheduleTests</c> without a fixture (the spec 0039 lesson about untested arithmetic).
///
/// The governing rule is conservative: this class recognises the two dialects a Bangladeshi
/// prescription actually uses, and returns <c>false</c> for everything else so the caller can
/// tell the nurse to schedule those lines by hand. Guessing at a drug time is a clinical error;
/// asking for a manual entry is a chore.
///
/// Slot times are a clinical convention, not a regulation — they live here as constants
/// precisely so a ward sister's correction is a one-line change (open question in the spec).
/// </summary>
public static class MarSchedule
{
    /// <summary>"1+1+1" — morning, midday, night.</summary>
    private static readonly TimeOnly[] ThreeSlots = [new(8, 0), new(13, 0), new(20, 0)];

    /// <summary>"1+1+1+1" — the four-times-a-day ward round.</summary>
    private static readonly TimeOnly[] FourSlots = [new(8, 0), new(12, 0), new(16, 0), new(20, 0)];

    /// <summary>"N hourly" walks from the morning drug round.</summary>
    private static readonly TimeOnly HourlyStart = new(6, 0);

    /// <summary>Intervals a ward actually runs; 5-hourly is a typo, not a prescription.</summary>
    private static readonly int[] HourlyIntervals = [4, 6, 8, 12, 24];

    /// <summary>Duration the prescriber left blank — a short course, regenerated if extended.</summary>
    public const int DefaultDays = 3;

    /// <summary>
    /// Horizon cap. Two reasons: rows on a 2 vCPU / 3 GB box (§16), and a schedule further out
    /// than this is a guess about a stay whose end nobody knows yet. Regeneration is idempotent,
    /// so extending a course costs one button press.
    /// </summary>
    public const int MaxDays = 10;

    /// <summary>
    /// How late a dose may run before the board calls it overdue. A badge that lights up on the
    /// scheduled second reads as an accusation on a ward where rounds legitimately drift.
    /// </summary>
    public static readonly TimeSpan OverdueGrace = TimeSpan.FromMinutes(30);

    private static readonly TimeZoneInfo Dhaka = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    /// <summary>
    /// Expands one prescription line into wall-clock slots. Returns false when the frequency is
    /// not one this class recognises — the caller then leaves that drug for manual scheduling.
    /// </summary>
    /// <param name="notBefore">
    /// Wall-clock time on <paramref name="firstDay"/> that slots must fall after. Day one drops
    /// everything at or before it, so generation never creates a dose that is already overdue.
    /// </param>
    public static bool TryExpand(
        string? frequency, string? duration, DateOnly firstDay, TimeOnly? notBefore,
        out IReadOnlyList<(DateOnly Day, TimeOnly Time)> slots, out int days)
    {
        slots = [];
        days = ParseDays(duration);

        if (!TryDailySlots(frequency, out var daily)) return false;

        var expanded = new List<(DateOnly, TimeOnly)>();
        for (var dayOffset = 0; dayOffset < days; dayOffset++)
        {
            var day = firstDay.AddDays(dayOffset);
            foreach (var time in daily)
            {
                if (dayOffset == 0 && notBefore is not null && time <= notBefore) continue;
                expanded.Add((day, time));
            }
        }

        slots = expanded;
        return true;
    }

    /// <summary>
    /// Whether a dose the ward has not acted on is now late. Computed at render time from the
    /// row and the clock — there is no background worker writing an "overdue" state, because a
    /// state written by a machine into a clinical record is a machine's opinion about a patient.
    /// </summary>
    public static bool IsOverdue(string state, DateTimeOffset scheduledAt, DateTimeOffset nowUtc)
        => state == DoseState.Scheduled && nowUtc - scheduledAt > OverdueGrace;

    /// <summary>The Dhaka wall-clock day and time of a UTC instant (operators read wall clock).</summary>
    public static (DateOnly Day, TimeOnly Time) InDhaka(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Dhaka).DateTime;
        return (DateOnly.FromDateTime(local), TimeOnly.FromDateTime(local));
    }

    /// <summary>A Dhaka wall-clock slot as the UTC instant Npgsql will store (ADR-0004).</summary>
    public static DateTimeOffset ToUtc(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time);
        return new DateTimeOffset(local, Dhaka.GetUtcOffset(local)).ToUniversalTime();
    }

    // ---- parsing ---------------------------------------------------------------

    private static bool TryDailySlots(string? frequency, out TimeOnly[] slots)
    {
        slots = [];
        if (string.IsNullOrWhiteSpace(frequency)) return false;
        var text = frequency.Trim();

        // "N hourly"
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 2 && words[1].Equals("hourly", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(words[0], NumberStyles.None, CultureInfo.InvariantCulture, out var every)
            && HourlyIntervals.Contains(every))
        {
            var times = new List<TimeOnly>();
            for (var hour = HourlyStart.Hour; hour < 24; hour += every)
                times.Add(new TimeOnly(hour, 0));
            slots = [.. times];
            return true;
        }

        // "1+0+1" / "1+1+1+1" — position decides the slot, the count decides whether it exists.
        // A count above one is still one dose at that slot: two tablets at 8 a.m. is one
        // administration, and the dose text on the row already says how many.
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        var pattern = parts.Length == 3 ? ThreeSlots : parts.Length == 4 ? FourSlots : null;
        if (pattern is null) return false;

        var chosen = new List<TimeOnly>();
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
                return false;                                  // "1+x+1" is not a frequency
            if (count > 0) chosen.Add(pattern[i]);
        }

        slots = [.. chosen];
        return true;                                           // "0+0+0" is valid and empty
    }

    /// <summary>First integer in the duration text, defaulted and clamped to the horizon.</summary>
    private static int ParseDays(string? duration)
    {
        if (string.IsNullOrWhiteSpace(duration)) return DefaultDays;

        var digits = new string([.. duration.SkipWhile(c => !char.IsAsciiDigit(c))
            .TakeWhile(char.IsAsciiDigit)]);
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var days))
            return DefaultDays;                                // "till review" — a short course

        return Math.Clamp(days, 1, MaxDays);
    }
}
