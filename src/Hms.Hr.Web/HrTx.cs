using Hms.Hr;
using Hms.Hr.Contracts;
using Hms.Hr.Data;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Hr.Web;

/// <summary>
/// G19 for the HRM SKU: one business action, one transaction. The same shape as the ERP host's
/// <c>HmsTx</c>, over three contexts instead of fourteen — an HR write and its kernel audit row
/// commit together or not at all.
/// </summary>
public sealed class HrTx(IConfiguration config) : IHrTx
{
    private readonly string _cs = config.GetConnectionString("Hms")!;

    public async Task<T> RunAsync<T>(Func<HrScope, Task<T>> body, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using var kernel = Attach<KernelDbContext>(conn, tx, o => new(o), "kernel");
        await using var auth = Attach<AuthDbContext>(conn, tx, o => new(o), "adm");
        await using var hr = Attach<HrDbContext>(conn, tx, o => new(o), "hr");

        var result = await body(new HrScope(kernel, auth, hr));
        await tx.CommitAsync(ct);
        return result;
    }

    public Task RunAsync(Func<HrScope, Task> body, CancellationToken ct = default)
        => RunAsync<object?>(async s => { await body(s); return null; }, ct);

    private static T Attach<T>(
        NpgsqlConnection conn, NpgsqlTransaction tx, Func<DbContextOptions<T>, T> factory, string schema)
        where T : DbContext
    {
        var opts = new DbContextOptionsBuilder<T>()
            .UseNpgsql(conn, o => o.MigrationsHistoryTable("__ef_migrations", schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        var ctx = factory(opts);
        ctx.Database.UseTransaction(tx);
        return ctx;
    }
}

/// <summary>
/// There is no ledger in this product, and §6.6 forbids an operational module inventing one. The
/// balanced journal is persisted on the payroll run at lock time; this records that the hand-off
/// step happened. An HRM customer who later buys Accounts finds every journal already waiting.
/// </summary>
public sealed class JournalOnlyPosting : IPayrollPosting
{
    public Task<long> PostAsync(PayrollJournal journal, long actorId, CancellationToken ct = default)
        => Task.FromResult(journal.RunId);
}
