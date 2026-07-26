using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Kernel.Auth;

/// <summary>03 §9: users/roles live in the `adm` schema. Identity supplies hashing, lockout
/// and 2FA plumbing (ADR-0019); permissions are our own rows, stamped as claims at sign-in.</summary>
public class AppUser : IdentityUser<long>
{
    public string DisplayName { get; set; } = "";
    public bool MustUse2fa { get; set; }              // finance-approver/admin/MD roles (ADR-0019)
    public bool Active { get; set; } = true;
    public string? EmployeeRef { get; set; }
}

public class AppRole : IdentityRole<long>
{
    /// <summary>§12 role templates ship as system rows; per-facility custom roles are data.</summary>
    public bool System { get; set; }
}

/// <summary>One `module.action` grant for a role (§12 matrix as data).</summary>
public class Permission
{
    public long Id { get; set; }
    public long RoleId { get; set; }
    public required string Module { get; set; }
    public required string Action { get; set; }
    public string? Scope { get; set; }

    public string Value => $"{Module}.{Action}";
}

public class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AppUser, AppRole, long>(options)
{
    public DbSet<Permission> Permissions => Set<Permission>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema("adm");
        b.Entity<AppUser>(e => e.ToTable("app_user"));
        b.Entity<AppRole>(e => e.ToTable("role"));
        b.Entity<IdentityUserRole<long>>(e => e.ToTable("user_role"));
        b.Entity<IdentityUserClaim<long>>(e => e.ToTable("user_claim"));
        b.Entity<IdentityUserLogin<long>>(e => e.ToTable("user_login"));
        b.Entity<IdentityUserToken<long>>(e => e.ToTable("user_token"));
        b.Entity<IdentityRoleClaim<long>>(e => e.ToTable("role_claim"));
        b.Entity<Permission>(e =>
        {
            e.ToTable("permission");
            e.HasIndex(x => new { x.RoleId, x.Module, x.Action }).IsUnique();
        });
    }
}

public class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "adm"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AuthDbContext(opts);
    }
}
