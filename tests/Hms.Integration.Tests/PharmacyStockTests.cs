using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Pharmacy;
using Hms.Pharmacy.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0016 / ADR-0021: the stock kernel's invariants on real Postgres — FEFO order, the
/// expired-batch block (US11.1), the qty CHECK + row lock under concurrency, the PO machine,
/// and the approval-gated audit adjustment. These are the pharmacy's equivalents of the
/// money-spine tests.
/// </summary>
[Collection("postgres")]
public class PharmacyStockTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly StockService _stock = new(new AuditWriter(TimeProvider.System), TimeProvider.System);
    private readonly PurchaseService _purchase;

    public PharmacyStockTests(PostgresFixture pg)
    {
        _pg = pg;
        _purchase = new PurchaseService(_stock, new AuditWriter(TimeProvider.System), TimeProvider.System);
    }

    public async Task InitializeAsync()
    {
        await using var pharm = CreatePharm(null);
        await pharm.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private PharmDbContext CreatePharm(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<PharmDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "pharm"))
            : new DbContextOptionsBuilder<PharmDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "pharm")))
        .UseSnakeCaseNamingConvention().Options);

    private KernelDbContext CreateKernel(NpgsqlConnection conn) => new(
        new DbContextOptionsBuilder<KernelDbContext>().UseNpgsql(conn,
            o => o.MigrationsHistoryTable("__ef_migrations", "kernel"))
        .UseSnakeCaseNamingConvention().Options);

    private async Task<T> InTxAsync<T>(Func<PharmDbContext, KernelDbContext, Task<T>> body)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var pharm = CreatePharm(conn);
        await using var kernel = CreateKernel(conn);
        await pharm.Database.UseTransactionAsync(tx);
        await kernel.Database.UseTransactionAsync(tx);
        var result = await body(pharm, kernel);
        await tx.CommitAsync();
        return result;
    }

    private async Task<(long OutletId, long ProductId)> SetupProductAsync()
        => await InTxAsync(async (pharm, _) =>
        {
            var company = new Company { Name = "C-" + Guid.NewGuid().ToString("N")[..6] };
            pharm.Companies.Add(company);
            var outlet = new Outlet { BranchId = 1, Name = "O-" + Guid.NewGuid().ToString("N")[..6], Kind = "main" };
            pharm.Outlets.Add(outlet);
            await pharm.SaveChangesAsync();
            var product = new PharmProduct
            {
                BranchId = 1, CompanyId = company.Id, Brand = "B-" + Guid.NewGuid().ToString("N")[..6],
                Generic = "G", Strength = "500 mg", Form = "tablet", Unit = "pcs", ReorderLevel = 10,
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            pharm.Products.Add(product);
            await pharm.SaveChangesAsync();
            return (outlet.Id, product.Id);
        });

    private Task<Batch> ReceiveAsync(long outletId, long productId, string batchNo, int daysToExpiry, int qty)
        => InTxAsync((pharm, kernel) => _stock.ReceiveAsync(pharm, kernel, 1, outletId, productId,
            batchNo, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysToExpiry), qty, 5, 8, null, 7, "Tester"));

    // ---- FEFO + expiry (US11.1) --------------------------------------------

    [Fact]
    public async Task Fefo_picks_earliest_expiry_first_and_splits_across_batches()
    {
        var (outlet, product) = await SetupProductAsync();
        await ReceiveAsync(outlet, product, "LATE", 720, 100);
        var early = await ReceiveAsync(outlet, product, "EARLY", 60, 10);

        var allocations = await InTxAsync((pharm, _) =>
            _stock.AllocateFefoAsync(pharm, outlet, product, 15, "test", null, 7));

        Assert.Equal(2, allocations.Count);
        Assert.Equal("EARLY", allocations[0].BatchNo);       // earliest expiry drains first
        Assert.Equal(10, allocations[0].Qty);
        Assert.Equal("LATE", allocations[1].BatchNo);
        Assert.Equal(5, allocations[1].Qty);
        Assert.True(allocations[0].NearExpiry);              // 60 days < the 90-day window

        await using var pharm2 = CreatePharm(null);
        Assert.Equal(0, (await pharm2.Batches.SingleAsync(b => b.Id == early.Id)).QtyOnHand);
    }

    [Fact]
    public async Task Expired_batch_is_invisible_to_the_sale_even_when_it_is_the_only_stock()
    {
        var (outlet, product) = await SetupProductAsync();
        // Receive refuses expired stock at the door, so age a valid batch by SQL to simulate time.
        var batch = await ReceiveAsync(outlet, product, "EXP", 30, 50);
        await InTxAsync(async (pharm, _) =>
            await pharm.Database.ExecuteSqlAsync(
                $"UPDATE pharm.batch SET expiry = {DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)} WHERE id = {batch.Id}"));

        var ex = await Assert.ThrowsAsync<PharmacyException>(() => InTxAsync((pharm, _) =>
            _stock.AllocateFefoAsync(pharm, outlet, product, 1, "test", null, 7)));
        Assert.Contains("expired", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Receiving_an_already_expired_batch_is_refused_at_the_door()
    {
        var (outlet, product) = await SetupProductAsync();
        await Assert.ThrowsAsync<PharmacyException>(() => ReceiveAsync(outlet, product, "OLD", -1, 10));
    }

    // ---- concurrency (ADR-0021 #3) -----------------------------------------

    [Fact]
    public async Task Concurrent_sales_of_the_last_units_serialize_and_stock_never_goes_negative()
    {
        var (outlet, product) = await SetupProductAsync();
        await ReceiveAsync(outlet, product, "LAST", 365, 10);

        // Two parallel transactions each want 8 of the 10 — exactly one can win.
        var results = await Task.WhenAll(TryTakeAsync(), TryTakeAsync());

        async Task<bool> TryTakeAsync()
        {
            try { await InTxAsync((pharm, _) => _stock.AllocateFefoAsync(pharm, outlet, product, 8, "test", null, 7)); return true; }
            catch (PharmacyException) { return false; }
        }

        Assert.Single(results, ok => ok);
        await using var pharm2 = CreatePharm(null);
        var remaining = await pharm2.Batches.Where(b => b.OutletId == outlet && b.ProductId == product)
            .SumAsync(b => b.QtyOnHand);
        Assert.Equal(2, remaining);                          // 10 − 8, never negative
    }

    [Fact]
    public async Task Quarantined_stock_cannot_be_sold_and_its_exits_follow_the_state_machine()
    {
        var (outlet, product) = await SetupProductAsync();
        var batch = await ReceiveAsync(outlet, product, "DMG", 365, 20);

        await InTxAsync(async (pharm, kernel) =>
        { await _stock.QuarantineAsync(pharm, kernel, 1, batch.Id, "water damage", 7, "Tester"); return 0; });

        // US11.1: quarantined stock is unsellable by predicate.
        await Assert.ThrowsAsync<PharmacyException>(() => InTxAsync((pharm, _) =>
            _stock.AllocateFefoAsync(pharm, outlet, product, 1, "test", null, 7)));

        // Dispose without an approved request is refused (§11 ⚿).
        await Assert.ThrowsAsync<PharmacyException>(() => InTxAsync(async (pharm, kernel) =>
        { await _stock.DisposeAsync(pharm, kernel, 1, batch.Id, approvalId: 999999, 7, "Tester"); return 0; }));

        // Return to supplier zeroes the batch and credits the ledger.
        var supplierId = await InTxAsync(async (pharm, _) =>
        {
            var s = new Supplier { Name = "S-" + Guid.NewGuid().ToString("N")[..6] };
            pharm.Suppliers.Add(s);
            await pharm.SaveChangesAsync();
            return s.Id;
        });
        await InTxAsync(async (pharm, kernel) =>
        { await _stock.ReturnToSupplierAsync(pharm, kernel, 1, batch.Id, supplierId, replacement: false, 7, "Tester"); return 0; });

        await using var pharm2 = CreatePharm(null);
        var after = await pharm2.Batches.SingleAsync(b => b.Id == batch.Id);
        Assert.Equal(BatchState.Returned, after.State);
        Assert.Equal(0, after.QtyOnHand);
        var credit = await pharm2.SupplierLedger.SingleAsync(e => e.SupplierId == supplierId);
        Assert.Equal(-20 * 5, credit.Amount);                // qty × cost, negative = we owe less
    }

    // ---- purchase order machine (§11) --------------------------------------

    [Fact]
    public async Task Purchase_order_walks_its_state_machine_and_receiving_creates_the_batch()
    {
        var (outlet, product) = await SetupProductAsync();
        var supplierId = await InTxAsync(async (pharm, _) =>
        {
            var s = new Supplier { Name = "S-" + Guid.NewGuid().ToString("N")[..6] };
            pharm.Suppliers.Add(s);
            await pharm.SaveChangesAsync();
            return s.Id;
        });

        var po = await InTxAsync((pharm, kernel) => _purchase.CreateAsync(pharm, kernel, 1, supplierId,
            outlet, [new NewPoLine(product, 100, 5)], 7, "Tester"));

        // Approve⚿ requires a genuinely approved request — a bogus id is refused.
        await Assert.ThrowsAsync<PharmacyException>(() => InTxAsync(async (pharm, kernel) =>
        { await _purchase.ApproveAsync(pharm, kernel, po.Id, approvalId: 999999); return 0; }));

        var approvalId = await InTxAsync(async (pharm, kernel) =>
        {
            var request = new ApprovalRequest
            {
                BranchId = 1, Type = "purchase-order", SourceTable = "pharm.purchase_order",
                SourceId = po.Id, RequestedBy = 7, Reason = "test", RequestedAt = DateTimeOffset.UtcNow,
                State = ApprovalState.Approved, DecidedBy = 8, DecidedAt = DateTimeOffset.UtcNow,
            };
            kernel.ApprovalRequests.Add(request);
            await kernel.SaveChangesAsync();
            return request.Id;
        });

        await InTxAsync(async (pharm, kernel) => { await _purchase.ApproveAsync(pharm, kernel, po.Id, approvalId); return 0; });
        await InTxAsync(async (pharm, _) => { await _purchase.OrderAsync(pharm, po.Id); return 0; });

        var lineId = await InTxAsync(async (pharm, _) =>
            (await pharm.PurchaseOrderLines.SingleAsync(l => l.PurchaseOrderId == po.Id)).Id);

        // Partial receive → PartiallyReceived; full receive → Received; over-receive refused.
        await InTxAsync((pharm, kernel) => _purchase.ReceiveLineAsync(pharm, kernel, 1, po.Id, lineId,
            "PO-B1", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(365), 40, 5, 8, 7, "Tester"));
        await using (var check = CreatePharm(null))
            Assert.Equal(PoState.PartiallyReceived, (await check.PurchaseOrders.SingleAsync(p => p.Id == po.Id)).State);

        await Assert.ThrowsAsync<PharmacyException>(() => InTxAsync((pharm, kernel) =>
            _purchase.ReceiveLineAsync(pharm, kernel, 1, po.Id, lineId,
                "PO-B2", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(365), 70, 5, 8, 7, "Tester")));

        await InTxAsync((pharm, kernel) => _purchase.ReceiveLineAsync(pharm, kernel, 1, po.Id, lineId,
            "PO-B2", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(365), 60, 5, 8, 7, "Tester"));
        await InTxAsync(async (pharm, _) => { await _purchase.CloseAsync(pharm, po.Id); return 0; });

        await using var pharm2 = CreatePharm(null);
        Assert.Equal(PoState.Closed, (await pharm2.PurchaseOrders.SingleAsync(p => p.Id == po.Id)).State);
        Assert.Equal(100, await pharm2.Batches.Where(b => b.PurchaseOrderId == po.Id).SumAsync(b => b.QtyOnHand));
        // Supplier ledger carries the payable at actual cost.
        Assert.Equal(100 * 5, await pharm2.SupplierLedger.Where(e => e.SupplierId == supplierId).SumAsync(e => e.Amount));
        // A received order cannot be cancelled — only closed (already checked by state above).
    }

    // ---- stock audit (§11 ⚿) ------------------------------------------------

    [Fact]
    public async Task Audit_adjustment_posts_only_after_approval_and_writes_moves_not_edits()
    {
        var (outlet, product) = await SetupProductAsync();
        var batch = await ReceiveAsync(outlet, product, "CNT", 365, 50);

        var auditId = await InTxAsync(async (pharm, _) =>
        {
            var audit = new StockAudit { BranchId = 1, OutletId = outlet, StartedAt = DateTimeOffset.UtcNow, StartedBy = 7 };
            pharm.StockAudits.Add(audit);
            await pharm.SaveChangesAsync();
            pharm.StockAuditLines.Add(new StockAuditLine
            { StockAuditId = audit.Id, BatchId = batch.Id, ProductId = product, SystemQty = 50, CountedQty = 47 });
            audit.State = AuditState.VarianceListed;
            await pharm.SaveChangesAsync();
            return audit.Id;
        });

        // Posting before approval is refused (§11: Adjustment Approved⚿ precedes Posted).
        await Assert.ThrowsAsync<PharmacyException>(() => InTxAsync(async (pharm, kernel) =>
        { await _stock.PostAuditAsync(pharm, kernel, 1, auditId, 7, "Tester"); return 0; }));

        await InTxAsync(async (pharm, _) =>
        {
            var audit = await pharm.StockAudits.SingleAsync(a => a.Id == auditId);
            audit.State = AuditState.Approved;
            await pharm.SaveChangesAsync();
            return 0;
        });
        await InTxAsync(async (pharm, kernel) =>
        { await _stock.PostAuditAsync(pharm, kernel, 1, auditId, 7, "Tester"); return 0; });

        await using var pharm2 = CreatePharm(null);
        Assert.Equal(47, (await pharm2.Batches.SingleAsync(b => b.Id == batch.Id)).QtyOnHand);
        var adjustment = await pharm2.StockMoves.SingleAsync(m => m.Kind == MoveKind.Adjustment && m.BatchId == batch.Id);
        Assert.Equal(-3, adjustment.Qty);                    // the ledger records the delta, attributably
        Assert.Equal(AuditState.Posted, (await pharm2.StockAudits.SingleAsync(a => a.Id == auditId)).State);
    }
}
