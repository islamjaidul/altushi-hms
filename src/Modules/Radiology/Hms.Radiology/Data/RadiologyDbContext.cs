using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Radiology.Data;

// radiology.* per spec 0026 (PRD §5 M10). Deliberately small: a radiology *report* is a result,
// stored once in lis.result and verified by M9's own e-sign path. What lives here is the study —
// which machine, who performed it, what film it used — and the modality mapping that makes a
// worklist possible. There is no second copy of a patient's report anywhere in this schema.

public static class StudyState
{
    /// <summary>Paid and waiting for the machine.</summary>
    public const string Awaiting = "awaiting";
    /// <summary>Images acquired; waiting for the radiologist.</summary>
    public const string Done = "done";
    /// <summary>A result exists against the order test (signed or not — M9 owns that).</summary>
    public const string Reported = "reported";
}

public class Modality
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Code { get; set; }          // XR, CT, MRI, USG, ECG
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Which tests a machine performs. A test mapped nowhere shows on the unassigned list.</summary>
public class ModalityTest
{
    public long Id { get; set; }
    public long ModalityId { get; set; }
    public long TestCatalogId { get; set; }
}

public class Study
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    /// <summary>diag.order_test — the one place a study and a result agree on what they are about.</summary>
    public long OrderTestId { get; set; }
    public long PatientId { get; set; }
    public long ModalityId { get; set; }
    /// <summary>
    /// The DICOM seam. Generated now and unused: when a modality worklist feed is eventually
    /// built (§13 I10), this is the key the machine echoes back on the images. Reserving it costs
    /// nothing; retrofitting an accession number onto historical studies would cost plenty.
    /// </summary>
    public required string AccessionNo { get; set; }
    public string State { get; set; } = StudyState.Awaiting;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PerformedAt { get; set; }
    public long? PerformedBy { get; set; }
    public string? FilmSize { get; set; }
    public int FilmCount { get; set; }
    public string? TechnicianNote { get; set; }
}

public class RadiologyDbContext(DbContextOptions<RadiologyDbContext> options) : DbContext(options)
{
    public DbSet<Modality> Modalities => Set<Modality>();
    public DbSet<ModalityTest> ModalityTests => Set<ModalityTest>();
    public DbSet<Study> Studies => Set<Study>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("radiology");

        b.Entity<Modality>(e =>
        {
            e.ToTable("modality");
            e.HasIndex(x => new { x.BranchId, x.Code }).IsUnique();
        });

        b.Entity<ModalityTest>(e =>
        {
            e.ToTable("modality_test");
            // A test belongs to one machine: two machines claiming the same test would put the
            // same patient on two worklists, which is the mismatch US10.1 is about.
            e.HasIndex(x => x.TestCatalogId).IsUnique();
        });

        b.Entity<Study>(e =>
        {
            e.ToTable("study");
            e.HasIndex(x => x.OrderTestId).IsUnique();   // idempotency anchor for study creation
            e.HasIndex(x => x.AccessionNo).IsUnique();
            e.HasIndex(x => new { x.ModalityId, x.State });
        });
    }
}

public class RadiologyDbContextFactory : IDesignTimeDbContextFactory<RadiologyDbContext>
{
    public RadiologyDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<RadiologyDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "radiology"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
