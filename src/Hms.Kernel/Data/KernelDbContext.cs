using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Kernel.Data;

public class KernelDbContext(DbContextOptions<KernelDbContext> options) : DbContext(options)
{

    /// <summary>The branch this context's queries are isolated to (spec 0039 WP5). Captured
    /// from the ambient request scope at construction; every entity carrying a BranchId is
    /// filtered to it structurally — see BranchIsolation.</summary>
    public long CurrentBranch { get; set; } = Hms.Kernel.Data.BranchScope.Current;
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalPolicy> ApprovalPolicies => Set<ApprovalPolicy>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("kernel");

        // Table names follow 03-data-model.md exactly (singular) — raw SQL on the money
        // paths depends on them.
        b.Entity<Branch>(e => e.ToTable("branch"));
        b.Entity<NumberSeries>(e => e.ToTable("number_series"));
        b.Entity<ApprovalRequest>(e => e.ToTable("approval_request"));
        b.Entity<ApprovalPolicy>(e => e.ToTable("approval_policy"));
        b.Entity<ApprovalDelegation>(e => e.ToTable("approval_delegation"));
        b.Entity<AuditEvent>(e => e.ToTable("audit_event"));
        b.Entity<Job>(e => e.ToTable("job"));
        b.Entity<OutboxMessage>(e => e.ToTable("outbox"));
        b.Entity<Setting>(e => e.ToTable("setting"));
        b.Entity<ImportBatch>(e => e.ToTable("import_batch"));

        b.Entity<Branch>(e => e.HasIndex(x => x.Code).IsUnique());

        b.Entity<NumberSeries>(e =>
        {
            e.HasIndex(x => new { x.BranchId, x.DocType, x.FiscalYear }).IsUnique();
        });

        b.Entity<ApprovalRequest>(e =>
        {
            e.Property(x => x.ThresholdSnapshot).HasColumnType("jsonb");
            // Spec 0039 WP2: bound the free-text reason (the 100 KB-paste class).
            e.Property(x => x.Reason).HasMaxLength(4000);
            e.HasIndex(x => new { x.State, x.Type });
            e.ToTable(t => t.HasCheckConstraint("ck_approval_state",
                "state in ('pending','approved','rejected','expired')"));
        });

        // Spec 0039 WP2 (audit Tier 4 #21): a delegation window must run forward. The overlap
        // EXCLUDE the audit also suggests is NOT added: `types` is text[] and per-row — an
        // EXCLUDE on (from_user, tstzrange) alone would forbid two concurrent delegations for
        // disjoint approval types, which the model allows; text[] has no built-in gist overlap
        // opclass to include it. Overlap routing stays app-enforced.
        b.Entity<ApprovalDelegation>(e => e.ToTable("approval_delegation",
            t => t.HasCheckConstraint("ck_delegation_window", "valid_to > valid_from")));

        b.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Before).HasColumnType("jsonb");
            e.Property(x => x.After).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Entity, x.EntityId });
            e.HasIndex(x => x.At);
        });

        b.Entity<Job>(e =>
        {
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => new { x.RunAfter, x.DoneAt });
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => x.DispatchedAt);
        });

        b.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Value).HasColumnType("jsonb");
        });
        b.ApplyBranchIsolation(this);   // WP5: branch predicate as structure (AUD-ARCH-01)
    }
}

public class KernelDbContextFactory : IDesignTimeDbContextFactory<KernelDbContext>
{
    public KernelDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<KernelDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "kernel"))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new KernelDbContext(opts);
    }
}
