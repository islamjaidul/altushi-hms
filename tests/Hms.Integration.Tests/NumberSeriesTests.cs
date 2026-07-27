using Hms.Kernel.Numbering;

namespace Hms.Integration.Tests;

// ADR-0004 + G7: the first mandatory concurrency test — parallel issuance on real Postgres.
[Collection("postgres")]
public class NumberSeriesTests(PostgresFixture pg)
{
    private readonly NumberSeriesService _svc = new();

    /// <summary>
    /// A series key of this class's own. The shared Postgres fixture is one database, so
    /// borrowing the real "invoice" series makes these assertions depend on whether some
    /// other test class happened to raise an invoice first — a trap that only springs when
    /// someone adds a test elsewhere.
    /// </summary>
    private const string Series = "test-number-series";

    [Fact]
    public async Task Parallel_issuance_is_collision_free_and_contiguous()
    {
        const int workers = 40;
        var issued = new List<long>();

        await Parallel.ForAsync(0, workers, async (_, ct) =>
        {
            await using var db = pg.CreateKernelContext();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var v = await _svc.IssueValueAsync(db, 1, Series, "2026-27", "INV-{fy}-{n:D6}", ct);
            await tx.CommitAsync(ct);
            lock (issued) issued.Add(v);
        });

        Assert.Equal(workers, issued.Count);
        Assert.Equal(workers, issued.Distinct().Count());            // collision-free
        Assert.Equal(Enumerable.Range(1, workers).Select(i => (long)i).Order(),
                     issued.Order());                                 // contiguous, gap-free
    }

    [Fact]
    public async Task Rollback_returns_the_number_to_the_series()
    {
        long first;
        await using (var db = pg.CreateKernelContext())
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            first = await _svc.IssueValueAsync(db, 1, "receipt", "2026-27", "RCP-{n}");
            await tx.RollbackAsync();                                 // e.g. payment failed validation
        }

        await using var db2 = pg.CreateKernelContext();
        await using var tx2 = await db2.Database.BeginTransactionAsync();
        var second = await _svc.IssueValueAsync(db2, 1, "receipt", "2026-27", "RCP-{n}");
        await tx2.CommitAsync();

        Assert.Equal(first, second);                                  // gap-free after rollback
    }

    [Fact]
    public async Task Issuance_outside_a_transaction_is_refused()
    {
        await using var db = pg.CreateKernelContext();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.IssueValueAsync(db, 1, Series, "2026-27", "X-{n}"));
    }

    [Fact]
    public async Task Fiscal_year_scopes_are_independent()
    {
        await using var db = pg.CreateKernelContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await _svc.IssueValueAsync(db, 1, Series, "2027-28", "INV-{fy}-{n:D6}");
        await tx.CommitAsync();
        Assert.Equal(1, a);                                           // fresh series per fiscal year
    }

    [Fact]
    public void Display_format_applies_padding_and_fiscal_year()
        => Assert.Equal("INV-2026-27-000042",
            NumberSeriesService.Format("INV-{fy}-{n:D6}", "2026-27", 42));
}
