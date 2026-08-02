using Hms.Kernel.Data;
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

/// <summary>
/// The ways money moves at a counter. Day-close groups receipts by this string exactly
/// (<c>tenderTotals.GetValueOrDefault("cash")</c>), so a stray "Cash" is a different drawer and a
/// silent cash variance — which is why the set is named once here rather than in each dropdown.
/// </summary>
public static class Tenders
{
    public const string Cash = "cash";
    public const string Card = "card";
    public const string Bkash = "bkash";
    public const string Nagad = "nagad";
    public const string Corporate = "corporate";

    public static readonly string[] All = [Cash, Card, Bkash, Nagad, Corporate];

    public static bool IsKnown(string? tender) => Array.IndexOf(All, tender) >= 0;
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
    public long? EncounterId { get; set; }
    public long? FolioId { get; set; }                 // settlement parent (M6, spec 0017)
    public long CounterSessionId { get; set; }
    public long Gross { get; set; }
    public long Discount { get; set; }
    public long? DiscountApprovalId { get; set; }
    public long Tax { get; set; }                      // dormant (ADR-0018)
    public string? TaxCode { get; set; }
    public long Net { get; set; }
    public short RoundingAdj { get; set; }             // 03 §6: MVP keeps 0; column is the audit seam
    public string State { get; set; } = InvoiceState.Billed;
    /// <summary>
    /// One prepared bill = one invoice (spec 0021). The issuing screen mints this per render
    /// and carries it through cart postbacks; a double-click, a browser resend or a retried
    /// request arrives with the same token and resolves to the invoice that already exists.
    /// The UNIQUE index is what makes that true under concurrency — the check alone is a race.
    /// </summary>
    public Guid? SubmissionToken { get; set; }
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
    public long? InvoiceId { get; set; }
    public long? FolioId { get; set; }                 // folio-parented = advance (spec 0017)
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
    public long AdvancesTaken { get; set; }            // folio advances through this session (spec 0017)
    public long Refunds { get; set; }
    public long Variance { get; set; }
    public int Version { get; set; } = 1;
    public long? Supersedes { get; set; }              // reopen appends (02 §2.6)
}

public class BillDbContext(DbContextOptions<BillDbContext> options) : DbContext(options)
{

