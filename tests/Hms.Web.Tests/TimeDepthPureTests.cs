using Hms.Hr;
using Hms.Hr.Data;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0058 — roster pattern expansion (module PRD §7.5, G27). Pure, so the arithmetic that decides
/// which of forty nurses works which night is pinned without a database.
/// </summary>
public class RosterPatternTests
{
    private static readonly DateOnly Start = new(2026, 3, 1);

    /// <summary>"6 mornings, 1 off, 6 evenings, 1 off" — the shape §5 M16's rotating shifts take.</summary>
    private static IReadOnlyList<(long? ShiftId, int Days)> Cycle() =>
        [(1L, 6), (null, 1), (2L, 6), (null, 1)];

    [Fact]
    public void A_single_person_walks_the_cycle_in_order()
    {
        var plan = RosterPlanService.Expand([100], Cycle(), Start, Start.AddDays(13));

        Assert.Equal(14, plan.Count);
        Assert.All(plan.Take(6), p => Assert.Equal(1L, p.ShiftId));
        Assert.Null(plan[6].ShiftId);
        Assert.All(plan.Skip(7).Take(6), p => Assert.Equal(2L, p.ShiftId));
        Assert.Null(plan[13].ShiftId);
    }

    [Fact]
    public void The_cycle_repeats_past_its_own_length()
    {
        var plan = RosterPlanService.Expand([100], Cycle(), Start, Start.AddDays(27));

        Assert.Equal(28, plan.Count);
        // Day 14 is day 0 again.
        Assert.Equal(plan[0].ShiftId, plan[14].ShiftId);
        Assert.Equal(plan[6].ShiftId, plan[20].ShiftId);
    }

    /// <summary>
    /// The point of a rotation: staggered by whole steps, not single days. Two nurses one day apart
    /// on a six-day block is the same block twice, not a rotation.
    /// </summary>
    [Fact]
    public void Each_person_starts_a_different_step_of_the_cycle()
    {
        var plan = RosterPlanService.Expand([100, 200, 300, 400], Cycle(), Start, Start);

        var day = plan.Where(p => p.OnDate == Start).ToList();
        Assert.Equal(4, day.Count);
        Assert.Equal(1L, day[0].ShiftId);      // offset 0  — morning block
        Assert.Null(day[1].ShiftId);           // offset 6  — the rest day
        Assert.Equal(2L, day[2].ShiftId);      // offset 7  — evening block
        Assert.Null(day[3].ShiftId);           // offset 13 — the second rest day
    }

    [Fact]
    public void More_people_than_steps_wraps_round_the_cycle()
    {
        var plan = RosterPlanService.Expand([1, 2, 3, 4, 5], Cycle(), Start, Start);

        var day = plan.Where(p => p.OnDate == Start).ToList();
        // The fifth person starts where the first did.
        Assert.Equal(day[0].ShiftId, day[4].ShiftId);
    }

    [Fact]
    public void Every_person_gets_every_day_of_the_range()
    {
        var plan = RosterPlanService.Expand([1, 2, 3], Cycle(), Start, Start.AddDays(9));

        Assert.Equal(30, plan.Count);
        foreach (var person in new long[] { 1, 2, 3 })
            Assert.Equal(10, plan.Count(p => p.EmployeeId == person));
    }

    [Theory]
    [InlineData(0)]     // nobody
    [InlineData(-1)]    // an inverted range
    public void Nothing_expands_to_nothing(int shape)
    {
        var plan = shape == 0
            ? RosterPlanService.Expand([], Cycle(), Start, Start.AddDays(6))
            : RosterPlanService.Expand([1], Cycle(), Start, Start.AddDays(-1));

        Assert.Empty(plan);
    }

    [Fact]
    public void An_empty_cycle_expands_to_nothing_rather_than_dividing_by_zero()
    {
        Assert.Empty(RosterPlanService.Expand([1], [], Start, Start.AddDays(6)));
    }

    [Fact]
    public void A_single_step_cycle_is_the_same_shift_every_day()
    {
        var plan = RosterPlanService.Expand([1, 2], [(7L, 1)], Start, Start.AddDays(4));

        Assert.Equal(10, plan.Count);
        Assert.All(plan, p => Assert.Equal(7L, p.ShiftId));
    }
}

/// <summary>Spec 0058 — the overtime bank (module PRD §7.4, G25).</summary>
public class OvertimeBankTests
{
    [Fact]
    public void A_balance_is_credits_less_debits_less_expiries()
    {
        var balance = TimeRequestService.Balance(
        [
            (OvertimeBankKind.Credit, 480),
            (OvertimeBankKind.Credit, 120),
            (OvertimeBankKind.Debit, 240),
            (OvertimeBankKind.Expiry, 60),
        ]);

        Assert.Equal(300, balance);
    }

    [Fact]
    public void No_movements_is_no_balance()
    {
        Assert.Equal(0, TimeRequestService.Balance([]));
    }

    [Fact]
    public void Spending_everything_lands_on_zero()
    {
        Assert.Equal(0, TimeRequestService.Balance(
            [(OvertimeBankKind.Credit, 480), (OvertimeBankKind.Debit, 480)]));
    }
}

