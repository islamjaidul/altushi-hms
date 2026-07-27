using Hms.Kernel.Audit;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Hms.Kernel.Entitlements;
using Hms.Kernel.Jobs;
using Hms.Kernel.Numbering;
using Hms.Kernel.Approvals;
using Hms.Kernel.Time;
using Hms.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var conn = builder.Configuration.GetConnectionString("Hms")
           ?? throw new InvalidOperationException("ConnectionStrings:Hms missing");

builder.Services.AddDbContext<KernelDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "kernel"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<AuthDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "adm"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<Hms.Registration.Data.RegDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "reg")).UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<Hms.Billing.Data.BillDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "bill")).UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<Hms.Diagnostics.Data.DiagDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "diag")).UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<Hms.Lis.Data.LisDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "lis")).UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<Hms.Admin.Data.AdmDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "adm_data")).UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<Hms.Appointments.Data.ApptDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "appt")).UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<Hms.Notifications.Data.NotifDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "notif")).UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<Hms.Pharmacy.Data.PharmDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "pharm")).UseSnakeCaseNamingConvention());
builder.Services.AddDbContext<Hms.Ipd.Data.IpdDbContext>(o => o
    .UseNpgsql(conn, x => x.MigrationsHistoryTable("__ef_migrations", "ipd")).UseSnakeCaseNamingConvention());

builder.Services
    .AddIdentity<AppUser, AppRole>(o =>
    {
        // ADR-0019: lockout on by default; password policy sane-not-heroic for counter staff
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddClaimsPrincipalFactory<PermissionClaimsFactory>()
    .AddDefaultTokenProviders();

// ADR-0019 amendment: a revoked grant must die mid-shift, not at the next voluntary sign-in.
// The stamp check re-runs PermissionClaimsFactory, so the cookie's permissions refresh too.
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
    o.ValidationInterval = TimeSpan.FromMinutes(
        builder.Configuration.GetValue("Auth:RevalidationMinutes", 5)));

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/login";
    o.AccessDeniedPath = "/denied";
    o.SlidingExpiration = true;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(
        builder.Configuration.GetValue("Auth:IdleTimeoutMinutes", 15));   // idle-lock (ADR-0019)
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;             // TLS terminates at Caddy
});

// G10: deny-by-default — every endpoint needs auth unless explicitly anonymous.
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRazorPages();
builder.Services.AddAntiforgery();

// Kernel services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NumberSeriesService>();
builder.Services.AddSingleton<AuditWriter>();
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddSingleton<EntitlementProvider>();
builder.Services.AddSingleton(new FiscalCalendar(
    builder.Configuration.GetValue("Business:FiscalStartMonth", 7)));       // P1 default July
builder.Services.AddSingleton(new BusinessDayCalendar(
    TimeOnly.Parse(builder.Configuration.GetValue("Business:DayBoundary", "00:00")!))); // P2
builder.Services.AddSingleton<IReadOnlyList<NavItem>>(ModuleNav.Registry);
builder.Services.AddSingleton<HmsTx>();
builder.Services.AddSingleton<Hms.Registration.RegistrationService>();
builder.Services.AddSingleton<Hms.Billing.BillingService>();
builder.Services.AddSingleton<Hms.Billing.DayCloseService>();
builder.Services.AddSingleton<Hms.Lis.LisService>();
builder.Services.AddSingleton<ApprovalEngine>();
builder.Services.AddSingleton<Hms.Admin.RateResolver>();
builder.Services.AddSingleton<Hms.Admin.CatalogImportService>();
builder.Services.AddSingleton<Hms.Appointments.AppointmentsService>();
builder.Services.AddSingleton<Hms.Pharmacy.StockService>();
builder.Services.AddSingleton<Hms.Pharmacy.PurchaseService>();
builder.Services.AddSingleton<Hms.Ipd.IpdService>();
builder.Services.AddSingleton<Hms.Ipd.FolioService>();
builder.Services.AddSingleton<Hms.Ipd.CertificateService>();
builder.Services.AddSingleton(Hms.Notifications.SmsOptions.From(
    builder.Configuration["HMS_SMS_MODE"]));                                 // edge 3: simulation default
