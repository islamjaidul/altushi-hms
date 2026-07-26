using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Diagnostics.Data;

// diag.* per 03 §7. The TestOrder machine (02 §2.8) is guarded by state-checked UPDATEs.

public static class TestOrderState
{
    public const string Ordered = "ordered";
    public const string Invoiced = "invoiced";
    public const string InProgress = "in_progress";
    public const string Reported = "reported";
    public const string Delivered = "delivered";
    public const string Cancelled = "cancelled";
}

public class TestOrder
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long PatientId { get; set; }
    public long EncounterId { get; set; }
    public long? OrderingDoctorId { get; set; }
    public long? ReferrerId { get; set; }              // payout attribution (ADR-0017)
    public string State { get; set; } = TestOrderState.Ordered;
    public DateTimeOffset PromisedAt { get; set; }     // TAT promise (§9A.2)
    public long? InvoiceId { get; set; }
    public long? CancelApprovalId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

public class OrderTest
{
    public long Id { get; set; }
    public long TestOrderId { get; set; }
    public long TestCatalogId { get; set; }
    public long ChargeLineId { get; set; }
    public string State { get; set; } = "ordered";
    public bool Refunded { get; set; }                 // edge 21 partial refunds
}

public class DeliveryLog
{
    public long Id { get; set; }
    public long TestOrderId { get; set; }
    public int ReportVersion { get; set; }             // edge 22: which version left the building
    public DateTimeOffset DeliveredAt { get; set; }
    public string? CollectorNote { get; set; }
    public long DeliveredBy { get; set; }
}

public class DiagDbContext(DbContextOptions<DiagDbContext> options) : DbContext(options)
{
    public DbSet<TestOrder> Orders => Set<TestOrder>();
    public DbSet<OrderTest> OrderTests => Set<OrderTest>();
    public DbSet<DeliveryLog> Deliveries => Set<DeliveryLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("diag");
        b.Entity<TestOrder>(e => e.ToTable("test_order"));
        b.Entity<OrderTest>(e => e.ToTable("order_test"));
        b.Entity<DeliveryLog>(e => e.ToTable("delivery_log"));
    }
}

public class DiagDbContextFactory : IDesignTimeDbContextFactory<DiagDbContext>
{
    public DiagDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<DiagDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "diag"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