/// <summary>Spec 0058 — device health (module PRD §7.4, G22, §13 I8).</summary>
public class DeviceHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_device_never_heard_from_says_so()
    {
        Assert.Equal(DeviceHealth.NeverHeard,
            AttendanceDevice.HealthOf(null, 60, Now));
    }

    [Fact]
    public void Reporting_inside_its_interval_is_healthy()
    {
        Assert.Equal(DeviceHealth.Healthy,
            AttendanceDevice.HealthOf(Now.AddMinutes(-30), 60, Now));
    }

    [Fact]
    public void Exactly_one_interval_is_still_healthy()
    {
        // The boundary is inclusive: a clock reporting exactly on time is on time.
        Assert.Equal(DeviceHealth.Healthy,
            AttendanceDevice.HealthOf(Now.AddMinutes(-60), 60, Now));
    }

    [Fact]
    public void Past_its_interval_but_inside_two_is_late_not_silent()
    {
        // The difference between a quiet night shift and an unplugged clock.
        Assert.Equal(DeviceHealth.Late,
            AttendanceDevice.HealthOf(Now.AddMinutes(-90), 60, Now));
    }

    [Fact]
    public void Beyond_two_intervals_is_silent()
    {
        Assert.Equal(DeviceHealth.Silent,
            AttendanceDevice.HealthOf(Now.AddHours(-5), 60, Now));
    }

    [Fact]
    public void A_zero_interval_does_not_make_everything_silent()
    {
        // The constraint refuses zero, but the function is called from a report over data that
        // predates it, so it clamps rather than dividing the world into "always broken".
        Assert.Equal(DeviceHealth.Healthy,
            AttendanceDevice.HealthOf(Now, 0, Now));
    }
}

/// <summary>Spec 0058 — coverage arithmetic (module PRD §7.5).</summary>
public class CoverageTests
{
    private static CoverageCell Cell(int required, int rostered, int onLeave) =>
        new(new DateOnly(2026, 3, 10), 1, "Morning", required, rostered, onLeave);

    [Fact]
    public void Available_is_rostered_less_those_on_leave()
    {
        Assert.Equal(3, Cell(required: 4, rostered: 5, onLeave: 2).Available);
    }

    [Fact]
    public void A_shift_is_short_when_the_available_do_not_meet_the_requirement()
    {
        var cell = Cell(required: 4, rostered: 5, onLeave: 2);

        Assert.True(cell.IsShort);
        Assert.Equal(1, cell.Short);
    }

    [Fact]
    public void Somebody_on_approved_leave_does_not_count_as_cover()
    {
        // Five rostered against a requirement of four looks covered until you notice two are away.
        var naive = Cell(required: 4, rostered: 5, onLeave: 0);
        var real = Cell(required: 4, rostered: 5, onLeave: 2);

        Assert.False(naive.IsShort);
        Assert.True(real.IsShort);
    }

    [Fact]
    public void No_requirement_configured_claims_no_shortfall()
    {
        // Asserting a shortfall against a number nobody set would be inventing the number.
        var cell = Cell(required: 0, rostered: 0, onLeave: 0);

        Assert.False(cell.IsShort);
        Assert.Equal(0, cell.Short);
    }

    [Fact]
    public void Exactly_meeting_the_requirement_is_covered()
    {
        Assert.False(Cell(required: 4, rostered: 4, onLeave: 0).IsShort);
    }
}

/// <summary>
/// Spec 0058 — the device push key (module PRD §7.4, G22). A machine credential, not a password:
/// the operator never types it, so it has no human-memorable entropy to protect and no login rate
/// to slow down. What it must do is verify in fixed time and not reveal that two branches share one.
/// </summary>
public class DeviceKeyTests
{
    [Fact]
    public void A_key_verifies_against_its_own_hash()
    {
        var hash = DeviceService.HashKey("a-very-long-machine-key");

        Assert.True(DeviceService.Verify("a-very-long-machine-key", hash));
    }

    [Fact]
    public void A_wrong_key_does_not()
    {
        var hash = DeviceService.HashKey("a-very-long-machine-key");

        Assert.False(DeviceService.Verify("a-very-long-machine-kex", hash));
        Assert.False(DeviceService.Verify("", hash));
    }

    [Fact]
    public void The_same_key_hashes_differently_every_time()
    {
        // The salt is what stops one employer's stolen database revealing that two branches
        // configured the same key into two clocks.
        var first = DeviceService.HashKey("identical-machine-key-here");
        var second = DeviceService.HashKey("identical-machine-key-here");

        Assert.NotEqual(first, second);
        Assert.True(DeviceService.Verify("identical-machine-key-here", first));
        Assert.True(DeviceService.Verify("identical-machine-key-here", second));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-colon-in-here")]
    [InlineData("not-base64:also-not-base64")]
    [InlineData("::")]
    public void A_malformed_stored_hash_refuses_rather_than_throwing(string stored)
    {
        // Whatever is in that column, a device gets a clean refusal — never a 500 that tells it
        // something about the shape of the store.
        Assert.False(DeviceService.Verify("anything", stored));
    }
}
