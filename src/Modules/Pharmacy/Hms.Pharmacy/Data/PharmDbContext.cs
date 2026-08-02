using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Pharmacy.Data;

// pharm.* per ADR-0021: the stock ledger (stock_move) is append-only truth for history;
// batch.qty_on_hand is the row-locked truth for "can I sell this now"; both move in one
// transaction. §11 machines: Purchase Order, Stock Batch, Stock Audit.

public static class PoState
{
    public const string Requested = "requested";
    public const string Approved = "approved";           // ⚿ approval engine
    public const string Ordered = "ordered";
    public const string PartiallyReceived = "partially_received";
    public const string Received = "received";
    public const string Closed = "closed";
    public const string Cancelled = "cancelled";
}

public static class BatchState
{
    public const string InStock = "instock";
    public const string Quarantined = "quarantined";     // expired or damaged, unsellable
    public const string Returned = "returned";           // went back to the supplier
    public const string Disposed = "disposed";           // written off ⚿, logged
}

public static class TransferState
{
    public const string Indent = "indent";
    public const string Sent = "sent";
    public const string Received = "received";
    public const string Cancelled = "cancelled";
}

public static class AuditState
{
    public const string CountStarted = "count_started";
    public const string VarianceListed = "variance_listed";
    public const string Approved = "approved";           // ⚿ adjustment approval
    public const string Posted = "posted";
}

/// <summary>Kinds a stock move can carry — the append-only ledger's vocabulary.</summary>
public static class MoveKind
{
    public const string Receive = "receive";
    public const string Sale = "sale";
    public const string SaleReturn = "sale_return";
    public const string TransferOut = "transfer_out";
    public const string TransferIn = "transfer_in";
    public const string Quarantine = "quarantine";       // qty 0 marker: state change, stock kept
    public const string SupplierReturn = "supplier_return";
    public const string Dispose = "dispose";
    public const string Adjustment = "adjustment";       // stock audit posting ⚿
    public const string IndoorIssue = "indoor_issue";    // ward indent issue (spec 0017, 5A-9)
    public const string IndoorReturn = "indoor_return";  // discharge-time return
}

public class Company
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
}

public class PharmProduct
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long CompanyId { get; set; }
    public required string Brand { get; set; }
    public required string Generic { get; set; }
    public required string Strength { get; set; }        // "500 mg"
    public required string Form { get; set; }            // tablet|syrup|injection|...
    public required string Unit { get; set; }            // pcs|bottle|vial|strip
    public int ReorderLevel { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

public class Supplier
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public string? Phone { get; set; }
    public bool Active { get; set; } = true;
}

public class Outlet
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }            // main|sub (5A-11 multi-outlet)
    public bool Active { get; set; } = true;
}

public class PurchaseOrder
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long SupplierId { get; set; }
    public long OutletId { get; set; }
    public string State { get; set; } = PoState.Requested;
    public long? ApprovalId { get; set; }                // ⚿ Requested → Approved
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

public class PurchaseOrderLine
{
    public long Id { get; set; }
    public long PurchaseOrderId { get; set; }
    public long ProductId { get; set; }
    public int Qty { get; set; }
    public int ReceivedQty { get; set; }
    public long ExpectedCost { get; set; }               // per unit, whole taka
}

public class Batch
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long OutletId { get; set; }
    public long ProductId { get; set; }
    public required string BatchNo { get; set; }
    public DateOnly Expiry { get; set; }
    public int QtyOnHand { get; set; }                   // CHECK >= 0; mutated only under FOR UPDATE
    public long Cost { get; set; }                       // per unit at receipt (valuation)
    public long Mrp { get; set; }                        // per unit sale price — immutable after receipt
    public string State { get; set; } = BatchState.InStock;
    public string? StateReason { get; set; }             // why quarantined/disposed
    public long? PurchaseOrderId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public long ReceivedBy { get; set; }
}

/// <summary>Append-only. No UPDATE/DELETE grant for the app role (ADR-0021 #2).</summary>
public class StockMove
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long OutletId { get; set; }
    public long BatchId { get; set; }
    public long ProductId { get; set; }
    public required string Kind { get; set; }
    public int Qty { get; set; }                         // signed: receive +, sale −
    public string? RefTable { get; set; }
    public long? RefId { get; set; }
    public string? Reason { get; set; }
    public long ActorId { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>Pins a sold charge line to the batches that backed it — refunds restock exactly
/// these, and profit is (MRP − cost) × qty without guessing (ADR-0021 #5).</summary>
public class SaleAllocation
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long InvoiceId { get; set; }
    public long ChargeLineId { get; set; }
    public long BatchId { get; set; }
    public long ProductId { get; set; }
    public int Qty { get; set; }
    public long UnitMrp { get; set; }
    public long UnitCost { get; set; }
    public int RefundedQty { get; set; }
}

