using Hms.Hr;
using Hms.Hr.Data;
using Hms.Hr.Web;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Hms.Integration.Tests;

/// <summary>
/// The transaction seam has to make staged work durable.
/// <para>
/// Until spec 0037 it did not. <c>HrTx.RunAsync</c> committed the Npgsql transaction without ever
/// calling <c>SaveChangesAsync</c>, and a commit over an unflushed change tracker writes nothing —
/// so six HR screens returned 302 with a success toast and changed no row: attendance correction,
/// leave recommend, leave approve, payroll review, the payroll policy, and every roster assignment.
/// Payroll could not be walked past <c>generated</c> at all.
/// </para>
/// <para>
/// Every one of those defects is one shape: a handler that stages through EF and trusts the commit.
/// A test per screen would grow one row at a time and still miss the seventh. This asserts the
/// property the screens rely on, once, at the seam they all go through. It was seen to fail on the
/// pre-fix tree with the row absent.
/// </para>
/// </summary>
[Collection("postgres")]
public sealed class TransactionDurabilityTests(PostgresFixture pg)
{
    private const long Branch = 1;

    [Fact]
    public async Task Staged_work_is_durable_without_the_caller_saving()
    {
        await MigrateAsync();
        var tx = new HrTx(Config());
        var code = "DUR-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        // Exactly what a page handler does: stage, and let the seam make it durable.
        await tx.RunAsync(async s =>
        {
            s.Hr.OrgUnits.Add(new OrgUnit { BranchId = Branch, Code = code, Name = "Durability" });
            await Task.CompletedTask;
        });

        await using var hr = HrContext();
        Assert.True(
            await hr.OrgUnits.AnyAsync(u => u.Code == code),
            "RunAsync committed without flushing — the row the body staged was never written.");
    }

    [Fact]
    public async Task A_mutation_to_a_tracked_row_is_durable_too()
    {
        await MigrateAsync();
        var tx = new HrTx(Config());
        var code = "MUT-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        await tx.RunAsync(async s =>
        {
            s.Hr.OrgUnits.Add(new OrgUnit { BranchId = Branch, Code = code, Name = "Before" });
            await Task.CompletedTask;
        });

        // The shape of every one of the six: load, mutate a tracked entity, return.
        await tx.RunAsync(async s =>
        {
            var unit = await s.Hr.OrgUnits.SingleAsync(u => u.Code == code);
            unit.Name = "After";
        });

        await using var hr = HrContext();
        Assert.Equal("After", (await hr.OrgUnits.SingleAsync(u => u.Code == code)).Name);
    }

    [Fact]
    public async Task A_body_that_throws_leaves_nothing_behind()
    {
        await MigrateAsync();
        var tx = new HrTx(Config());
        var code = "RBK-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

        await Assert.ThrowsAsync<HrException>(() => tx.RunAsync(async s =>
        {
            s.Hr.OrgUnits.Add(new OrgUnit { BranchId = Branch, Code = code, Name = "Doomed" });
            await Task.CompletedTask;
            throw new HrException("no");
        }));

        // Flushing before the commit must not turn a rolled-back action into a partial write.
        await using var hr = HrContext();
        Assert.False(await hr.OrgUnits.AnyAsync(u => u.Code == code));
    }

    // -----------------------------------------------------------------------
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
