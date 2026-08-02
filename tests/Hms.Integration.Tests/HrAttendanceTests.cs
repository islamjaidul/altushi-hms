using System.Text;
using Hms.Hr;
using Hms.Hr.Data;
using Hms.Hr.Web;
using Hms.Kernel.Audit;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Hms.Integration.Tests;

/// <summary>
/// The punch pipeline, executed against product-shaped input for the first time (AUD-M16-08:
/// <c>ImportAsync</c> and <c>DeriveDayAsync</c> had zero callers, so punch pairing, night-shift
/// spanning, break deduction and import idempotency were unproven, not merely untested). Times in
/// the fixtures are Dhaka wall-clock exactly as an operator's file carries them; Asia/Dhaka has no
/// DST, so the +06:00 conversion inside <c>CsvPunchSource</c> is a constant.
/// </summary>
[Collection("postgres")]
public sealed class HrAttendanceTests(PostgresFixture pg)
{
    private static long _branchSeed = 6200;
    private static long NextBranch() => Interlocked.Increment(ref _branchSeed);

    private AttendanceService Service() => new(new AuditWriter(TimeProvider.System), TimeProvider.System);

    [Fact]
    public async Task Punches_pair_into_a_present_day_and_the_break_is_deducted()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var svc = Service();
        var (employeeId, code) = await SeedEmployeeAsync(tx, branch);
        var day = new DateOnly(2026, 3, 10);
        await SeedRosterAsync(tx, branch, employeeId, day, Shift(branch, "GEN", new(9, 0), new(18, 0), false, 60));

        await ImportAsync(tx, svc, branch,
            $"{code}, 10/03/2026 09:00, in\n{code}, 10/03/2026 18:00, out\n");