/// <summary>What a ward indent took, per folio charge line — the indoor twin of
/// <see cref="SaleAllocation"/>; discharge-time returns restock these exact batches.</summary>
public class IssueAllocation
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    /// <summary>ipd.indent id — opaque cross-module reference, same as InvoiceId above.</summary>
    public long IndentId { get; set; }
    public long ChargeLineId { get; set; }
    public long BatchId { get; set; }
    public long ProductId { get; set; }
    public int Qty { get; set; }
    public long UnitMrp { get; set; }
    public long UnitCost { get; set; }
    public int ReturnedQty { get; set; }
}

public class Transfer
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long FromOutletId { get; set; }
    public long ToOutletId { get; set; }
    public string State { get; set; } = TransferState.Indent;
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public long? ReceivedBy { get; set; }
}

public class TransferLine
{
    public long Id { get; set; }
    public long TransferId { get; set; }
    public long ProductId { get; set; }
    public int RequestedQty { get; set; }
    public int SentQty { get; set; }
}

/// <summary>Batch detail captured at send so the receiving outlet recreates identical batch
/// identity (batch no, expiry, cost, MRP) — the outlet transfer ledger of 5A-11.</summary>
public class TransferBatch
{
    public long Id { get; set; }
    public long TransferId { get; set; }
    public long SourceBatchId { get; set; }
    public long ProductId { get; set; }
    public required string BatchNo { get; set; }
    public DateOnly Expiry { get; set; }
    public int Qty { get; set; }
    public long Cost { get; set; }
    public long Mrp { get; set; }
}

/// <summary>Append-only supplier money trail: purchases raise payable (+), payments and
/// return credits reduce it (−). §5 M11 [M] supplier ledger.</summary>
public class SupplierLedgerEntry
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long SupplierId { get; set; }
    public required string Kind { get; set; }            // purchase|payment|return_credit|replacement
    public long Amount { get; set; }                     // signed, whole taka
    public string? RefTable { get; set; }
    public long? RefId { get; set; }
    public string? Note { get; set; }
    public long ActorId { get; set; }
    public DateTimeOffset At { get; set; }
}

public class StockAudit
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long OutletId { get; set; }
    public string State { get; set; } = AuditState.CountStarted;
    public long? ApprovalId { get; set; }                // ⚿ adjustment approval
    public DateTimeOffset StartedAt { get; set; }
    public long StartedBy { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
}

public class StockAuditLine
{
    public long Id { get; set; }
    public long StockAuditId { get; set; }
    public long BatchId { get; set; }
    public long ProductId { get; set; }
    public int SystemQty { get; set; }
    public int CountedQty { get; set; }
}

public class PharmDbContext(DbContextOptions<PharmDbContext> options) : DbContext(options)
{

