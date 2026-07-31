using Hms.Kernel.Approvals;
using Hms.Kernel.Audit;
using Hms.Kernel.Auth;
using Hms.Kernel.Data;
using Hms.Kernel.Entitlements;
using Hms.Kernel.Jobs;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hms.Kernel.Hosting;

/// <summary>Which product a database belongs to. A database is one or the other, never both.</summary>
public static class HostKind
{
    public const string Erp = "erp";
    public const string Hrm = "hrm";
}

/// <summary>
/// The bootstrap both hosts share (ADR-0025). Identity, cookies, authorization, forwarded headers
/// and the migration advisory lock live here rather than being written twice.
/// <para>
/// This is a security boundary, not a convenience. Two <c>Program.cs</c> files each hand-rolling
/// lockout thresholds, cookie flags and the deny-by-default fallback would drift, and the drift
/// would be a vulnerability rather than an inconsistency.
/// </para>
/// </summary>
public static class HmsPlatform
{
    /// <summary>Advisory lock id shared by every host: only one process migrates at a time (03 §12).</summary>
    public const long MigrationLockId = 422026;

    public static IServiceCollection AddHmsPlatform(
        this IServiceCollection services, IConfiguration config)
    {
        services
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
        services.Configure<SecurityStampValidatorOptions>(o =>
            o.ValidationInterval = TimeSpan.FromMinutes(config.GetValue("Auth:RevalidationMinutes", 5)));

        services.ConfigureApplicationCookie(o =>
        {
            o.LoginPath = "/login";
            o.AccessDeniedPath = "/denied";
            o.SlidingExpiration = true;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(config.GetValue("Auth:IdleTimeoutMinutes", 15));
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;   // TLS terminates at Caddy
        });

        // G10: deny-by-default — every endpoint needs auth unless explicitly anonymous.
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<NumberSeriesService>();
        services.AddSingleton<AuditWriter>();
        services.AddSingleton<JobQueue>();
        services.AddSingleton<ApprovalEngine>();
        services.AddSingleton<EntitlementProvider>();
        services.AddSingleton(new FiscalCalendar(config.GetValue("Business:FiscalStartMonth", 7)));
        services.AddSingleton(new BusinessDayCalendar(
            TimeOnly.Parse(config.GetValue("Business:DayBoundary", "00:00")!)));

        return services;
    }

    /// <summary>
    /// Verifies the entitlement from disk. Fail-fast by design: a host that cannot prove what it is
    /// licensed for must not start and quietly serve everything.
    /// </summary>
    public static EntitlementProvider LoadEntitlement(
        this IServiceProvider sp, IHostEnvironment env, IConfiguration config, string defaultPath)
    {
        var entitlement = sp.GetRequiredService<EntitlementProvider>();
        var entPath = Path.GetFullPath(Path.Combine(
            env.ContentRootPath, config["Entitlement:Path"] ?? defaultPath));
        var keyPath = Path.GetFullPath(Path.Combine(
            env.ContentRootPath, config["Entitlement:PublicKeyPath"] ?? "vendor-public-key.pem"));
        entitlement.Load(File.ReadAllText(entPath), File.ReadAllText(keyPath), DateTimeOffset.UtcNow);
        return entitlement;
    }

    /// <summary>
    /// Claims the database for a product line, or refuses to start.
    /// <para>
    /// An HRM database booted under the ERP host is the upsell path and is allowed — the ERP simply
    /// migrates the schemas HRM never had. The reverse is refused: pointing the HRM host at a full
    /// ERP database would serve a fraction of it while the rest went unmaintained, which is worse
    /// than not starting.
    /// </para>
    /// </summary>
    public static async Task ClaimDatabaseAsync(
        KernelDbContext kernel, string hostKind, bool acceptsAnyExisting, CancellationToken ct = default)
    {
        await kernel.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS kernel.host_kind (
                id boolean PRIMARY KEY DEFAULT true CHECK (id),
                kind text NOT NULL,
                claimed_at timestamptz NOT NULL DEFAULT now()
            );
            """, ct);

        var existing = (await kernel.Database
            .SqlQueryRaw<string>("SELECT kind AS \"Value\" FROM kernel.host_kind LIMIT 1")
            .ToListAsync(ct)).FirstOrDefault();

        if (existing is null)
        {
            await kernel.Database.ExecuteSqlRawAsync(
                "INSERT INTO kernel.host_kind (kind) VALUES ({0}) ON CONFLICT (id) DO NOTHING",
                [hostKind], ct);
            return;
        }

        if (existing == hostKind) return;

        if (acceptsAnyExisting)
        {
            // The ERP adopting an HRM database: record the change so the history is not silent.
            await kernel.Database.ExecuteSqlRawAsync(
                "UPDATE kernel.host_kind SET kind = {0}, claimed_at = now()", [hostKind], ct);
            return;
        }

        throw new InvalidOperationException(
            $"This database belongs to the '{existing}' product and cannot be served by the "
            + $"'{hostKind}' host. Point the connection string at the right database, or use the "
            + "full ERP host, which can adopt an HRM database.");
    }

    /// <summary>Forwarded headers, static files, auth, and the read-only licence gate, in order.</summary>
    public static WebApplication UseHmsPlatform(this WebApplication app)
    {
        // TLS terminates at the front proxy (Caddy); trust its X-Forwarded-* so auth cookies and
        // redirects see the real scheme/host (ADR-0005 topology).
        var fwd = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
        };
        fwd.KnownIPNetworks.Clear();   // proxy reaches us over the docker bridge; trust boundary is the box
        fwd.KnownProxies.Clear();
        app.UseForwardedHeaders(fwd);

        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<ReadOnlyEntitlementMiddleware>();

        return app;
    }
}
