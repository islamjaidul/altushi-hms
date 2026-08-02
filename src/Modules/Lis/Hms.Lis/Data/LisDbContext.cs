using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Lis.Data;

// lis.* per 03 §7. Identity lives on Sample alone (edge 27/33) — barcodes cannot duplicate.

public static class SampleState
{
    public const string PendingCollection = "pending_collection";
    public const string Collected = "collected";
    public const string Received = "received";
    public const string Rejected = "rejected";
    public const string Resulted = "resulted";
    public const string Verified = "verified";
    public const string ReportReady = "report_ready";
    public const string Delivered = "delivered";
}

public class Sample
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Barcode { get; set; }
    public required string SampleType { get; set; }
    public string State { get; set; } = SampleState.PendingCollection;
    public DateTimeOffset? CollectedAt { get; set; }
    public long? CollectedBy { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public long? ReceivedBy { get; set; }
    public string? RejectedReason { get; set; }
    public long? RecollectionOf { get; set; }          // child chain (02 §2.8)
    public string? DisposalNote { get; set; }          // jsonb (edge 21)
}

/// <summary>M:N (edge 33): one CBC+ESR tube = 1 sample / 2 tests; culture = many samples / 1 test.</summary>
public class SampleTest
{
    public long SampleId { get; set; }
    public long OrderTestId { get; set; }
}

public class LabelPrint
{
    public long Id { get; set; }
    public long SampleId { get; set; }
    public DateTimeOffset PrintedAt { get; set; }
    public long PrintedBy { get; set; }
    public bool Reprint { get; set; }                  // edge 27 audit
}

public class Result
{
    public long Id { get; set; }
    public long OrderTestId { get; set; }
    public int Version { get; set; } = 1;              // v1, v2… all retained (edge 22)
    public required string Values { get; set; }        // jsonb {param:{value,unit,flag,ref_used,age_precision}}
    public string? Narrative { get; set; }
    public long EnteredBy { get; set; }
    public DateTimeOffset EnteredAt { get; set; }
    public long? VerifiedBy { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public string? VerifierRole { get; set; }          // treating|pathologist|reporting_consultant (edge 34)
    public string? EsignHash { get; set; }
    public string? SignatureImageRef { get; set; }
    public long? AmendApprovalId { get; set; }
    public int? SupersedesVersion { get; set; }
}

public class LisDbContext(DbContextOptions<LisDbContext> options) : DbContext(options)
{

    /// <summary>The branch this context's queries are isolated to (spec 0039 WP5). Captured
    /// from the ambient request scope at construction; every entity carrying a BranchId is
    /// filtered to it structurally — see BranchIsolation.</summary>
    public long CurrentBranch { get; set; } = Hms.Kernel.Data.BranchScope.Current;
    public DbSet<Sample> Samples => Set<Sample>();
    public DbSet<SampleTest> SampleTests => Set<SampleTest>();
    public DbSet<LabelPrint> LabelPrints => Set<LabelPrint>();
    public DbSet<Result> Results => Set<Result>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Spec 0039 WP2: state CHECKs from SampleState above, state-dependent completeness,
        // intra-schema FKs, HasMaxLength and the Result xmin token — same posture as
        // BillDbContext (additive only, NOT VALID in the migration for pre-existing tables).
        b.HasDefaultSchema("lis");
        b.Entity<Sample>(e =>
        {
            e.ToTable("sample", t =>
            {
                t.HasCheckConstraint("ck_sample_state",
                    "state IN ('pending_collection','collected','received','rejected',"
                    + "'resulted','verified','report_ready','delivered')");
                // LisService stamps these on the transitions: every state past
                // pending_collection went through collection, every state past collected
                // went through receipt (rejection happens at receipt), and a rejection
                // always says why (audit §2).
                t.HasCheckConstraint("ck_sample_collected",
                    "state = 'pending_collection' "
                    + "OR (collected_at IS NOT NULL AND collected_by IS NOT NULL)");
                t.HasCheckConstraint("ck_sample_received",
                    "state IN ('pending_collection','collected') "
                    + "OR (received_at IS NOT NULL AND received_by IS NOT NULL)");
                t.HasCheckConstraint("ck_sample_rejected",
                    "state <> 'rejected' OR rejected_reason IS NOT NULL");
            });
            e.Property(x => x.Barcode).HasMaxLength(40);
            e.Property(x => x.SampleType).HasMaxLength(40);
            e.Property(x => x.State).HasMaxLength(40);
            e.Property(x => x.RejectedReason).HasMaxLength(4000);
            e.Property(x => x.DisposalNote).HasMaxLength(4000);
            e.HasIndex(x => x.Barcode).IsUnique();     // single identity (edge 27/33)
            e.HasOne<Sample>().WithMany().HasForeignKey(x => x.RecollectionOf);
        });
        b.Entity<SampleTest>(e =>
        {
            e.ToTable("sample_test");
            e.HasKey(x => new { x.SampleId, x.OrderTestId });
            // The lab board's join predicate — the PK's prefix is sample_id, unusable here
            // (audit §5).
            e.HasIndex(x => x.OrderTestId);
            e.HasOne<Sample>().WithMany().HasForeignKey(x => x.SampleId);
        });
        b.Entity<LabelPrint>(e =>
        {
            e.ToTable("label_print");
            e.HasOne<Sample>().WithMany().HasForeignKey(x => x.SampleId);
        });
        b.Entity<Result>(e =>
        {
            // A verified result carries its whole signature or none of it — the e-sign
            // trio is one write in LisService.VerifyAsync (audit §2).
            e.ToTable("result", t => t.HasCheckConstraint("ck_result_verified",
                "verified_at IS NULL OR (verified_by IS NOT NULL "
                + "AND verifier_role IS NOT NULL AND esign_hash IS NOT NULL)"));
            e.Property(x => x.Values).HasColumnType("jsonb");
            e.Property(x => x.Narrative).HasMaxLength(10000);
            e.Property(x => x.VerifierRole).HasMaxLength(40);
            e.Property(x => x.EsignHash).HasMaxLength(200);          // hex SHA-256 is 64 chars
            e.Property(x => x.SignatureImageRef).HasMaxLength(200);
            e.HasIndex(x => new { x.OrderTestId, x.Version }).IsUnique();
            e.Property<uint>("xmin").IsRowVersion();
        });
        // Hard rule 4: the schema must never delete on its own. Every relationship this model
        // declares refuses a parent delete instead of cascading it — corrections are reversals,
        // and a cascade is a delete machine (spec 0039 WP2, ADR-0028).
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        b.ApplyBranchIsolation(this);   // WP5: branch predicate as structure (AUD-ARCH-01)
    }
}

public class LisDbContextFactory : IDesignTimeDbContextFactory<LisDbContext>
{
    public LisDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<LisDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "lis"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
