using Hms.Hr;
using Hms.Hr.Data;
using Hms.Hr.Screens;
using Hms.Hr.Web;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Hms.Kernel.Entitlements;
using Hms.Kernel.Hosting;
using Hms.Shell;
using Microsoft.EntityFrameworkCore;

// The HRM SKU (ADR-0025). Same source as the ERP, a fraction of the surface: three schemas, no
// clinical code, its own entitlement file. Auth and the platform middleware come from
// HmsPlatform so the two hosts cannot drift on anything security-relevant.

var builder = WebApplication.CreateBuilder(args);
var conn = builder.Configuration.GetConnectionString("Hms")
           ?? throw new InvalidOperationException("ConnectionStrings:Hms missing");

builder.Services.AddDbContext<KernelDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "kernel"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<AuthDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "adm"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<HrDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "hr"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddHmsPlatform(builder.Configuration);

builder.Services.AddRazorPages();
builder.Services.AddAntiforgery();

// ADR-0026 choke point 2. Even in a single-module product this matters: it is what makes an expired
// or wrong licence refuse the application rather than merely hide its menu.
builder.Services.AddModuleEntitlement(new Dictionary<string, string>
{
    [HrModule.RoutePrefix] = HrModule.Name,
});

builder.Services.AddSingleton<IReadOnlyList<NavItem>>(HrNav.Registry);
builder.Services.AddSingleton<IHrTx, HrTx>();
builder.Services.AddSingleton<Hms.Hr.Contracts.IPayrollPosting, JournalOnlyPosting>();
builder.Services.AddSingleton<PolicyResolver>();
builder.Services.AddSingleton<EmployeeService>();
builder.Services.AddSingleton<AttendanceService>();
builder.Services.AddSingleton<LeaveService>();
builder.Services.AddSingleton<PayrollService>();

// P27: this product is sold to employers that are not hospitals, so the identity is neutral.
builder.Services.AddSingleton(OrgIdentity.From(
    builder.Configuration, "Your Organisation", "HR & Payroll"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;

    sp.LoadEntitlement(app.Environment, app.Configuration,
        "../../deploy/entitlements/hrm-only.json");

    var kernel = sp.GetRequiredService<KernelDbContext>();
    await kernel.Database.OpenConnectionAsync();
    await kernel.Database.ExecuteSqlRawAsync(
        $"SELECT pg_advisory_lock({HmsPlatform.MigrationLockId})");
    try
    {
        await kernel.Database.MigrateAsync();
        await sp.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<HrDbContext>().Database.MigrateAsync();

        // Refuses to serve an ERP database rather than serving a fraction of one (ADR-0025).
        await HmsPlatform.ClaimDatabaseAsync(kernel, HostKind.Hrm, acceptsAnyExisting: false);

        await HrSeed.RunAsync(sp);
    }
    finally
    {
        await kernel.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_unlock({HmsPlatform.MigrationLockId})");
        await kernel.Database.CloseConnectionAsync();
    }
}

app.UseHmsPlatform();

app.MapGet("/health", async (KernelDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503);
}).AllowAnonymous();

app.MapRazorPages();

app.Run();

public partial class Program;   // WebApplicationFactory hook for endpoint tests