        await using var hr = HrContext();
        var derived = await hr.AttendanceDays.SingleAsync(d => d.EmployeeId == employeeId && d.OnDate == day);
        Assert.Equal(AttendanceStatus.Present, derived.Status);
        Assert.Equal(480, derived.WorkedMinutes);              // 540 punched − 60 break
        Assert.Equal(0, derived.LateMinutes);
        Assert.Equal(10_000, derived.PayableFractionBp);
    }

    [Fact]
    public async Task A_late_arrival_and_a_late_departure_derive_late_and_overtime_minutes()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var svc = Service();
        var (employeeId, code) = await SeedEmployeeAsync(tx, branch);
        var day = new DateOnly(2026, 3, 11);
        await SeedRosterAsync(tx, branch, employeeId, day, Shift(branch, "GEN", new(9, 0), new(18, 0), false, 60));

        await ImportAsync(tx, svc, branch,
            $"{code}, 11/03/2026 09:25, in\n{code}, 11/03/2026 19:00, out\n");

        await using var hr = HrContext();
        var derived = await hr.AttendanceDays.SingleAsync(d => d.EmployeeId == employeeId && d.OnDate == day);
        Assert.Equal(25, derived.LateMinutes);
        Assert.Equal(60, derived.OvertimeMinutes);
    }

    [Fact]
    public async Task A_night_shift_day_belongs_to_the_shift_start_date_across_midnight()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var svc = Service();
        var (employeeId, code) = await SeedEmployeeAsync(tx, branch);
        var day = new DateOnly(2026, 3, 10);                   // shift 22:00 Tue → 06:00 Wed
        await SeedRosterAsync(tx, branch, employeeId, day, Shift(branch, "NIGHT", new(22, 0), new(6, 0), true, 30));

        await ImportAsync(tx, svc, branch,
            $"{code}, 10/03/2026 22:00, in\n{code}, 11/03/2026 06:00, out\n");

        await using var hr = HrContext();
        var derived = await hr.AttendanceDays
            .SingleAsync(d => d.EmployeeId == employeeId && d.OnDate == day);
        Assert.Equal(AttendanceStatus.Present, derived.Status);
        Assert.Equal(450, derived.WorkedMinutes);              // 480 spanning midnight − 30 break
        Assert.Equal(0, derived.LateMinutes);
        Assert.Equal(0, derived.OvertimeMinutes);
    }

    [Fact]
    public async Task A_morning_only_file_still_re_derives_the_previous_nights_shift()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var svc = Service();
        var (employeeId, code) = await SeedEmployeeAsync(tx, branch);
        var day = new DateOnly(2026, 3, 10);
        await SeedRosterAsync(tx, branch, employeeId, day, Shift(branch, "NIGHT", new(22, 0), new(6, 0), true, 30));

        // Evening file lands first: only the in-punch. The day derives Incomplete.
        await ImportAsync(tx, svc, branch, $"{code}, 10/03/2026 22:00, in\n");
        await using (var hr = HrContext())
        {
            var partial = await hr.AttendanceDays.SingleAsync(d => d.EmployeeId == employeeId && d.OnDate == day);
            Assert.Equal(AttendanceStatus.Incomplete, partial.Status);
        }

        // Next morning's file carries only the out-punch, dated the 11th. The batch derivation
        // must reach back to the 10th — the night shift that owns it — and complete the pair.
        await ImportAsync(tx, svc, branch, $"{code}, 11/03/2026 06:00, out\n");
        await using (var hr2 = HrContext())
        {
            var derived = await hr2.AttendanceDays.SingleAsync(d => d.EmployeeId == employeeId && d.OnDate == day);
            Assert.Equal(AttendanceStatus.Present, derived.Status);
            Assert.Equal(450, derived.WorkedMinutes);
        }
    }

    [Fact]
    public async Task Reimporting_the_same_file_is_a_reported_no_op()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var svc = Service();
        var (employeeId, code) = await SeedEmployeeAsync(tx, branch);
        var day = new DateOnly(2026, 3, 12);
        await SeedRosterAsync(tx, branch, employeeId, day, Shift(branch, "GEN", new(9, 0), new(18, 0), false, 60));
        var file = $"{code}, 12/03/2026 09:00, in\n{code}, 12/03/2026 18:00, out\n";

        var first = await ImportAsync(tx, svc, branch, file);
        Assert.Equal(2, first.Accepted);
        Assert.Equal(0, first.Duplicate);

        var second = await ImportAsync(tx, svc, branch, file);
        Assert.Equal(0, second.Accepted);
        Assert.Equal(2, second.Duplicate);                    // reported, not silently re-added

        await using var hr = HrContext();
        Assert.Equal(2, await hr.Punches.CountAsync(p => p.EmployeeId == employeeId));
        var derived = await hr.AttendanceDays.SingleAsync(d => d.EmployeeId == employeeId && d.OnDate == day);
        Assert.Equal(AttendanceStatus.Present, derived.Status);
        Assert.Equal(480, derived.WorkedMinutes);             // unchanged by the re-import
    }

    [Fact]
    public async Task A_single_punch_is_an_incomplete_day_that_pays_nothing_until_a_human_decides()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var svc = Service();
        var (employeeId, code) = await SeedEmployeeAsync(tx, branch);
        var day = new DateOnly(2026, 3, 13);
        await SeedRosterAsync(tx, branch, employeeId, day, Shift(branch, "GEN", new(9, 0), new(18, 0), false, 60));

        await ImportAsync(tx, svc, branch, $"{code}, 13/03/2026 09:00, in\n");

        await using var hr = HrContext();
        var derived = await hr.AttendanceDays.SingleAsync(d => d.EmployeeId == employeeId && d.OnDate == day);
        Assert.Equal(AttendanceStatus.Incomplete, derived.Status);
        Assert.Equal(0, derived.PayableFractionBp);
    }

    [Fact]
    public async Task An_unknown_code_and_a_garbled_line_are_rejected_with_a_count_not_dropped()
    {
        var branch = NextBranch();
        // Production scopes every context to the operator's branch claim (WP5); the
        // test does the same or the branch filter hides everything it seeds.
        BranchScope.Current = branch;
        var tx = await TxAsync();
        var svc = Service();
        var (_, code) = await SeedEmployeeAsync(tx, branch);

        var result = await ImportAsync(tx, svc, branch,
            "employee_code, punched_at, direction\n"          // header: skipped, not read
            + $"{code}, 14/03/2026 09:00, in\n"
            + "EMP-99999, 14/03/2026 09:00, in\n"             // nobody has this code
            + "garbage line without a timestamp\n");

        Assert.Equal(3, result.Read);
        Assert.Equal(1, result.Accepted);
        Assert.Equal(2, result.Rejected);
    }

    // ----------------------------------------------------------------------- plumbing
    private async Task<ImportResult> ImportAsync(HrTx tx, AttendanceService svc, long branch, string rows)
        => await tx.RunAsync(async s =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rows));
            var result = await svc.ImportAsync(
                s.Hr, s.Kernel, branch, "test.csv", new CsvPunchSource(), stream, 1, "test");
            await s.Hr.SaveChangesAsync();                     // punches down before derivation reads
            await svc.DeriveImportedDaysAsync(s.Hr, branch, result.BatchId);
            return result;
        });

    private static Shift Shift(
        long branch, string codePrefix, TimeOnly from, TimeOnly to, bool nextDay, int breakMinutes)
        => new()
        {
            BranchId = branch, Code = $"{codePrefix}-{branch}", Name = codePrefix,
            StartsAt = from, EndsAt = to, EndsNextDay = nextDay,
            BreakMinutes = breakMinutes, StandardMinutes = 480 - breakMinutes,
        };

    private async Task SeedRosterAsync(HrTx tx, long branch, long employeeId, DateOnly day, Shift shift)
        => await tx.RunAsync(async s =>
        {
            s.Hr.Shifts.Add(shift);
            var unit = new OrgUnit { BranchId = branch, Code = $"U-{branch}", Name = "Unit" };
            s.Hr.OrgUnits.Add(unit);
            await s.Hr.SaveChangesAsync();

            var roster = new Roster
            {
                BranchId = branch, OrgUnitId = unit.Id,
                FromDate = day.AddDays(-7), ToDate = day.AddDays(7), Published = true,
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.Rosters.Add(roster);
            await s.Hr.SaveChangesAsync();

            s.Hr.RosterEntries.Add(new RosterEntry
            {
                RosterId = roster.Id, EmployeeId = employeeId, OnDate = day, ShiftId = shift.Id,
            });
        });

    private async Task<(long Id, string Code)> SeedEmployeeAsync(HrTx tx, long branch)
        => await tx.RunAsync(async s =>
        {
            var employee = new Employee
            {
                BranchId = branch,
                EmployeeCode = $"A{branch}-001",
                FullName = "Attendance Subject",
                JoinedOn = new DateOnly(2025, 1, 1),
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Hr.Employees.Add(employee);
            await s.Hr.SaveChangesAsync();
            return (employee.Id, employee.EmployeeCode);
        });

    private async Task<HrTx> TxAsync()
    {
        await MigrateAsync();
        return new HrTx(Config());
    }

    private IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Hms"] = pg.ConnectionString,
        })
        .Build();

    private HrDbContext HrContext() => new(Options<HrDbContext>("hr"));

    private DbContextOptions<T> Options<T>(string schema) where T : DbContext
        => new DbContextOptionsBuilder<T>()
            .UseNpgsql(pg.ConnectionString, o => o.MigrationsHistoryTable("__ef_migrations", schema))
            .UseSnakeCaseNamingConvention()
            .Options;

    private async Task MigrateAsync()
    {
        await using var kernel = new KernelDbContext(Options<KernelDbContext>("kernel"));
        await kernel.Database.MigrateAsync();
        await using var auth = new AuthDbContext(Options<AuthDbContext>("adm"));
        await auth.Database.MigrateAsync();
        await using var hr = new HrDbContext(Options<HrDbContext>("hr"));
        await hr.Database.MigrateAsync();
    }
}
