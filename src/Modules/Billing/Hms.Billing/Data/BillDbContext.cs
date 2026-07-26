using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Billing.Data;

// bill.* per 03 §4. Money is bigint whole taka (C3). No DELETE grants for the app role (C5) —
// enforced by the grants block in the InitBill migration.

public class Encounter
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long PatientId { get; set; }
    public DateOnly OnDate { get; set; }
    public required string Type { get; set; }          // OPD|ER
    public long? DoctorId { get; set; }
    public long CounterId { get; set; }
    public string State { get; set; } = "open";
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

public class Counter
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }          // front-desk|diagnostics|er
}

public static class SessionState
{
    public const string Opened = "opened";
    public const string Active = "active";
    public const string ClosePending = "close_pending";
    public const string Closed = "closed";
    public const string Reopened = "reopened";
}

public class CounterSession
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long CounterId { get; set; }
    public long OperatorId { get; set; }
    public DateOnly BusinessDay { get; set; }          // ADR-0004 boundary rule
    public DateTimeOffset OpenedAt { get; set; }
    public long OpeningFloat { get; set; }
    public string State { get; set; } = SessionState.Opened;
    public DateTimeOffset? ClosedAt { get; set; }
    public long? CountedCash { get; set; }
    public long? ExpectedCash { get; set; }
    public long? Variance { get; set; }
    public long? CloseApprovedBy { get; set; }
    public bool CarryClosed { get; set; }              // edge 17
}

/// <summary>The C2 spine (02 §2.3): polymorphic parent via XOR check; folio arrives post-MVP.</summary>
public class ChargeLine
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long? EncounterId { get; set; }
    public long? FolioId { get; set; }                 // post-MVP parent (M6 seam)
    public required string SourceModule { get; set; }
    public required string CatalogKind { get; set; }   // service|test
    public long CatalogId { get; set; }
    public required string DescriptionSnapshot { get; set; }
    public int Qty { get; set; }
    public long UnitPrice { get; set; }                // resolved price (C6)
    public long? RateVersionId { get; set; }           // proof of resolution (S3 wires real versions)
    public long Amount { get; set; }                   // qty*unit_price
    public long? DoctorId { get; set; }
    public long? ReferrerId { get; set; }              // payout attribution (ADR-0017)
    public long? TestOrderId { get; set; }
    public long? InvoiceId { get; set; }               // null = unbilled (the §9A.2 seam)
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

public static class InvoiceState
{
    public const string Draft = "draft";
    public const string Billed = "billed";
    public const string PartiallyPaid = "partially_paid";
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";
    public const string Refunded = "refunded";
}

public class Invoice
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string InvoiceNo { get; set; }
    public required string FiscalYear { get; set; }
    public long PatientId { get; set; }
    public long EncounterId { get; set; }
    public long CounterSessionId { get; set; }
    public long Gross { get; set; }
    public long Discount { get; set; }
    public long? DiscountApprovalId { get; set; }
    public long Tax { get; set; }                      // dormant (ADR-0018)
    public string? TaxCode { get; set; }
    public long Net { get; set; }
    public short RoundingAdj { get; set; }             // 03 §6: MVP keeps 0; column is the audit seam
    public string State { get; set; } = InvoiceState.Billed;
    public int Version { get; set; }                   // optimistic (ADR-0015 #3)
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>Frozen copy of charge lines at billing (C6 — recomputation never changes a document).</summary>
public class InvoiceLine
{
    public long Id { get; set; }
    public long InvoiceId { get; set; }
    public long ChargeLineId { get; set; }
    public required string DescriptionSnapshot { get; set; }
    public int Qty { get; set; }
    public long UnitPrice { get; set; }
    public long Amount { get; set; }
    public long? RateVersionId { get; set; }
    public long? DoctorId { get; set; }
    public long? ReferrerId { get; set; }
}

public class Receipt
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string ReceiptNo { get; set; }
    public long InvoiceId { get; set; }
    public long CounterSessionId { get; set; }
    public long Amount { get; set; }                   // negative = refund (edge 20)
    public required string Tender { get; set; }        // cash|card|bkash|nagad|corporate
    public string? TenderRef { get; set; }
    public long OperatorId { get; set; }
    public DateTimeOffset At { get; set; }
    public long? RefundOfReceipt { get; set; }
    public long? ApprovalId { get; set; }
}

public class Due
{
    public long InvoiceId { get; set; }                // PK
    public long Balance { get; set; }
    public string? LastFollowup { get; set; }          // jsonb
}

public class DayCloseSummary
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long CounterSessionId { get; set; }
    public DateOnly BusinessDay { get; set; }
    public required string DeptSplit { get; set; }     // jsonb
    public required string TenderTotals { get; set; }  // jsonb
    public long Gross { get; set; }
    public long Discount { get; set; }
    public long Net { get; set; }
    public long DueCreated { get; set; }
    public long DueCollected { get; set; }
    public long Refunds { get; set; }
    public long Variance { get; set; }
    public int Version { get; set; } = 1;
    public long? Supersedes { get; set; }              // reopen appends (02 §2.6)
}

public class BillDbContext(DbContextOptions<BillDbContext> options) : DbContext(options)
{
    public DbSet<Encounter> Encounters => Set<Encounter>();
    public DbSet<Counter> Counters => Set<Counter>();
    public DbSet<CounterSession> Sessions => Set<CounterSession>();
    public DbSet<ChargeLine> ChargeLines => Set<ChargeLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<Due> Dues => Set<Due>();
    public DbSet<DayCloseSummary> DayCloses => Set<DayCloseSummary>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("bill");
        b.Entity<Encounter>(e => e.ToTable("encounter"));
        b.Entity<Counter>(e => e.ToTable("counter"));
        b.Entity<CounterSession>(e => e.ToTable("counter_session"));
        b.Entity<ChargeLine>(e => e.ToTable("charge_line", t => t.HasCheckConstraint(
            "ck_charge_parent", "num_nonnulls(encounter_id, folio_id) = 1")));
        b.Entity<Invoice>(e =>
        {
            e.ToTable("invoice", t => t.HasCheckConstraint(
                "ck_invoice_identity", "net = gross - discount + tax + rounding_adj"));   // G6, structural
            e.HasIndex(x => x.InvoiceNo).IsUnique();
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        b.Entity<InvoiceLine>(e => e.ToTable("invoice_line"));
        b.Entity<Receipt>(e =>
        {
            e.ToTable("receipt");
            e.HasIndex(x => x.ReceiptNo).IsUnique();
            e.HasIndex(x => x.CounterSessionId);
        });
        b.Entity<Due>(e =>
        {
            e.ToTable("due");
            e.HasKey(x => x.InvoiceId);
        });
        b.Entity<DayCloseSummary>(e =>
        {
            e.ToTable("day_close_summary");
            e.Property(x => x.DeptSplit).HasColumnType("jsonb");
            e.Property(x => x.TenderTotals).HasColumnType("jsonb");
            e.HasIndex(x => new { x.CounterSessionId, x.Version }).IsUnique();
        });
    }
}

public class BillDbContextFactory : IDesignTimeDbContextFactory<BillDbContext>
{
    public BillDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<BillDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "bill"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
