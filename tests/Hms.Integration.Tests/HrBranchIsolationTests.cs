using Hms.Hr.Data;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Integration.Tests;

/// <summary>
/// WP5's branch isolation proven against real SQL (spec 0039). The architecture test asserts
/// every context declares a filter for every branch-carrying entity; this proves the filter
/// actually filters: rows one branch wrote are invisible to a context scoped elsewhere and
/// visible again to a context scoped to their branch — without any query mentioning BranchId.
/// </summary>
[Collection("postgres")]
public sealed class HrBranchIsolationTests(PostgresFixture pg)
{
    [Fact]
    public async Task A_row_in_another_branch_is_invisible_until_the_context_is_scoped_to_it()
    {
        await using (var kernel = new KernelDbContext(Options<KernelDbContext>("kernel")))
            await kernel.Database.MigrateAsync();
        await using (var auth = new AuthDbContext(Options<AuthDbContext>("adm")))
            await auth.Database.MigrateAsync();
        await using (var hr = new HrDbContext(Options<HrDbContext>("hr")))
            await hr.Database.MigrateAsync();

        var branch = Random.Shared.NextInt64(9_000_000, 9_900_000);
        await using (var writer = new HrDbContext(Options<HrDbContext>("hr")))
        {
            writer.PayrollPolicies.Add(new PayrollPolicy
            {
                BranchId = branch, EffectiveFrom = new DateOnly(2025, 1, 1),
                DayCountConvention = DayCountConvention.CalendarDays, MinimumNetPayTaka = 0,
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            });
            await writer.SaveChangesAsync();
        }

        // A context left at the default scope (branch 1) must not see the row, even when the
        // query names the branch explicitly — the filter wins over the predicate.
        await using (var other = new HrDbContext(Options<HrDbContext>("hr")))
            Assert.Empty(await other.PayrollPolicies.AsNoTracking()
                .Where(p => p.BranchId == branch).ToListAsync());

        // The same query through a context scoped to the row's branch sees it.
        await using (var scoped = new HrDbContext(Options<HrDbContext>("hr")))
        {
            scoped.CurrentBranch = branch;
            Assert.Single(await scoped.PayrollPolicies.AsNoTracking()
                .Where(p => p.BranchId == branch).ToListAsync());
        }
    }

    private DbContextOptions<T> Options<T>(string schema) where T : DbContext
        => new DbContextOptionsBuilder<T>()
            .UseNpgsql(pg.ConnectionString, o => o.MigrationsHistoryTable("__ef_migrations", schema))
            .UseSnakeCaseNamingConvention()
            .Options;
}
