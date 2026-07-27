using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Pharmacy.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Pharmacy;

public sealed record NewPoLine(long ProductId, int Qty, long ExpectedCost);

/// <summary>
/// §11 Purchase Order machine: Requested → Approved⚿ → Ordered → Partially Received →
/// Received → Closed, with Cancelled as the exit. Transitions are state-guarded UPDATEs
/// (ADR-0015 #4): a concurrent actor loses with a message, never a silent double-move.
/// Receiving delegates batch creation to <see cref="StockService.ReceiveAsync"/> and raises
/// the supplier's payable on the ledger.
/// </summary>
public sealed class PurchaseService(StockService stock, AuditWriter audit, TimeProvider clock)
{
    public async Task<PurchaseOrder> CreateAsync(
        PharmDbContext pharm, KernelDbContext kernel, long branchId, long supplierId, long outletId,
        IReadOnlyList<NewPoLine> lines, long actorId, string actorName, CancellationToken ct = default)
    {
        if (lines.Count == 0) throw new PharmacyException("A purchase order needs at least one line.");
        if (lines.Any(l => l.Qty <= 0 || l.ExpectedCost < 0))
            throw new PharmacyException("Quantities must be positive and costs non-negative.");

        var po = new PurchaseOrder
        {
            BranchId = branchId, SupplierId = supplierId, OutletId = outletId,
            CreatedAt = clock.GetUtcNow(), CreatedBy = actorId,
        };
        pharm.PurchaseOrders.Add(po);
        await pharm.SaveChangesAsync(ct);
        foreach (var l in lines)
            pharm.PurchaseOrderLines.Add(new PurchaseOrderLine
            {
                PurchaseOrderId = po.Id, ProductId = l.ProductId, Qty = l.Qty, ExpectedCost = l.ExpectedCost,
            });
        await pharm.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "pharmacy.po-create", "pharm.purchase_order", po.Id,
            after: new { supplierId, outletId, lines = lines.Count, value = lines.Sum(l => l.Qty * l.ExpectedCost) });
        await kernel.SaveChangesAsync(ct);
        return po;
    }

    /// <summary>⚿ Requested → Approved. The caller passes an APPROVED approval id — verified here.</summary>
    public async Task ApproveAsync(
        PharmDbContext pharm, KernelDbContext kernel, long poId, long approvalId, CancellationToken ct = default)
    {
        var ok = await kernel.ApprovalRequests.AsNoTracking().AnyAsync(a =>
            a.Id == approvalId && a.Type == "purchase-order" && a.SourceTable == "pharm.purchase_order"
            && a.SourceId == poId && a.State == Kernel.Data.ApprovalState.Approved, ct);
        if (!ok) throw new PharmacyException("The purchase order needs an approved request first (§11 ⚿).");

        await Transition(pharm, poId, PoState.Requested, PoState.Approved, ct);
        await pharm.Database.ExecuteSqlAsync(
            $"UPDATE pharm.purchase_order SET approval_id = {approvalId} WHERE id = {poId}", ct);
    }

    public Task OrderAsync(PharmDbContext pharm, long poId, CancellationToken ct = default)
        => Transition(pharm, poId, PoState.Approved, PoState.Ordered, ct);

    public async Task CancelAsync(PharmDbContext pharm, long poId, CancellationToken ct = default)
    {
        var affected = await pharm.Database.ExecuteSqlAsync($"""
            UPDATE pharm.purchase_order SET state = {PoState.Cancelled}
            WHERE id = {poId} AND state IN ({PoState.Requested}, {PoState.Approved}, {PoState.Ordered})
            """, ct);
        if (affected == 0)
            throw new PharmacyException("Stock was already received against this order — it can only be closed.");
    }

    /// <summary>One GRN line: creates the batch, advances the PO (partially/fully received),
    /// and raises the supplier payable at actual cost.</summary>
    public async Task<Batch> ReceiveLineAsync(
        PharmDbContext pharm, KernelDbContext kernel, long branchId, long poId, long poLineId,
        string batchNo, DateOnly expiry, int qty, long unitCost, long unitMrp,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var po = await pharm.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == poId, ct);
        if (po.State is not (PoState.Ordered or PoState.PartiallyReceived))
            throw new PharmacyException("Only an ordered purchase order can receive stock.");
        var line = await pharm.PurchaseOrderLines.SingleAsync(l => l.Id == poLineId && l.PurchaseOrderId == poId, ct);
        if (line.ReceivedQty + qty > line.Qty)
            throw new PharmacyException(
                $"That would exceed the ordered quantity ({line.ReceivedQty + qty} of {line.Qty}).");

        var batch = await stock.ReceiveAsync(pharm, kernel, branchId, po.OutletId, line.ProductId,
            batchNo, expiry, qty, unitCost, unitMrp, poId, actorId, actorName, ct);

        line.ReceivedQty += qty;
        await pharm.SaveChangesAsync(ct);

        pharm.SupplierLedger.Add(new SupplierLedgerEntry
        {
            BranchId = branchId, SupplierId = po.SupplierId, Kind = "purchase",
            Amount = qty * unitCost, RefTable = "pharm.batch", RefId = batch.Id,
            Note = $"GRN {qty} × {batchNo}", ActorId = actorId, At = clock.GetUtcNow(),
        });
        await pharm.SaveChangesAsync(ct);

        var lines = await pharm.PurchaseOrderLines.AsNoTracking()
            .Where(l => l.PurchaseOrderId == poId).ToListAsync(ct);
        var newState = lines.All(l => l.ReceivedQty >= l.Qty) ? PoState.Received : PoState.PartiallyReceived;
        await pharm.Database.ExecuteSqlAsync($"""
            UPDATE pharm.purchase_order SET state = {newState}
            WHERE id = {poId} AND state IN ({PoState.Ordered}, {PoState.PartiallyReceived})
            """, ct);
        return batch;
    }

    public Task CloseAsync(PharmDbContext pharm, long poId, CancellationToken ct = default)
        => Transition(pharm, poId, PoState.Received, PoState.Closed, ct);

    public async Task RecordPaymentAsync(
        PharmDbContext pharm, KernelDbContext kernel, long branchId, long supplierId, long amount,
        string? note, long actorId, string actorName, CancellationToken ct = default)
    {
        if (amount <= 0) throw new PharmacyException("Enter the amount being paid.");
        pharm.SupplierLedger.Add(new SupplierLedgerEntry
        {
            BranchId = branchId, SupplierId = supplierId, Kind = "payment", Amount = -amount,
            Note = note, ActorId = actorId, At = clock.GetUtcNow(),
        });
        await pharm.SaveChangesAsync(ct);
        audit.Append(kernel, branchId, actorId, actorName, "pharmacy.supplier-payment", "pharm.supplier", supplierId,
            after: new { amount, note });
        await kernel.SaveChangesAsync(ct);
    }

    private static async Task Transition(
        PharmDbContext pharm, long poId, string from, string to, CancellationToken ct)
    {
        var affected = await pharm.Database.ExecuteSqlAsync($"""
            UPDATE pharm.purchase_order SET state = {to} WHERE id = {poId} AND state = {from}
            """, ct);
        if (affected == 0)
            throw new PharmacyException($"The order has already moved on from '{from}' — refresh.");
    }
}
