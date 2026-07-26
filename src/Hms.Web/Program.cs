using Hms.Kernel.Audit;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Hms.Kernel.Entitlements;
using Hms.Kernel.Jobs;
using Hms.Kernel.Numbering;
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

app.MapRazorPages();

app.Run();

public partial class Program;   // WebApplicationFactory hook for endpoint tests