    /// <summary>The branch this context's queries are isolated to (spec 0039 WP5). Captured
    /// from the ambient request scope at construction; every entity carrying a BranchId is
    /// filtered to it structurally — see BranchIsolation.</summary>
    public long CurrentBranch { get; set; } = Hms.Kernel.Data.BranchScope.Current;
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<PharmProduct> Products => Set<PharmProduct>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Outlet> Outlets => Set<Outlet>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<StockMove> StockMoves => Set<StockMove>();
    public DbSet<SaleAllocation> SaleAllocations => Set<SaleAllocation>();
    public DbSet<IssueAllocation> IssueAllocations => Set<IssueAllocation>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<TransferLine> TransferLines => Set<TransferLine>();
    public DbSet<TransferBatch> TransferBatches => Set<TransferBatch>();
    public DbSet<SupplierLedgerEntry> SupplierLedger => Set<SupplierLedgerEntry>();
    public DbSet<StockAudit> StockAudits => Set<StockAudit>();
    public DbSet<StockAuditLine> StockAuditLines => Set<StockAuditLine>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Spec 0039 WP2: domain-value CHECKs, state CHECKs from the *State constants above,
        // intra-schema FKs, HasMaxLength and xmin tokens — same posture as BillDbContext
        // (additive only, NOT VALID in the migration for pre-existing tables).
        b.HasDefaultSchema("pharm");
        b.Entity<Company>(e =>
        {
            e.ToTable("company");
            e.Property(x => x.Name).HasMaxLength(200);
        });
        b.Entity<PharmProduct>(e =>
        {
            e.ToTable("product");
            e.Property(x => x.Brand).HasMaxLength(200);
            e.Property(x => x.Generic).HasMaxLength(200);
            e.Property(x => x.Strength).HasMaxLength(40);
            e.Property(x => x.Form).HasMaxLength(40);
            e.Property(x => x.Unit).HasMaxLength(40);
            e.HasIndex(x => new { x.Brand, x.Generic });
            e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId);
        });
        b.Entity<Supplier>(e =>
        {
            e.ToTable("supplier");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(20);
        });
        b.Entity<Outlet>(e =>
        {
            e.ToTable("outlet");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Kind).HasMaxLength(40);
        });
        b.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("purchase_order", t => t.HasCheckConstraint("ck_purchase_order_state",
                "state IN ('requested','approved','ordered','partially_received',"
                + "'received','closed','cancelled')"));
            e.Property(x => x.State).HasMaxLength(40);
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
        });
        b.Entity<PurchaseOrderLine>(e =>
        {
            // The database backstop for the over-receipt TOCTOU at PurchaseService (audit §1b).
            e.ToTable("purchase_order_line", t => t.HasCheckConstraint("ck_po_line_qty",
                "qty > 0 AND expected_cost >= 0 AND received_qty BETWEEN 0 AND qty"));
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId);
        });
        b.Entity<Batch>(e =>
        {
            // The invariant that makes over-selling impossible even under races (ADR-0021 #3).
            e.ToTable("batch", t =>
            {
                t.HasCheckConstraint("ck_batch_qty", "qty_on_hand >= 0");
                t.HasCheckConstraint("ck_batch_state",
                    "state IN ('instock','quarantined','returned','disposed')");
                t.HasCheckConstraint("ck_batch_money", "cost >= 0 AND mrp >= 0");
            });
            e.Property(x => x.BatchNo).HasMaxLength(40);
            e.Property(x => x.State).HasMaxLength(40);
            e.Property(x => x.StateReason).HasMaxLength(4000);
            e.HasIndex(x => new { x.OutletId, x.ProductId, x.State, x.Expiry });
            // Products/Reports filter without outlet_id, skipping the index above's leading
            // column (audit §5).
            e.HasIndex(x => new { x.ProductId, x.State, x.Expiry });
            e.HasOne<PharmProduct>().WithMany().HasForeignKey(x => x.ProductId);
            e.HasOne<Outlet>().WithMany().HasForeignKey(x => x.OutletId);
            e.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId);
            e.Property<uint>("xmin").IsRowVersion();
        });
        b.Entity<StockMove>(e =>
        {
            // No qty CHECK: signed by design, and legitimately 0 for the quarantine marker
            // and for disposing an already-empty quarantined batch (StockService.DisposeAsync).
            e.ToTable("stock_move");
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.RefTable).HasMaxLength(40);
            e.Property(x => x.Reason).HasMaxLength(4000);
            e.HasIndex(x => new { x.BatchId, x.At });
            e.HasIndex(x => new { x.ProductId, x.At });
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId);
            e.HasOne<PharmProduct>().WithMany().HasForeignKey(x => x.ProductId);
        });
        b.Entity<SaleAllocation>(e =>
        {
            e.ToTable("sale_allocation", t =>
            {
                t.HasCheckConstraint("ck_sale_allocation_qty",
                    "qty > 0 AND refunded_qty BETWEEN 0 AND qty");
                t.HasCheckConstraint("ck_sale_allocation_money",
                    "unit_mrp >= 0 AND unit_cost >= 0");
            });
            e.HasIndex(x => x.InvoiceId);
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId);
        });
        b.Entity<IssueAllocation>(e =>
        {
            e.ToTable("issue_allocation", t => t.HasCheckConstraint(
                "ck_issue_allocation_qty", "returned_qty BETWEEN 0 AND qty"));
            e.HasIndex(x => x.IndentId);
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId);
        });
        b.Entity<Transfer>(e =>
        {
            e.ToTable("transfer", t =>
            {
                t.HasCheckConstraint("ck_transfer_state",
                    "state IN ('indent','sent','received','cancelled')");
                t.HasCheckConstraint("ck_transfer_sent",
                    "state <> 'sent' OR sent_at IS NOT NULL");
                t.HasCheckConstraint("ck_transfer_received",
                    "state <> 'received' "
                    + "OR (received_at IS NOT NULL AND received_by IS NOT NULL)");
            });
            e.Property(x => x.State).HasMaxLength(40);
        });
        b.Entity<TransferLine>(e =>
        {
            e.ToTable("transfer_line", t => t.HasCheckConstraint(
                "ck_transfer_line_qty", "sent_qty BETWEEN 0 AND requested_qty"));
            e.HasOne<Transfer>().WithMany().HasForeignKey(x => x.TransferId);
        });
        b.Entity<TransferBatch>(e =>
        {
            e.ToTable("transfer_batch");
            e.Property(x => x.BatchNo).HasMaxLength(40);
        });
        b.Entity<SupplierLedgerEntry>(e =>
        {
            e.ToTable("supplier_ledger");
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.RefTable).HasMaxLength(40);
            e.Property(x => x.Note).HasMaxLength(4000);
            e.HasIndex(x => new { x.SupplierId, x.At });
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
        });
        b.Entity<StockAudit>(e =>
        {
            e.ToTable("stock_audit", t => t.HasCheckConstraint("ck_stock_audit_state",
                "state IN ('count_started','variance_listed','approved','posted')"));
            e.Property(x => x.State).HasMaxLength(40);
        });
        b.Entity<StockAuditLine>(e =>
        {
            e.ToTable("stock_audit_line", t => t.HasCheckConstraint(
                "ck_stock_audit_line_counted", "counted_qty >= 0"));
            e.HasOne<StockAudit>().WithMany().HasForeignKey(x => x.StockAuditId);
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

public class PharmDbContextFactory : IDesignTimeDbContextFactory<PharmDbContext>
{
    public PharmDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<PharmDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "pharm"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
