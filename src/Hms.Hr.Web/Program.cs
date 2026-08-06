using Hms.Hr;
using Hms.Hr.Data;
using Hms.Hr.Screens;
using Hms.Hr.Web;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Hms.Kernel.Entitlements;
using Hms.Kernel.Hosting;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;

// The HRM SKU (ADR-0025). Same source as the ERP, a fraction of the surface: three schemas, no
// clinical code, its own entitlement file. Auth and the platform middleware come from
// HmsPlatform so the two hosts cannot drift on anything security-relevant.

var builder = WebApplication.CreateBuilder(args);
var conn = builder.Configuration.GetConnectionString("Hms")
           ?? throw new InvalidOperationException("ConnectionStrings:Hms missing");

// AUD-XCUT-01: cap the pool below the deployed max_connections=40 shared by both SKUs, unless
// the connection string sets its own. Written back so HrTx and the contexts read the same value.
if (!conn.Contains("Pool Size", StringComparison.OrdinalIgnoreCase))
{
    conn = new Npgsql.NpgsqlConnectionStringBuilder(conn)
    {
        MaxPoolSize = builder.Configuration.GetValue("Db:MaxPoolSize", 15),
    }.ConnectionString;
    builder.Configuration["ConnectionStrings:Hms"] = conn;
}

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

// Spec 0039 WP6 (AUD-XCUT-02): the background worker, next to the JobQueue AddHmsPlatform just
// registered. This SKU runs the kernel's own recurring work (approval escalation); it ships no
// notification module, so the SMS drain and daily jobs live only in the ERP host.
builder.Services.AddHostedService<Hms.Kernel.Jobs.JobWorker>();
builder.Services.AddSingleton<Hms.Kernel.Jobs.IRecurringJob, Hms.Kernel.Jobs.ApprovalEscalationJob>();

builder.Services.AddRazorPages().AddSessionStateTempDataProvider();   // spec 0039 WP1.3
builder.Services.AddAntiforgery();

// ADR-0026 choke point 2. Even in a single-module product this matters: it is what makes an expired
// or wrong licence refuse the application rather than merely hide its menu.
builder.Services.AddModuleEntitlement(new Dictionary<string, string>
{
    [HrModule.RoutePrefix] = HrModule.Name,
});

builder.Services.AddSingleton<IReadOnlyList<NavItem>>(HrmNav.Composed);
builder.Services.AddSingleton<IHrTx, HrTx>();
builder.Services.AddSingleton<IPlatformTx, PlatformTxOverHr>();

// What this SKU can grant: HR, plus the platform capabilities every host has. It ships no clinical
// permission, so none can be granted here — the commercial boundary and the security boundary are
// the same list.
builder.Services.AddSingleton(PermissionCatalog.FromPolicyConstants(
    typeof(HrPerm), typeof(PlatformPerm)));
builder.Services.AddSingleton<Hms.Hr.Contracts.IPayrollPosting, JournalOnlyPosting>();

// Numbering's fiscal year (ADR-0004 P1, default July). The ERP host has always registered this;
// the HRM host did not, because nothing here needed it until the period control did.
builder.Services.AddSingleton(new FiscalCalendar(
    builder.Configuration.GetValue("Business:FiscalStartMonth", 7)));

// The period control's leave year and financial year come from employer configuration, never a
// constant (module PRD §12.3, D2). HR owns that policy, so HR supplies the calendar.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IPeriodCalendarSource, HrPeriodCalendarSource>();
builder.Services.AddSingleton<PolicyResolver>();
builder.Services.AddSingleton<EmployeeService>();
builder.Services.AddSingleton<AttendanceService>();
builder.Services.AddSingleton<LeaveService>();
builder.Services.AddSingleton<PayrollService>();

// P27: this product is sold to employers that are not hospitals, so the identity is neutral.
builder.Services.AddSingleton(OrgIdentity.From(
    builder.Configuration, "Your Organisation", "HR & Payroll",
    ProductChrome.HrmName, ProductChrome.HrmShortcuts));

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

        // WP2.6 (AUD-ARCH-03): hard rule 4's grants for the schemas this SKU serves.
        await HmsPlatform.ApplyHardRule4GrantsAsync(kernel, ["kernel", "adm", "hr"]);

        await HrSeed.RunAsync(sp);
        await HrDemoSeed.RunAsync(sp);
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
