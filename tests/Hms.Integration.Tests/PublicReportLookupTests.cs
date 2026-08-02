using Hms.Diagnostics;
using Hms.Diagnostics.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0039 WP6 (AUD-PHI-01): the public report-status lookup key. The audit walked
/// LB-00001…LB-00033 anonymously and read back 9 of 9 live orders, because the "secret" printed
/// on the receipt was the zero-padded primary key. These tests pin the fix at the seam the page
/// uses: an id-shaped input finds nothing (this is the assertion that goes red if anyone
/// reverts to a pk lookup), only the random token resolves, and a cancelled order stays silent.
/// </summary>
[Collection("postgres")]
public class PublicReportLookupTests(PostgresFixture pg) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await using var diag = CreateDiag();
        await diag.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DiagDbContext CreateDiag() => new(
        new DbContextOptionsBuilder<DiagDbContext>()
            .UseNpgsql(pg.ConnectionString, o => o.MigrationsHistoryTable("__ef_migrations", "diag"))
            .UseSnakeCaseNamingConvention().Options);

    private async Task<TestOrder> NewOrderAsync(string state = TestOrderState.InProgress, long branchId = 1)
    {
        await using var diag = CreateDiag();
        var order = new TestOrder
        {
            BranchId = branchId,
            PatientId = 1,
            EncounterId = 1,
            State = state,
            PromisedAt = DateTimeOffset.UtcNow.AddHours(2),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = 1,
            PublicToken = PublicToken.New(),
        };
        diag.Orders.Add(order);
        await diag.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task An_order_number_or_raw_id_finds_nothing_ids_are_not_keys()
    {
        var order = await NewOrderAsync();
        await using var diag = CreateDiag();

        // The exact probe the audit ran: the LB- number from the receipt, and the bare id.
        Assert.Null(await PublicReportLookup.FindAsync(diag, $"LB-{order.Id:D5}"));
        Assert.Null(await PublicReportLookup.FindAsync(diag, order.Id.ToString()));
        Assert.Null(await PublicReportLookup.FindAsync(diag, order.Id.ToString("D32")));
        Assert.Null(await PublicReportLookup.FindAsync(diag, ""));
        Assert.Null(await PublicReportLookup.FindAsync(diag, null));
        Assert.Null(await PublicReportLookup.FindAsync(diag, "NOT-A-TOKEN"));
    }

    [Fact]
    public async Task Only_the_printed_token_resolves_the_order_across_branches()
    {
        // Branch 5: the kiosk visitor has no branch, so the token must work for any branch's order.
        var order = await NewOrderAsync(branchId: 5);
        await using var diag = CreateDiag();

        var found = await PublicReportLookup.FindAsync(diag, order.PublicToken);
        Assert.NotNull(found);
        Assert.Equal(order.Id, found!.Id);

        // Operators read codes back over the phone: case and stray whitespace must not matter.
        Assert.NotNull(await PublicReportLookup.FindAsync(diag, $"  {order.PublicToken.ToUpperInvariant()}  "));
    }

    [Fact]
    public async Task A_cancelled_order_answers_exactly_like_an_unknown_code()
    {
        var order = await NewOrderAsync(state: TestOrderState.Cancelled);
        await using var diag = CreateDiag();
        Assert.Null(await PublicReportLookup.FindAsync(diag, order.PublicToken));
    }

    [Fact]
    public async Task Every_new_order_carries_a_distinct_32_hex_token()
    {
        var a = await NewOrderAsync();
        var b = await NewOrderAsync();
        Assert.Matches("^[0-9a-f]{32}$", a.PublicToken);
        Assert.Matches("^[0-9a-f]{32}$", b.PublicToken);
        Assert.NotEqual(a.PublicToken, b.PublicToken);

        // And the database refuses a duplicate — the unique index is the last line of defence.
        await using var diag = CreateDiag();
        diag.Orders.Add(new TestOrder
        {
            BranchId = 1, PatientId = 1, EncounterId = 1,
            State = TestOrderState.InProgress,
            PromisedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            PublicToken = a.PublicToken,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => diag.SaveChangesAsync());
    }
}
