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
        b.HasDefaultSchema("pharm");
        b.Entity<Company>(e => e.ToTable("company"));
        b.Entity<PharmProduct>(e =>
        {
            e.ToTable("product");
            e.HasIndex(x => new { x.Brand, x.Generic });
        });
        b.Entity<Supplier>(e => e.ToTable("supplier"));
        b.Entity<Outlet>(e => e.ToTable("outlet"));
        b.Entity<PurchaseOrder>(e => e.ToTable("purchase_order"));
        b.Entity<PurchaseOrderLine>(e => e.ToTable("purchase_order_line"));
        b.Entity<Batch>(e =>
        {
            // The invariant that makes over-selling impossible even under races (ADR-0021 #3).
            e.ToTable("batch", t => t.HasCheckConstraint("ck_batch_qty", "qty_on_hand >= 0"));
            e.HasIndex(x => new { x.OutletId, x.ProductId, x.State, x.Expiry });
        });
        b.Entity<StockMove>(e =>
        {
            e.ToTable("stock_move");
            e.HasIndex(x => new { x.BatchId, x.At });
            e.HasIndex(x => new { x.ProductId, x.At });
        });
        b.Entity<SaleAllocation>(e =>
        {
            e.ToTable("sale_allocation");
            e.HasIndex(x => x.InvoiceId);
        });
        b.Entity<IssueAllocation>(e =>
        {
            e.ToTable("issue_allocation");
            e.HasIndex(x => x.IndentId);
        });
        b.Entity<Transfer>(e => e.ToTable("transfer"));
        b.Entity<TransferLine>(e => e.ToTable("transfer_line"));
        b.Entity<TransferBatch>(e => e.ToTable("transfer_batch"));
        b.Entity<SupplierLedgerEntry>(e =>
        {
            e.ToTable("supplier_ledger");
            e.HasIndex(x => new { x.SupplierId, x.At });
        });
        b.Entity<StockAudit>(e => e.ToTable("stock_audit"));
        b.Entity<StockAuditLine>(e => e.ToTable("stock_audit_line"));
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
