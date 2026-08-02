using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Registration.Data;

/// <summary>reg.patient per 03 §2. Permanent identity — never deleted (merge/deactivate only).</summary>
public class Patient
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Uhid { get; set; }
    public required string FullName { get; set; }
    public char Sex { get; set; }                      // M|F|O
    public DateOnly? Dob { get; set; }
    public short? AgeYears { get; set; }               // edge 26: either DOB or age
    public short? AgeMonths { get; set; }
    public bool AgeEstimated { get; set; }
    public DateOnly? AgeAsOf { get; set; }
    public string? Phone { get; set; }                 // edge 24: nullable, never blocks

    /// <summary>
    /// Spec 0020: the phone with every non-digit stripped, maintained by the DATABASE
    /// (generated column). Registration stores the readable form `01712-345999` (§7 U13) but
    /// an operator types what is on the slip — `01712345999` — so search matches on this.
    /// A generated column cannot drift from <see cref="Phone"/> the way a second write path
    /// could, and the migration fills it for every existing row by definition.
    /// </summary>
    public string? PhoneDigits { get; private set; }
    public string? Guardian { get; set; }
    public string? Area { get; set; }
    public string? Address { get; set; }
    public string? Nid { get; set; }
    public string? BloodGroup { get; set; }
    public string PatientType { get; set; } = "general";
    public bool UnknownIdentity { get; set; }          // edge 25
    public bool Provisional { get; set; }              // edge 11
    public long? MergedInto { get; set; }              // edge 23
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

public class PatientMerge
{
    public long Id { get; set; }
    public long SurvivorId { get; set; }
    public long MergedId { get; set; }
    public long ApprovalId { get; set; }
    public DateTimeOffset At { get; set; }
    public long By { get; set; }
    public string? Repointed { get; set; }             // jsonb summary of re-pointed refs
}

public class RegDbContext(DbContextOptions<RegDbContext> options) : DbContext(options)
{

    /// <summary>The branch this context's queries are isolated to (spec 0039 WP5). Captured
    /// from the ambient request scope at construction; every entity carrying a BranchId is
    /// filtered to it structurally — see BranchIsolation.</summary>
    public long CurrentBranch { get; set; } = Hms.Kernel.Data.BranchScope.Current;
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<PatientMerge> Merges => Set<PatientMerge>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("reg");
        b.Entity<Patient>(e =>
        {
            // Widened for a months-only age (spec 0032, M1-D1): an infant under one year has no
            // whole year to give, and rounding to "1" or inventing a DOB corrupts the record.
            e.ToTable("patient", t =>
            {
                t.HasCheckConstraint("ck_identity",
                    "dob is not null or age_years is not null or age_months is not null or unknown_identity");
                // WP2.1 (AUD-VAL-08): 28 patients once carried a DOB of ±infinity — Npgsql maps
                // DateOnly.Min/MaxValue there, and nothing bounded the value. current_date is
                // legal in a CHECK; a same-day birth registers, a future one does not.
                t.HasCheckConstraint("ck_patient_dob",
                    "dob IS NULL OR (dob >= '1900-01-01' AND dob <= current_date)");
                t.HasCheckConstraint("ck_patient_age",
                    "(age_years IS NULL OR age_years BETWEEN 0 AND 130) AND "
                    + "(age_months IS NULL OR age_months BETWEEN 0 AND 11)");
            });
            e.HasIndex(x => x.Uhid).IsUnique();
            e.HasIndex(x => x.Phone);
            e.Property(x => x.PhoneDigits)
                .HasComputedColumnSql(@"regexp_replace(coalesce(phone, ''), '\D', '', 'g')", stored: true);
            e.HasIndex(x => x.PhoneDigits);
            e.Property(x => x.Sex).HasColumnType("char(1)");
            e.Property(x => x.Uhid).HasMaxLength(40);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Guardian).HasMaxLength(200);
            e.Property(x => x.Area).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Nid).HasMaxLength(40);
            e.Property(x => x.BloodGroup).HasMaxLength(40);
            e.Property(x => x.PatientType).HasMaxLength(40);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.MergedInto);
        });
        b.Entity<PatientMerge>(e =>
        {
            e.ToTable("patient_merge");
            e.Property(x => x.Repointed).HasColumnType("jsonb");
        });
        // Hard rule 4: the schema must never delete on its own. Every relationship this model
        // declares refuses a parent delete instead of cascading it — corrections are reversals,
        // and a cascade is a delete machine (spec 0039 WP2, ADR-0028).
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        b.ApplyBranchIsolation(this);   // WP5: branch predicate as structure (AUD-ARCH-01)
    }
}

public class RegDbContextFactory : IDesignTimeDbContextFactory<RegDbContext>
{
    public RegDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<RegDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "reg"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
