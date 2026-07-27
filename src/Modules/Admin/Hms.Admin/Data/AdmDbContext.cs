using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Admin.Data;

// adm.* masters per 03 §5 — S3 scope: service/test catalog + effective-dated rate versions.
// Doctors/referrers/beds/bank accounts join in S5 with the masters screens.

public class Service
{
    public long Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Dept { get; set; }
    public required string Kind { get; set; }          // consult|procedure|other
    public bool Provisional { get; set; }              // edge 11
    public bool Active { get; set; } = true;
}

public class TestCatalogItem
{
    public long Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Dept { get; set; }
    public required string[] SampleTypes { get; set; } // edge 33 M:N basis
    public int TatMinutes { get; set; }                // TAT promise input (§9A.2)
    public string? Template { get; set; }              // jsonb parameter template
    public bool Provisional { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>C6: prices are effective-dated versions; overlap is impossible by constraint.</summary>
public class RateVersion
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public string Scope { get; set; } = "standard";    // standard|corporate:<id>|package:<id>
    public required string CatalogKind { get; set; }   // service|test
    public long CatalogId { get; set; }
    public long Price { get; set; }                    // whole taka
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }             // null = open-ended
    public long AuthorId { get; set; }
    public long? ApprovalId { get; set; }              // rate changes are MD-approved (§12)
}

/// <summary>
/// §5 M8 [M]: "referrer capture on every order". Referral economics are the driver of the
/// Bangladeshi diagnostic business (§3.2), so the reference is a master row, never free text —
/// M19's commission reporting later joins on this id.
/// </summary>
public class Referrer
{
    public long Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    /// <summary>doctor|agent|corporate|self — "self" is the walk-in default.</summary>
    public required string Kind { get; set; }
    public string? Area { get; set; }
    public string? Phone { get; set; }
    /// <summary>Accrual rate for M19; the MVP captures it, payouts land post-MVP (§9A.3).</summary>
    public short CommissionPercent { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// 5A-R1 [Must]: the external consultant who verifies and signs lab/imaging reports — not the
/// treating doctor. Their stored signature block is applied to the released report (edge 34).
/// </summary>
public class ReportingConsultant
{
    public long Id { get; set; }
    public required string Name { get; set; }
    /// <summary>e.g. "MBBS, MD (Pathology)" — printed under the signature line.</summary>
    public required string Degrees { get; set; }
    public string? BmdcNo { get; set; }
    /// <summary>Which test departments this consultant may sign for; empty = all.</summary>
    public required string[] Departments { get; set; }
    public bool Active { get; set; } = true;
}

public class AdmDbContext(DbContextOptions<AdmDbContext> options) : DbContext(options)
{
    public DbSet<Service> Services => Set<Service>();
    public DbSet<TestCatalogItem> TestCatalog => Set<TestCatalogItem>();
    public DbSet<RateVersion> RateVersions => Set<RateVersion>();
    public DbSet<Referrer> Referrers => Set<Referrer>();
    public DbSet<ReportingConsultant> ReportingConsultants => Set<ReportingConsultant>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("adm");
        b.Entity<Service>(e => { e.ToTable("service"); e.HasIndex(x => x.Code).IsUnique(); });
        b.Entity<TestCatalogItem>(e =>
        {
            e.ToTable("test_catalog");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Template).HasColumnType("jsonb");
        });
        b.Entity<RateVersion>(e => e.ToTable("rate_version"));
        b.Entity<Referrer>(e => { e.ToTable("referrer"); e.HasIndex(x => x.Code).IsUnique(); });
        b.Entity<ReportingConsultant>(e => e.ToTable("reporting_consultant"));
        // The GiST exclusion constraint is raw SQL in the InitAdm migration (00 §2).
    }
}

public class AdmDbContextFactory : IDesignTimeDbContextFactory<AdmDbContext>
{
    public AdmDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<AdmDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "adm_data"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