    /// <summary>The branch this context's queries are isolated to (spec 0039 WP5). Captured
    /// from the ambient request scope at construction; every entity carrying a BranchId is
    /// filtered to it structurally — see BranchIsolation.</summary>
    public long CurrentBranch { get; set; } = Hms.Kernel.Data.BranchScope.Current;
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
        // Spec 0039 WP2: domain-value CHECKs, intra-schema FKs and hot-path indexes. Every CHECK
        // and FK added to a pre-existing table ships NOT VALID in the migration (hand-edited):
        // legacy rows are retained under hard rule 4, new writes are constrained — the additive
        // posture ADR-0027 records.
        b.HasDefaultSchema("bill");
        b.Entity<Encounter>(e =>
        {
            e.ToTable("encounter", t => t.HasCheckConstraint(
                "ck_encounter_state", "state IN ('open','closed')"));
            e.Property(x => x.Type).HasMaxLength(40);
            e.Property(x => x.State).HasMaxLength(40);
            // The get-or-create that runs on nearly every counter action, and the EMR queue's
            // "today's OPD visits" (AUD-ARCH-04).
            e.HasIndex(x => new { x.PatientId, x.OnDate });
            e.HasIndex(x => new { x.OnDate, x.Type });
            e.HasOne<Counter>().WithMany().HasForeignKey(x => x.CounterId);
        });
        b.Entity<Counter>(e =>
        {
            e.ToTable("counter");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Kind).HasMaxLength(40);
        });
        b.Entity<CounterSession>(e =>
        {
            e.ToTable("counter_session", t =>
            {
                t.HasCheckConstraint("ck_session_state",
                    "state IN ('opened','active','close_pending','closed','reopened')");
                // A closed session with no cash count is a hole in the day's money trail.
                t.HasCheckConstraint("ck_session_closed_complete",
                    "state <> 'closed' OR (closed_at IS NOT NULL AND counted_cash IS NOT NULL "
                    + "AND expected_cash IS NOT NULL AND variance IS NOT NULL)");
                t.HasCheckConstraint("ck_session_opening_float", "opening_float >= 0");
            });
            e.Property(x => x.State).HasMaxLength(40);
            e.HasOne<Counter>().WithMany().HasForeignKey(x => x.CounterId);
        });
        b.Entity<ChargeLine>(e =>
        {
            e.ToTable("charge_line", t =>
            {
                t.HasCheckConstraint(
                    "ck_charge_parent", "num_nonnulls(encounter_id, folio_id) = 1");
                // qty 999999 once posted 29,99,99,700 Tk to a folio (AUD-VAL-14g). Signed:
                // an indent return posts NEGATIVE lines — a correction is a reversal (hard
                // rule 4), so the bound is magnitude, with zero excluded.
                t.HasCheckConstraint("ck_charge_line_qty",
                    "qty <> 0 AND qty BETWEEN -9999 AND 9999");
                t.HasCheckConstraint("ck_charge_line_money",
                    "unit_price >= 0 AND amount = qty::bigint * unit_price");
            });
            e.Property(x => x.SourceModule).HasMaxLength(40);
            e.Property(x => x.CatalogKind).HasMaxLength(40);
            e.Property(x => x.DescriptionSnapshot).HasMaxLength(200);
            // The hottest predicates in the product: the unbilled cart by encounter or folio,
            // and an invoice's lines (AUD-ARCH-04).
            e.HasIndex(x => x.EncounterId).HasFilter("invoice_id IS NULL")
                .HasDatabaseName("ix_charge_line_unbilled_encounter");
            e.HasIndex(x => x.FolioId).HasFilter("invoice_id IS NULL")
                .HasDatabaseName("ix_charge_line_unbilled_folio");
            e.HasIndex(x => x.InvoiceId);
            e.HasOne<Encounter>().WithMany().HasForeignKey(x => x.EncounterId);
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId);
        });
        b.Entity<Invoice>(e =>
        {
            e.ToTable("invoice", t =>
            {
                t.HasCheckConstraint(
                    "ck_invoice_identity", "net = gross - discount + tax + rounding_adj");   // G6, structural
                t.HasCheckConstraint(
                    "ck_invoice_parent", "num_nonnulls(encounter_id, folio_id) = 1");        // M6 seam
                // The identity alone is satisfied by gross = -1000, or by a discount larger
                // than the bill; these bound the values themselves (AUD-VAL-03/06).
                t.HasCheckConstraint("ck_invoice_bounds",
                    "gross >= 0 AND tax >= 0 AND discount BETWEEN 0 AND gross");
                t.HasCheckConstraint("ck_invoice_state",
                    "state IN ('draft','billed','partially_paid','paid','cancelled','refunded')");
            });
            e.Property(x => x.InvoiceNo).HasMaxLength(40);
            e.Property(x => x.FiscalYear).HasMaxLength(40);
            e.Property(x => x.TaxCode).HasMaxLength(40);
            e.Property(x => x.State).HasMaxLength(40);
            e.HasIndex(x => x.InvoiceNo).IsUnique();
            e.HasIndex(x => x.SubmissionToken).IsUnique()
                .HasFilter("submission_token IS NOT NULL");     // spec 0021: one submit, one invoice
            e.HasIndex(x => x.CounterSessionId);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.CreatedAt);
            // AUD-ARCH-02: `Version` was declared a token and never assigned — both racers
            // matched and both won. xmin is maintained by PostgreSQL itself on every write, so
            // it cannot be forgotten again (HR set the precedent). `Version` stays as a plain
            // column: dropping it is not additive.
            e.Property<uint>("xmin").IsRowVersion();
            e.HasOne<CounterSession>().WithMany().HasForeignKey(x => x.CounterSessionId);
        });
        b.Entity<InvoiceLine>(e =>
        {
            e.ToTable("invoice_line", t =>
            {
                // Signed for the same reason as charge_line: a settlement freezes reversal
                // lines too.
                t.HasCheckConstraint("ck_invoice_line_qty",
                    "qty <> 0 AND qty BETWEEN -9999 AND 9999");
                t.HasCheckConstraint("ck_invoice_line_money",
                    "unit_price >= 0 AND amount = qty::bigint * unit_price");
            });
            e.Property(x => x.DescriptionSnapshot).HasMaxLength(200);
            e.HasIndex(x => x.InvoiceId);
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId);
            e.HasOne<ChargeLine>().WithMany().HasForeignKey(x => x.ChargeLineId);
        });
        b.Entity<Receipt>(e =>
        {
            e.ToTable("receipt", t =>
            {
                t.HasCheckConstraint(
                    "ck_receipt_parent", "num_nonnulls(invoice_id, folio_id) = 1");
                // Day-close groups the drawer by this exact string: a tender outside the set is
                // a silent cash variance (AUD-VAL-15f: 12 receipts once tendered 'bitcoin').
                t.HasCheckConstraint("ck_receipt_tender",
                    "tender IN ('cash','card','bkash','nagad','corporate')");
            });
            e.Property(x => x.ReceiptNo).HasMaxLength(40);
            e.Property(x => x.Tender).HasMaxLength(40);
            e.Property(x => x.TenderRef).HasMaxLength(40);
            e.HasIndex(x => x.ReceiptNo).IsUnique();
            e.HasIndex(x => x.CounterSessionId);
            e.HasIndex(x => x.FolioId);
            e.HasIndex(x => x.InvoiceId);
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId);
        });
        b.Entity<Due>(e =>
        {
            e.ToTable("due", t => t.HasCheckConstraint("ck_due_balance", "balance >= 0"));
            e.HasKey(x => x.InvoiceId);
            e.Property(x => x.LastFollowup).HasMaxLength(4000);
            e.HasOne<Invoice>().WithOne().HasForeignKey<Due>(x => x.InvoiceId);
        });
        b.Entity<DayCloseSummary>(e =>
        {
            e.ToTable("day_close_summary");
            e.Property(x => x.DeptSplit).HasColumnType("jsonb");
            e.Property(x => x.TenderTotals).HasColumnType("jsonb");
            e.HasIndex(x => new { x.CounterSessionId, x.Version }).IsUnique();
            e.HasOne<CounterSession>().WithMany().HasForeignKey(x => x.CounterSessionId);
        });
        // Hard rule 4: the schema must never delete on its own. Every relationship this model
        // declares refuses a parent delete instead of cascading it — corrections are reversals,
        // and a cascade is a delete machine (spec 0039 WP2, ADR-0028).
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
        b.ApplyBranchIsolation(this);   // WP5: branch predicate as structure (AUD-ARCH-01)
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
