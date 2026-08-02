using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// LC-XCUT-09 and LC-XCUT-10 — the two cross-cutting guarantees the QA pass found had no proof
/// anywhere in the repo (spec 0031, finding F7).
///
/// Both are Postgres guarantees, so both run against a real Postgres (G7). SQLite would happily
/// pass a lost-update test that Postgres exists to prevent, which is exactly why it is banned
/// for this class of test.
/// </summary>
[Collection("postgres")]
public class ConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly FolioService _folios = new(
        new AuditWriter(TimeProvider.System), new BusinessDayCalendar(TimeOnly.MinValue),
        TimeProvider.System);

    public ConcurrencyTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var ipd = CreateIpd(null);
        await ipd.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private IpdDbContext CreateIpd(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<IpdDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "ipd"))
            : new DbContextOptionsBuilder<IpdDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "ipd")))
        .UseSnakeCaseNamingConvention().Options);

    /// <summary>A folio, the admission it belongs to, and a bed — all this test's own.</summary>
    private async Task<(long FolioId, long AdmissionId, long BedId)> SeedFolioAsync()
    {
        await using var ipd = CreateIpd(null);
        var admission = await IpdSeed.OpenAdmissionAsync(ipd);
        var bed = await IpdSeed.BedAsync(ipd);
        var folio = new Folio
        {
            BranchId = 1, AdmissionId = admission.Id, PatientId = admission.PatientId,
        };
        ipd.Folios.Add(folio);
        await ipd.SaveChangesAsync();
        return (folio.Id, admission.Id, bed.Id);
    }

    private static BedDay Day(long admissionId, long bedId, int day) => new()
    {
        AdmissionId = admissionId, BedId = bedId, ChargeLineId = day,
        OnDate = new DateOnly(2026, 8, day),
    };

    // -----------------------------------------------------------------------
    // LC-XCUT-09 — a power cut mid-transaction leaves no half-write (PRD §8 N2)
    // -----------------------------------------------------------------------
    [Fact]
    public async Task A_transaction_that_dies_partway_leaves_nothing_behind()
    {
        var (_, admissionId, bedId) = await SeedFolioAsync();

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var ipd = CreateIpd(conn);

        // A unit of work that writes several rows and is then interrupted. At the database a
        // power cut is indistinguishable from this: the connection goes away with the
        // transaction still uncommitted.
        var tx = await conn.BeginTransactionAsync();
        await ipd.Database.UseTransactionAsync(tx);
        ipd.BedDays.Add(Day(admissionId, bedId, 1));
        ipd.BedDays.Add(Day(admissionId, bedId, 2));
        await ipd.SaveChangesAsync();

        // …and the lights go out before COMMIT.
        await tx.RollbackAsync();

        await using var after = CreateIpd(null);
        Assert.Empty(await after.BedDays.Where(b => b.AdmissionId == admissionId).ToListAsync());
    }

    [Fact]
    public async Task A_committed_transaction_survives_the_connection_going_away()
    {
        // The other half of N2: whatever was committed before the cut must still be there. A
        // rollback test on its own would pass on a database that lost everything.
        var (_, admissionId, bedId) = await SeedFolioAsync();

        await using (var conn = new NpgsqlConnection(_pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var ipd = CreateIpd(conn);
            var tx = await conn.BeginTransactionAsync();
            await ipd.Database.UseTransactionAsync(tx);
            ipd.BedDays.Add(Day(admissionId, bedId, 3));
            await ipd.SaveChangesAsync();
            await tx.CommitAsync();
        }

        await using var fresh = CreateIpd(null);
        Assert.Single(await fresh.BedDays.Where(b => b.AdmissionId == admissionId).ToListAsync());
    }

    // -----------------------------------------------------------------------
    // LC-XCUT-10 — two operators on one folio: one waits, neither charge is lost
    // -----------------------------------------------------------------------
    [Fact]
    public async Task Two_operators_posting_to_one_folio_serialize_rather_than_lose_a_charge()
    {
        var (folioId, admissionId, bedId) = await SeedFolioAsync();

        // Both operators open the folio for posting at the same moment. `FolioService.LockAsync`
        // is a SELECT … FOR UPDATE, so the second must WAIT for the first to commit — which is
        // the difference between two charges on the bill and one silently overwriting the other.
        await using var connA = new NpgsqlConnection(_pg.ConnectionString);
        await using var connB = new NpgsqlConnection(_pg.ConnectionString);
        await connA.OpenAsync();
        await connB.OpenAsync();
        await using var ipdA = CreateIpd(connA);
        await using var ipdB = CreateIpd(connB);

        var txA = await connA.BeginTransactionAsync();
        await ipdA.Database.UseTransactionAsync(txA);
        Assert.Equal(FolioState.Open, await _folios.LockAsync(ipdA, folioId));
        ipdA.BedDays.Add(Day(admissionId, bedId, 4));
        await ipdA.SaveChangesAsync();

        var txB = await connB.BeginTransactionAsync();
        await ipdB.Database.UseTransactionAsync(txB);
        var operatorB = Task.Run(async () =>
        {
            await _folios.LockAsync(ipdB, folioId);        // blocks until A commits
            ipdB.BedDays.Add(Day(admissionId, bedId, 5));
            await ipdB.SaveChangesAsync();
            await txB.CommitAsync();
        });

        var finishedEarly = await Task.WhenAny(operatorB, Task.Delay(1500)) == operatorB;
        Assert.False(finishedEarly, "the second operator did not wait for the row lock");

        await txA.CommitAsync();
        await operatorB;                                   // now it may proceed

        await using var fresh = CreateIpd(null);
        var posted = await fresh.BedDays.Where(b => b.AdmissionId == admissionId)
            .OrderBy(b => b.OnDate).ToListAsync();
        Assert.Equal(2, posted.Count);                     // neither charge was lost
    }

    [Fact]
    public async Task A_folio_frozen_for_settlement_refuses_a_concurrent_posting()
    {
        // Serialization alone is not the whole guarantee: once the first operator has moved the
        // folio into settlement, the second is refused in words rather than quietly dropped.
        var (folioId, _, _) = await SeedFolioAsync();
        await using (var ipd = CreateIpd(null))
            await _folios.BeginSettlementAsync(ipd, folioId);

        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var ipdB = CreateIpd(conn);
        await using var kernel = _pg.CreateKernelContext();

        var refused = await Assert.ThrowsAsync<IpdException>(
            () => _folios.EnsurePostableAsync(ipdB, kernel, folioId));
        Assert.Contains("frozen", refused.Message);
    }
}
