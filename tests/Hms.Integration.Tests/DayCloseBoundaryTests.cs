using Hms.Billing;
using Hms.Billing.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0027. The day-close staleness guard compared a **Dhaka** business day against a **UTC**
/// calendar date, so every session opened between 00:00 and 06:00 Asia/Dhaka looked stale and
/// demanded a supervised carry-close. A night shift closing its own drawer was told to fetch a
/// supervisor — and the suite that should have caught it was itself only green for eighteen
/// hours a day, because it used the real clock.
///
/// These tests use a **fixed** clock parked inside that window. A defect that only appears at
/// certain hours has to be tested at those hours deliberately, or it is not tested at all.
/// </summary>
[Collection("postgres")]
public class DayCloseBoundaryTests : IAsyncLifetime
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>01:30 Asia/Dhaka on 2026-07-28 — i.e. 19:30 UTC on 2026-07-27.</summary>
    private static readonly DateTimeOffset NightShift =
        new(2026, 7, 27, 19, 30, 0, TimeSpan.Zero);

    private readonly PostgresFixture _pg;
    private readonly FixedClock _clock = new(NightShift);
    private readonly BusinessDayCalendar _businessDay = new(TimeOnly.MinValue);

    public DayCloseBoundaryTests(PostgresFixture pg) => _pg = pg;

    private BillingService Billing() => new(
        new NumberSeriesService(), new AuditWriter(_clock), new FiscalCalendar(7),
        _businessDay, _clock);

    private DayCloseService DayClose() => new(new AuditWriter(_clock), _businessDay, _clock);

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var bill = CreateBill(conn);
        await bill.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private BillDbContext CreateBill(NpgsqlConnection conn) => new(
        new DbContextOptionsBuilder<BillDbContext>().UseNpgsql(conn,
            o => o.MigrationsHistoryTable("__ef_migrations", "bill"))
        .UseSnakeCaseNamingConvention().Options);

    private KernelDbContext CreateKernel(NpgsqlConnection conn) => new(
        new DbContextOptionsBuilder<KernelDbContext>().UseNpgsql(conn,
            o => o.MigrationsHistoryTable("__ef_migrations", "kernel"))
        .UseSnakeCaseNamingConvention().Options);

    private async Task<T> InTxAsync<T>(Func<BillDbContext, KernelDbContext, Task<T>> body)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var bill = CreateBill(conn);
        await using var kernel = CreateKernel(conn);
        await bill.Database.UseTransactionAsync(tx);
        await kernel.Database.UseTransactionAsync(tx);
        var result = await body(bill, kernel);
        await tx.CommitAsync();
        return result;
    }

    private static long _counter = 7_700;
    private static long NextCounter() => Interlocked.Increment(ref _counter);

    [Fact]
    public async Task The_business_day_at_half_past_one_in_the_morning_is_that_morning()
    {
        // The premise the guard has to respect: 01:30 Dhaka on the 28th belongs to the 28th,
        // even though the UTC date is still the 27th.
        Assert.Equal(new DateOnly(2026, 7, 28), _businessDay.BusinessDayOf(NightShift));
        Assert.Equal(new DateOnly(2026, 7, 27), DateOnly.FromDateTime(NightShift.UtcDateTime));
    }

    [Fact]
    public async Task A_session_opened_on_the_night_shift_closes_without_an_approval()
    {
        var counterId = NextCounter();
        var sessionId = await InTxAsync(async (bill, _) =>
        {
            var session = await Billing().OpenSessionAsync(bill, 1, counterId, 3, 1000);
            return session.Id;
        });

        _clock.Now = NightShift.AddMinutes(30);          // 02:00 Dhaka, same business day

        var summary = await InTxAsync(async (bill, kernel) =>
            await DayClose().CloseAsync(bill, kernel, sessionId, 1000, 3, "Night operator"));

        Assert.Equal(new DateOnly(2026, 7, 28), summary.BusinessDay);
        Assert.Equal(0, summary.Variance);
    }

    [Fact]
    public async Task A_session_from_yesterday_still_needs_the_carry_close()
    {
        var counterId = NextCounter();
        _clock.Now = NightShift.AddDays(-1);             // opened the previous business day
        var sessionId = await InTxAsync(async (bill, _) =>
        {
            var session = await Billing().OpenSessionAsync(bill, 1, counterId, 3, 1000);
            return session.Id;
        });

        _clock.Now = NightShift;                          // 01:30 Dhaka, the next business day

        var refused = await Assert.ThrowsAsync<BillingException>(() => InTxAsync(
            async (bill, kernel) =>
                await DayClose().CloseAsync(bill, kernel, sessionId, 1000, 3, "Night operator")));
        Assert.Contains("carry-close", refused.Message);
    }
}
