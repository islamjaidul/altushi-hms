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

        // Spec 0039 WP1.3 (AUD-VAL-05): TempData rides in server-side session state, never in
        // cookies. With no provider registered ASP.NET's cookie provider applied, so one
        // oversized toast became 136 KB of request headers and Kestrel answered HTTP 431 to
        // every request from that session — including /logout. In-memory store is correct for
        // the single-VM deployment (§16); a lost toast on restart is a non-event.
        services.AddDistributedMemoryCache();
        services.AddSession(o =>
        {
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.IdleTimeout = TimeSpan.FromMinutes(config.GetValue("Auth:IdleTimeoutMinutes", 15));
        });

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

    /// <summary>
    /// Puts hard rule 4's enforcement into force (spec 0039 WP2.6, AUD-ARCH-03). The grants
    /// existed since InitBill but covered only <c>bill</c> + the audit table, and nothing
    /// connected as the protected role. This runs after EVERY boot's migrations — idempotent,
    /// covering all schemas the host just migrated including tables created moments ago —
    /// which a fixed-order migration could never do (kernel migrates first, before the other
    /// schemas exist).
    /// <para>
    /// The runtime switch itself is deployment configuration: point the app's connection
    /// string at <c>hms_app</c> and keep <c>hms_migrator</c> for migration-time only. Until a
    /// deployment flips that, these grants are ready and waiting rather than in force —
    /// but they can no longer be *missing*.
    /// </para>
    /// </summary>
    public static async Task ApplyHardRule4GrantsAsync(
        KernelDbContext kernel, string[] schemas, CancellationToken ct = default)
    {
        foreach (var schema in schemas)
        {
            // Financial/clinical rows are never hard-deleted (hard rule 4): the app role gets
            // read/write and NO DELETE anywhere. The few legitimate hard-delete sites
            // (draft prescription lines, modality remaps, permission rows) are re-granted
            // narrowly below. Idempotent: GRANT/REVOKE re-apply cleanly.
            // Schema names are the compile-time constants each host passes — never request
            // input — so interpolating them into DDL is safe (DDL cannot be parameterised).
            var grantSql = $"""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'hms_app') THEN
                        CREATE ROLE hms_app LOGIN PASSWORD 'hms_app_dev';  -- deploy overrides via ALTER ROLE
                    END IF;
                    IF EXISTS (SELECT FROM information_schema.schemata WHERE schema_name = '{schema}') THEN
                        EXECUTE 'GRANT USAGE ON SCHEMA {schema} TO hms_app';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON ALL TABLES IN SCHEMA {schema} TO hms_app';
                        EXECUTE 'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {schema} TO hms_app';
                        EXECUTE 'REVOKE DELETE ON ALL TABLES IN SCHEMA {schema} FROM hms_app';
                        EXECUTE 'ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} '
                             || 'GRANT SELECT, INSERT, UPDATE ON TABLES TO hms_app';
                    END IF;
                END $$;
                """;
            await kernel.Database.ExecuteSqlRawAsync(grantSql, ct);
        }

        // Append-only means no UPDATE either; and the app's own few hard-delete sites stay legal.
        await kernel.Database.ExecuteSqlRawAsync("""
            DO $$
            BEGIN
                IF to_regclass('kernel.audit_event') IS NOT NULL THEN
                    EXECUTE 'REVOKE UPDATE, DELETE ON kernel.audit_event FROM hms_app';
                END IF;
                IF to_regclass('pharm.stock_move') IS NOT NULL THEN
                    EXECUTE 'REVOKE UPDATE, DELETE ON pharm.stock_move FROM hms_app';
                END IF;
                IF to_regclass('pharm.supplier_ledger') IS NOT NULL THEN
                    EXECUTE 'REVOKE UPDATE, DELETE ON pharm.supplier_ledger FROM hms_app';
                END IF;
                IF to_regclass('emr.note_drug') IS NOT NULL THEN
                    EXECUTE 'GRANT DELETE ON emr.note_drug TO hms_app';        -- unfinalised draft scratch
                END IF;
                IF to_regclass('radiology.modality_test') IS NOT NULL THEN
                    EXECUTE 'GRANT DELETE ON radiology.modality_test TO hms_app';  -- mapping remap
                END IF;
                -- ASP.NET Identity and the permission matrix legitimately delete their own
                -- rows. NOT the whole adm schema: rate_version is effective-dated price
                -- history (hard rule 5) and the masters are deactivate-only.
                IF to_regclass('adm.user_role') IS NOT NULL THEN
                    EXECUTE 'GRANT DELETE ON adm.permission, adm.role_claim, adm.user_claim, '
                         || 'adm.user_login, adm.user_role, adm.user_token TO hms_app';
                END IF;
            END $$;
            """, ct);
    }

    /// <summary>Forwarded headers, static files, auth, and the read-only licence gate, in order.</summary>
    public static WebApplication UseHmsPlatform(this WebApplication app)
    {
        // Outermost: nothing below this line may surface to an operator as a blank 500 (WP6).
        app.UseMiddleware<FaultBoundaryMiddleware>();

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
        app.UseSession();          // TempData's backing store (spec 0039 WP1.3)
        app.UseAuthentication();
        app.UseMiddleware<BranchResolutionMiddleware>();   // branch from the principal (WP5)
        app.UseAuthorization();
        app.UseMiddleware<ReadOnlyEntitlementMiddleware>();

        return app;
    }
}
