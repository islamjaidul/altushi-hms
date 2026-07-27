using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Hms.Ot;
using Hms.Ot.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0025 (§5 M7, §11 OT Case). US7.1's "double-booking a theatre is impossible" is a claim
/// about the database, so it is tested against one — as are the state machine's ordering and
/// the refusal to complete a case twice (which would bill the operation twice).
/// </summary>
[Collection("postgres")]
public class OtTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly OtService _ot = new(
        new NumberSeriesService(), new AuditWriter(TimeProvider.System),
        new FiscalCalendar(7), TimeProvider.System);

    public OtTests(PostgresFixture pg) => _pg = pg;

    private OtDbContext CreateOt(NpgsqlConnection? conn = null) => new(
        (conn is null
            ? new DbContextOptionsBuilder<OtDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "ot"))
            : new DbContextOptionsBuilder<OtDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "ot")))
        .UseSnakeCaseNamingConvention().Options);

    private KernelDbContext CreateKernel(NpgsqlConnection conn) => new(
        new DbContextOptionsBuilder<KernelDbContext>().UseNpgsql(conn,
            o => o.MigrationsHistoryTable("__ef_migrations", "kernel"))
        .UseSnakeCaseNamingConvention().Options);

    /// <summary>ot + kernel on ONE connection and transaction (G19) — case numbers are issued
    /// under the caller's transaction, exactly as every other document number is.</summary>
    private async Task<T> InTxAsync<T>(Func<OtDbContext, KernelDbContext, Task<T>> body)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var ot = CreateOt(conn);
        await using var kernel = CreateKernel(conn);
        await ot.Database.UseTransactionAsync(tx);
        await kernel.Database.UseTransactionAsync(tx);
        var result = await body(ot, kernel);
        await tx.CommitAsync();
        return result;
    }

    private Task InTxAsync(Func<OtDbContext, KernelDbContext, Task> body)
        => InTxAsync<object?>(async (ot, kernel) => { await body(ot, kernel); return null; });

    private long _theatreA, _theatreB;

    public async Task InitializeAsync()
    {
        await using var ot = CreateOt();
        await ot.Database.MigrateAsync();

        var a = new Theatre { BranchId = 1, Name = $"T-A-{Guid.NewGuid():N}" };
        var b = new Theatre { BranchId = 1, Name = $"T-B-{Guid.NewGuid():N}" };
        ot.Theatres.AddRange(a, b);
        await ot.SaveChangesAsync();
        _theatreA = a.Id;
        _theatreB = b.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static readonly DateTimeOffset Base =
        new(2027, 3, 1, 3, 0, 0, TimeSpan.Zero);      // 09:00 Dhaka, well clear of other tests

    private static long _slot;
    /// <summary>A window nobody else in this class is using.</summary>
    private static (DateTimeOffset From, DateTimeOffset To) NextSlot()
    {
        var offset = Interlocked.Increment(ref _slot) * 4;
        var from = Base.AddHours(offset);
        return (from, from.AddMinutes(90));
    }

    private static TeamSlot Surgeon(long personId = 7) =>
        new(TeamRole.Surgeon, personId, "Dr. Test", 101);

    /// <summary>Scheduling issues a case number, so it runs inside a transaction (G19).</summary>
    private Task<OtCase> ScheduleAsync(
        long theatreId, (DateTimeOffset From, DateTimeOffset To) slot, long surgeonId = 7)
        => InTxAsync(async (ot, kernel) => await _ot.ScheduleAsync(ot, kernel, 1, 55, 900, null,
            theatreId, 500, "Appendicectomy", slot.From, slot.To, [Surgeon(surgeonId)], 7,
            "OT In-charge"));

    [Fact]
    public async Task A_case_belongs_to_one_parent_and_needs_a_surgeon()
    {
        var slot = NextSlot();
        await InTxAsync(async (ot, kernel) =>
        {
            await Assert.ThrowsAsync<OtException>(() => _ot.ScheduleAsync(ot, kernel, 1, 55, 900, 7,
                _theatreA, 500, "X", slot.From, slot.To, [Surgeon()], 7, "OT"));
            await Assert.ThrowsAsync<OtException>(() => _ot.ScheduleAsync(ot, kernel, 1, 55, null,
                null, _theatreA, 500, "X", slot.From, slot.To, [Surgeon()], 7, "OT"));
            await Assert.ThrowsAsync<OtException>(() => _ot.ScheduleAsync(ot, kernel, 1, 55, 900,
                null, _theatreA, 500, "X", slot.From, slot.To, [], 7, "OT"));
        });
    }

    [Fact]
    public async Task An_overlapping_booking_in_the_same_theatre_is_refused()
    {
        var slot = NextSlot();
        var booked = await ScheduleAsync(_theatreA, slot);

        var overlap = (slot.From.AddMinutes(30), slot.To.AddMinutes(30));
        var clash = await Assert.ThrowsAsync<OtException>(() => ScheduleAsync(_theatreA, overlap));
        Assert.Contains(booked.CaseNo, clash.Message);
    }

    [Fact]
    public async Task The_same_surgeon_in_two_theatres_at_once_is_refused()
    {
        var slot = NextSlot();
        await ScheduleAsync(_theatreA, slot, surgeonId: 4242);

        var overlap = (slot.From.AddMinutes(30), slot.To.AddMinutes(30));
        var clash = await Assert.ThrowsAsync<OtException>(
            () => ScheduleAsync(_theatreB, overlap, surgeonId: 4242));
        Assert.Contains("already in", clash.Message);
    }

    [Fact]
    public async Task A_cancelled_case_frees_its_slot()
    {
        await using var ot = CreateOt();
        await using var kernel = _pg.CreateKernelContext();
        var slot = NextSlot();
        var first = await ScheduleAsync(_theatreA, slot);

        await _ot.CancelAsync(ot, kernel, first.Id, 1, "Patient unfit", 7, "OT In-charge");

        // The same window is bookable again — a cancelled case must not hold a theatre hostage.
        var second = await ScheduleAsync(_theatreA, slot);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task The_state_machine_runs_in_order()
    {
        await using var ot = CreateOt();
        await using var kernel = _pg.CreateKernelContext();
        var otCase = await ScheduleAsync(_theatreA, NextSlot());

        // Straight to theatre without being sent for: refused.
        await Assert.ThrowsAsync<OtException>(() => _ot.StartAsync(ot, otCase.Id));
        // Completing a case that never started: refused.
        await Assert.ThrowsAsync<OtException>(() => _ot.CompleteAsync(
            ot, kernel, otCase.Id, 1, null, "Something", null, 7, "OT"));

        await _ot.MarkPatientReadyAsync(ot, otCase.Id);
        await _ot.StartAsync(ot, otCase.Id);
        await _ot.CompleteAsync(ot, kernel, otCase.Id, 1, "Nil", "Appendicectomy", "spinal", 7, "OT");

        var stored = await ot.Cases.AsNoTracking().SingleAsync(c => c.Id == otCase.Id);
        Assert.Equal(CaseState.Completed, stored.State);
        Assert.NotNull(stored.StartedAt);
        Assert.NotNull(stored.FinishedAt);
    }

    [Fact]
    public async Task Completing_twice_is_refused_so_nothing_bills_twice()
    {
        await using var ot = CreateOt();
        await using var kernel = _pg.CreateKernelContext();
        var otCase = await ScheduleAsync(_theatreA, NextSlot());
        await _ot.MarkPatientReadyAsync(ot, otCase.Id);
        await _ot.StartAsync(ot, otCase.Id);
        await _ot.CompleteAsync(ot, kernel, otCase.Id, 1, null, "Done", null, 7, "OT");

        var again = await Assert.ThrowsAsync<OtException>(() => _ot.CompleteAsync(
            ot, kernel, otCase.Id, 1, null, "Done again", null, 7, "OT"));
        Assert.Contains("not in theatre", again.Message);
    }

    [Fact]
    public async Task A_case_in_theatre_cannot_be_cancelled()
    {
        await using var ot = CreateOt();
        await using var kernel = _pg.CreateKernelContext();
        var otCase = await ScheduleAsync(_theatreA, NextSlot());
        await _ot.MarkPatientReadyAsync(ot, otCase.Id);
        await _ot.StartAsync(ot, otCase.Id);

        await Assert.ThrowsAsync<OtException>(() =>
            _ot.CancelAsync(ot, kernel, otCase.Id, 1, "changed our mind", 7, "OT"));
    }

    [Fact]
    public async Task Cancelling_and_postponing_both_need_a_reason()
    {
        await using var ot = CreateOt();
        await using var kernel = _pg.CreateKernelContext();
        var otCase = await ScheduleAsync(_theatreA, NextSlot());

        await Assert.ThrowsAsync<OtException>(() =>
            _ot.CancelAsync(ot, kernel, otCase.Id, 1, "  ", 7, "OT"));
        await Assert.ThrowsAsync<OtException>(() =>
            _ot.PostponeAsync(ot, kernel, otCase.Id, 1, "", 7, "OT"));
    }

    [Fact]
    public async Task Completion_needs_a_record_of_what_was_done()
    {
        await using var ot = CreateOt();
        await using var kernel = _pg.CreateKernelContext();
        var otCase = await ScheduleAsync(_theatreA, NextSlot());
        await _ot.MarkPatientReadyAsync(ot, otCase.Id);
        await _ot.StartAsync(ot, otCase.Id);

        await Assert.ThrowsAsync<OtException>(() => _ot.CompleteAsync(
            ot, kernel, otCase.Id, 1, "Findings only", null, null, 7, "OT"));
    }

    [Fact]
    public async Task A_backwards_window_is_refused()
    {
        var slot = NextSlot();
        await InTxAsync(async (ot, kernel) =>
            await Assert.ThrowsAsync<OtException>(() => _ot.ScheduleAsync(ot, kernel, 1, 55, 900,
                null, _theatreA, 500, "X", slot.To, slot.From, [Surgeon()], 7, "OT")));
    }
}
