using Hms.Admin.Data;
using Hms.Appointments.Data;
using Hms.Billing.Data;
using Hms.Notifications.Data;
using Hms.Ipd.Data;
using Hms.Emr.Data;
using Hms.Ot.Data;
using Hms.Radiology.Data;
using Hms.Pharmacy.Data;
using Hms.Diagnostics.Data;
using Hms.Hr.Data;
using Hms.Kernel.Data;
using Hms.Kernel.Auth;
using Hms.Lis.Data;
using Hms.Registration.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Web;

/// <summary>
/// G19: one business action = one transaction. All module contexts ride ONE NpgsqlConnection
/// and one transaction; commit makes invoice+number+audit+outbox durable together (mirrors the
/// integration-test harness the services are proven with).
/// </summary>
public sealed class HmsTx(IConfiguration config)
{
    private readonly string _cs = config.GetConnectionString("Hms")!;

    public async Task<T> RunAsync<T>(Func<TxScope, Task<T>> body, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var scope = new TxScope(conn, tx);
        var result = await body(scope);
        await tx.CommitAsync(ct);
        return result;
    }

    public Task RunAsync(Func<TxScope, Task> body, CancellationToken ct = default)
        => RunAsync<object?>(async s => { await body(s); return null; }, ct);
}

public sealed class TxScope(NpgsqlConnection conn, NpgsqlTransaction tx) : IAsyncDisposable
{
    private readonly List<DbContext> _contexts = [];

    private T Attach<T>(Func<DbContextOptions<T>, T> factory, string schema) where T : DbContext
    {
        var existing = _contexts.OfType<T>().FirstOrDefault();
        if (existing is not null) return existing;
        var opts = new DbContextOptionsBuilder<T>()
            .UseNpgsql(conn, o => o.MigrationsHistoryTable("__ef_migrations", schema))
            .UseSnakeCaseNamingConvention()
            .Options;
        var ctx = factory(opts);
        ctx.Database.UseTransaction(tx);
        _contexts.Add(ctx);
        return ctx;
    }

    public KernelDbContext Kernel => Attach<KernelDbContext>(o => new(o), "kernel");
    public AuthDbContext Auth => Attach<AuthDbContext>(o => new(o), "adm");
    public RegDbContext Reg => Attach<RegDbContext>(o => new(o), "reg");
    public BillDbContext Bill => Attach<BillDbContext>(o => new(o), "bill");
    public DiagDbContext Diag => Attach<DiagDbContext>(o => new(o), "diag");
    public LisDbContext Lis => Attach<LisDbContext>(o => new(o), "lis");
    public AdmDbContext Adm => Attach<AdmDbContext>(o => new(o), "adm_data");
    public ApptDbContext Appt => Attach<ApptDbContext>(o => new(o), "appt");
    public NotifDbContext Notif => Attach<NotifDbContext>(o => new(o), "notif");
    public PharmDbContext Pharm => Attach<PharmDbContext>(o => new(o), "pharm");
    public IpdDbContext Ipd => Attach<IpdDbContext>(o => new(o), "ipd");
    public EmrDbContext Emr => Attach<EmrDbContext>(o => new(o), "emr");
    public OtDbContext Ot => Attach<OtDbContext>(o => new(o), "ot");
    public RadiologyDbContext Radiology => Attach<RadiologyDbContext>(o => new(o), "radiology");
    public HrDbContext Hr => Attach<HrDbContext>(o => new(o), "hr");

    public async ValueTask DisposeAsync()
    {
        foreach (var c in _contexts) await c.DisposeAsync();
    }
}