builder.Services.AddSingleton<Hms.Notifications.SmsQueue>();
builder.Services.AddSingleton<HospitalIdentity>(_ => new HospitalIdentity(
    builder.Configuration["Hospital:Name"] ?? "Altushi General Hospital",
    builder.Configuration["Hospital:Tagline"] ?? "Hospital ERP",
    builder.Configuration["Hospital:Address"] ?? "VIP Road, Sheikhghat, Sylhet-3100",
    builder.Configuration["Hospital:Phone"] ?? "0821-719944, 01700-000000",
    builder.Configuration["Hospital:Monogram"] ?? "A",
    builder.Configuration["Hospital:FooterNote"]
        ?? "This is a computer generated document — no signature required for receipts."));

var app = builder.Build();

// Startup: verify entitlement, migrate under advisory lock, seed dev data.
using (var scope = app.Services.CreateScope())
{
    var sp = scope.ServiceProvider;

    var entitlement = sp.GetRequiredService<EntitlementProvider>();
    var contentRoot = app.Environment.ContentRootPath;
    var entPath = Path.GetFullPath(Path.Combine(contentRoot,
        app.Configuration["Entitlement:Path"] ?? "../../deploy/entitlements/dev-all-modules.json"));
    var keyPath = Path.GetFullPath(Path.Combine(contentRoot,
        app.Configuration["Entitlement:PublicKeyPath"] ?? "vendor-public-key.pem"));
    entitlement.Load(File.ReadAllText(entPath), File.ReadAllText(keyPath), DateTimeOffset.UtcNow);

    var kdb = sp.GetRequiredService<KernelDbContext>();
    await kdb.Database.OpenConnectionAsync();
    // single-flight migrations on multi-worker scale-up (03 §12)
    await kdb.Database.ExecuteSqlRawAsync("SELECT pg_advisory_lock(422026)");
    try
    {
        await kdb.Database.MigrateAsync();
        await sp.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<Hms.Registration.Data.RegDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<Hms.Billing.Data.BillDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<Hms.Diagnostics.Data.DiagDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<Hms.Lis.Data.LisDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<Hms.Admin.Data.AdmDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<Hms.Appointments.Data.ApptDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<Hms.Notifications.Data.NotifDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<Hms.Pharmacy.Data.PharmDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<Hms.Ipd.Data.IpdDbContext>().Database.MigrateAsync();
        await DevSeed.RunAsync(sp);
    }
    finally
    {
        await kdb.Database.ExecuteSqlRawAsync("SELECT pg_advisory_unlock(422026)");
        await kdb.Database.CloseConnectionAsync();
    }
}

// TLS terminates at the front proxy (Caddy); trust its X-Forwarded-* so auth cookies and
// redirects see the real scheme/host (ADR-0005 topology).
var fwd = new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                       | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
};
fwd.KnownIPNetworks.Clear(); // proxy reaches us over the docker bridge; trust boundary is the box
fwd.KnownProxies.Clear();
app.UseForwardedHeaders(fwd);

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// The only anonymous surface: login + health (G10).
app.MapGet("/health", async (KernelDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503);
}).AllowAnonymous();

// Kernel type-ahead source (ADR-0020, §7 U5): 2+ chars over name/UHID/phone, ranked
// prefix-first, served from the trigram-indexed patient table. Same authZ as the directory.
app.MapGet("/api/typeahead/patients", async (string? q, HmsTx tx) =>
{
    var term = q?.Trim();
    if (term is null || term.Length < 2) return Results.Json(Array.Empty<object>());

    var prefix = $"{term}%";
    var hits = await tx.RunAsync(async s => await s.Reg.Patients.AsNoTracking()
        .Searchable().Matching(term)                         // spec 0020: phone digits too
        .OrderByDescending(p => EF.Functions.ILike(p.FullName, prefix) ||
                                EF.Functions.ILike(p.Uhid, prefix))
        .ThenByDescending(p => p.Id)
        .Take(10)
        .Select(p => new { value = p.Id, label = p.FullName + " — " + p.Uhid +
                                                 (p.Phone == null ? "" : " · " + p.Phone) })
        .ToListAsync());
    return Results.Json(hits);
}).RequireAuthorization(Perm.RegistrationRead);

app.MapRazorPages();

app.Run();

public partial class Program;   // WebApplicationFactory hook for endpoint tests
