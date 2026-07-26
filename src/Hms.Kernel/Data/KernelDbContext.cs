using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Kernel.Data;

public class KernelDbContext(DbContextOptions<KernelDbContext> options) : DbContext(options)
{
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
            e.HasIndex(x => new { x.State, x.Type });
            e.ToTable(t => t.HasCheckConstraint("ck_approval_state",
                "state in ('pending','approved','rejected','expired')"));
        });

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
