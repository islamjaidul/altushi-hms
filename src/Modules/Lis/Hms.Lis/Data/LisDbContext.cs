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
    public DbSet<Sample> Samples => Set<Sample>();
    public DbSet<SampleTest> SampleTests => Set<SampleTest>();
    public DbSet<LabelPrint> LabelPrints => Set<LabelPrint>();
    public DbSet<Result> Results => Set<Result>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("lis");
        b.Entity<Sample>(e =>
        {
            e.ToTable("sample");
            e.HasIndex(x => x.Barcode).IsUnique();     // single identity (edge 27/33)
        });
        b.Entity<SampleTest>(e =>
        {
            e.ToTable("sample_test");
            e.HasKey(x => new { x.SampleId, x.OrderTestId });
        });
        b.Entity<LabelPrint>(e => e.ToTable("label_print"));
        b.Entity<Result>(e =>
        {
            e.ToTable("result");
            e.Property(x => x.Values).HasColumnType("jsonb");
            e.HasIndex(x => new { x.OrderTestId, x.Version }).IsUnique();
        });
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
