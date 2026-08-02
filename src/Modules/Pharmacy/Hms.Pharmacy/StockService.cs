using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Pharmacy.Contracts;
using Hms.Pharmacy.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Pharmacy;

public sealed class PharmacyException(string message) : Exception(message);

/// <summary>
/// The stock kernel (ADR-0021). Every quantity change pairs a row-locked batch mutation with
/// an append-only <c>stock_move</c> in the caller's ambient transaction (G19) — history and
/// on-hand truth commit together or not at all. FEFO issue excludes expired and quarantined
/// batches by predicate, so US11.1's "physically block" is a query property.
/// </summary>
public sealed class StockService(AuditWriter audit, TimeProvider clock)
{
    /// <summary>Near-expiry warning window, days (§5 M11 expiry management; config later).</summary>
    public const int NearExpiryDays = 90;

    private sealed record LockedBatch(
        long Id, long BranchId, string BatchNo, DateOnly Expiry, int QtyOnHand, long Cost, long Mrp);

    // ---- receive -----------------------------------------------------------

    public async Task<Batch> ReceiveAsync(
        PharmDbContext pharm, KernelDbContext kernel, long branchId, long outletId, long productId,
        string batchNo, DateOnly expiry, int qty, long unitCost, long unitMrp,
        long? purchaseOrderId, long actorId, string actorName, CancellationToken ct = default)
    {
        if (qty <= 0) throw new PharmacyException("Received quantity must be positive.");
        if (unitCost < 0 || unitMrp < 0) throw new PharmacyException("Cost and MRP cannot be negative.");
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (expiry <= today)
            throw new PharmacyException($"Batch {batchNo} is already expired ({expiry:dd MMM yyyy}) — refuse it at the door.");

        var batch = new Batch
        {
            BranchId = branchId, OutletId = outletId, ProductId = productId,
            BatchNo = batchNo.Trim(), Expiry = expiry, QtyOnHand = qty,
            Cost = unitCost, Mrp = unitMrp, PurchaseOrderId = purchaseOrderId,
            ReceivedAt = clock.GetUtcNow(), ReceivedBy = actorId,
        };
        pharm.Batches.Add(batch);
        await pharm.SaveChangesAsync(ct);

        Move(pharm, batch, MoveKind.Receive, qty, "pharm.purchase_order", purchaseOrderId, null, actorId);
        await pharm.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "pharmacy.receive", "pharm.batch", batch.Id,
            after: new { productId, batchNo, expiry, qty, unitCost, unitMrp, purchaseOrderId });
        await kernel.SaveChangesAsync(ct);
        return batch;
    }

    // ---- FEFO issue --------------------------------------------------------

    /// <summary>
    /// Locks and decrements sellable batches earliest-expiry-first. Expired/quarantined stock
    /// never matches the predicate (US11.1); concurrent last-strip sales serialize on the row
    /// lock and the loser gets a plain "just sold out" (ADR-0015 pattern).
    /// </summary>
    public async Task<IReadOnlyList<BatchAllocation>> AllocateFefoAsync(
        PharmDbContext pharm, long outletId, long productId, int qty, string refTable, long? refId,
        long actorId, CancellationToken ct = default)
    {
        if (qty <= 0) throw new PharmacyException("Quantity must be positive.");
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        // Column names map to the record via the snake_case naming convention.
        var candidates = await pharm.Database.SqlQuery<LockedBatch>($"""
            SELECT id, branch_id, batch_no, expiry, qty_on_hand, cost, mrp
            FROM pharm.batch
            WHERE outlet_id = {outletId} AND product_id = {productId}
              AND state = {BatchState.InStock} AND expiry > {today} AND qty_on_hand > 0
            ORDER BY expiry, id
            FOR UPDATE
            """).ToListAsync(ct);

        var available = candidates.Sum(b => b.QtyOnHand);
        if (available < qty)
            throw new PharmacyException(available == 0
                ? "Out of stock (or every batch is expired/quarantined) — nothing sellable."
                : $"Only {available} sellable unit(s) left — someone may have just sold the rest.");

        var near = today.AddDays(NearExpiryDays);
        var allocations = new List<BatchAllocation>();
        var remaining = qty;
        foreach (var b in candidates)
        {
            if (remaining == 0) break;
            var take = Math.Min(remaining, b.QtyOnHand);
            remaining -= take;

            await pharm.Database.ExecuteSqlAsync(
                $"UPDATE pharm.batch SET qty_on_hand = qty_on_hand - {take} WHERE id = {b.Id}", ct);
            pharm.StockMoves.Add(new StockMove
            {
                BranchId = b.BranchId, OutletId = outletId, BatchId = b.Id, ProductId = productId,
                Kind = MoveKind.Sale, Qty = -take, RefTable = refTable, RefId = refId,
                ActorId = actorId, At = clock.GetUtcNow(),
            });

            allocations.Add(new BatchAllocation(
                b.Id, productId, b.BatchNo, b.Expiry, take, b.Mrp, b.Cost, b.Expiry <= near));
        }
        await pharm.SaveChangesAsync(ct);
        return allocations;
    }

    /// <summary>
    /// Refund restocking: puts refunded goods back on the exact batches the sale took them
    /// from, unless a batch has since expired — expired stock goes to quarantine.
    /// <para>
    /// Spec 0039 WP4 (AUD-M11-01): the refunded amount decides HOW MUCH comes back. This used
    /// to take no quantity at all, so refunding 100 Tk of a 5,000 Tk sale restocked the whole
    /// sale, marked every line refunded, and the error was permanent because the allocations
    /// were exhausted. Units are restocked at each allocation's sale-time MRP until the
    /// refunded money is spent; a part-unit remainder restocks nothing — money smaller than
    /// one unit's price means no goods came back.
    /// </para>
    /// </summary>
    public async Task RestockAsync(
        PharmDbContext pharm, long branchId, long invoiceId, long refundedAmount, long actorId,
        CancellationToken ct = default)
    {
        if (refundedAmount <= 0) return;
        var allocations = await pharm.SaleAllocations
            .Where(a => a.InvoiceId == invoiceId && a.Qty > a.RefundedQty)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
        if (allocations.Count == 0) return;

        // Same discipline as the sale path: lock the batches (ordered, so two refunds of
        // adjacent invoices cannot ABBA) before touching qty_on_hand.
        var batchIds = allocations.Select(a => a.BatchId).Distinct().OrderBy(id => id).ToArray();
        await pharm.Database.SqlQuery<long>($"""
            SELECT id AS "Value" FROM pharm.batch WHERE id = ANY({batchIds}) ORDER BY id FOR UPDATE
            """).ToListAsync(ct);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var budget = refundedAmount;

        foreach (var a in allocations)
        {
            if (budget <= 0) break;
            var unit = Math.Max(1, a.UnitMrp);                      // 0-priced unit: count it, cost nothing
            var affordable = (int)Math.Min(a.Qty - a.RefundedQty, budget / unit);
            if (affordable <= 0) continue;

            var batch = await pharm.Batches.SingleAsync(b => b.Id == a.BatchId, ct);
            batch.QtyOnHand += affordable;
            if (batch.Expiry <= today && batch.State == BatchState.InStock)
            {
                batch.State = BatchState.Quarantined;
                batch.StateReason = "expired before refund restock";
            }
            a.RefundedQty += affordable;
            budget -= affordable * a.UnitMrp;
            Move(pharm, batch, MoveKind.SaleReturn, affordable, "bill.invoice", invoiceId, null, actorId);
        }
        await pharm.SaveChangesAsync(ct);
    }

    /// <summary>One restocked slice of a discharge-time return (spec 0017 / 0016 deferral #11).</summary>
    public sealed record IndentReturnLine(long BatchId, string BatchNo, long ProductId, int Qty, long UnitMrp);

    /// <summary>
    /// Discharge-time return of indoor-issued medicine: walks the indent's issue allocations
    /// (latest first) and puts stock back on the exact batches it came from, so the negative
    /// folio lines the caller posts reverse the same batch MRP that was charged.
    /// </summary>
    public async Task<IReadOnlyList<IndentReturnLine>> RestockIndentAsync(
        PharmDbContext pharm, long indentId, long productId, int qty, long actorId,
        CancellationToken ct = default)
    {
        if (qty <= 0) throw new PharmacyException("Return quantity must be positive.");
        var allocations = await pharm.IssueAllocations
            .Where(a => a.IndentId == indentId && a.ProductId == productId && a.Qty > a.ReturnedQty)
            .OrderByDescending(a => a.Id).ToListAsync(ct);
        var available = allocations.Sum(a => a.Qty - a.ReturnedQty);
        if (qty > available)
            throw new PharmacyException($"Only {available} of this item remains returnable on the indent.");

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var lines = new List<IndentReturnLine>();
        var remaining = qty;
        foreach (var a in allocations)
        {
            if (remaining == 0) break;
            var back = Math.Min(remaining, a.Qty - a.ReturnedQty);
            var batch = await pharm.Batches.SingleAsync(b => b.Id == a.BatchId, ct);
            batch.QtyOnHand += back;
            if (batch.Expiry <= today && batch.State == BatchState.InStock)
            {
                batch.State = BatchState.Quarantined;
                batch.StateReason = "expired before indoor return";
            }
            a.ReturnedQty += back;
            remaining -= back;
            Move(pharm, batch, MoveKind.IndoorReturn, back, "ipd.indent", indentId, null, actorId);
            lines.Add(new IndentReturnLine(batch.Id, batch.BatchNo, productId, back, a.UnitMrp));
        }
        await pharm.SaveChangesAsync(ct);
        return lines;
    }

    // ---- quarantine / write-off / supplier return (5A-11 damage & expiry) --

    public async Task QuarantineAsync(
        PharmDbContext pharm, KernelDbContext kernel, long branchId, long batchId, string reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var affected = await pharm.Database.ExecuteSqlAsync($"""
            UPDATE pharm.batch SET state = {BatchState.Quarantined}, state_reason = {reason}
            WHERE id = {batchId} AND state = {BatchState.InStock}
            """, ct);
        if (affected == 0) throw new PharmacyException("That batch is not in stock any more — refresh.");

        var batch = await pharm.Batches.AsNoTracking().SingleAsync(b => b.Id == batchId, ct);
        Move(pharm, batch, MoveKind.Quarantine, 0, null, null, reason, actorId);
        await pharm.SaveChangesAsync(ct);
        audit.Append(kernel, branchId, actorId, actorName, "pharmacy.quarantine", "pharm.batch", batchId,
            after: new { reason, batch.QtyOnHand });
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>Return quarantined stock to the supplier; the ledger receives the credit
    /// (or a replacement marker — 5A-11 supplier replacement).</summary>
    public async Task ReturnToSupplierAsync(
        PharmDbContext pharm, KernelDbContext kernel, long branchId, long batchId, long supplierId,
        bool replacement, long actorId, string actorName, CancellationToken ct = default)
    {
        var batch = await pharm.Batches.SingleAsync(b => b.Id == batchId, ct);
        if (batch.State != BatchState.Quarantined)
            throw new PharmacyException("Only quarantined stock goes back to the supplier — quarantine it first.");
        var qty = batch.QtyOnHand;
        if (qty <= 0) throw new PharmacyException("Nothing left in that batch to return.");

        batch.QtyOnHand = 0;
        batch.State = BatchState.Returned;
        Move(pharm, batch, MoveKind.SupplierReturn, -qty, "pharm.supplier", supplierId,
            replacement ? "replacement expected" : "credit", actorId);

        pharm.SupplierLedger.Add(new SupplierLedgerEntry
        {
            BranchId = branchId, SupplierId = supplierId,
            Kind = replacement ? "replacement" : "return_credit",
            Amount = -qty * batch.Cost,                     // reduces what we owe
            RefTable = "pharm.batch", RefId = batchId,
            Note = replacement
                ? $"{qty} × batch {batch.BatchNo} returned for replacement"
                : $"{qty} × batch {batch.BatchNo} returned for credit",
            ActorId = actorId, At = clock.GetUtcNow(),
        });
        await pharm.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "pharmacy.supplier-return", "pharm.batch", batchId,
            after: new { supplierId, qty, replacement });
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>Dispose of quarantined stock. Approval-gated (⚿ per §11: Disposed(logged)) —
    /// the caller passes an APPROVED approval id; this is verified, not trusted.</summary>
    public async Task DisposeAsync(
        PharmDbContext pharm, KernelDbContext kernel, long branchId, long batchId, long approvalId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var approved = await kernel.ApprovalRequests.AsNoTracking().AnyAsync(a =>
            a.Id == approvalId && a.Type == "stock-writeoff" && a.SourceTable == "pharm.batch"
            && a.SourceId == batchId && a.State == Kernel.Data.ApprovalState.Approved, ct);
        if (!approved) throw new PharmacyException("Write-off needs an approved request first.");

        var batch = await pharm.Batches.SingleAsync(b => b.Id == batchId, ct);
        if (batch.State != BatchState.Quarantined)
            throw new PharmacyException("Only quarantined stock can be written off.");
        var qty = batch.QtyOnHand;
        batch.QtyOnHand = 0;
        batch.State = BatchState.Disposed;
        Move(pharm, batch, MoveKind.Dispose, -qty, "kernel.approval_request", approvalId, batch.StateReason, actorId);
        await pharm.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "pharmacy.dispose", "pharm.batch", batchId,
            after: new { qty, approvalId }, tier: 1);
        await kernel.SaveChangesAsync(ct);
    }

    // ---- transfers (5A-11 multi-outlet) ------------------------------------

    public async Task<Transfer> SendTransferAsync(
        PharmDbContext pharm, KernelDbContext kernel, long branchId, long transferId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var transfer = await pharm.Transfers.SingleAsync(t => t.Id == transferId, ct);
        if (transfer.State != TransferState.Indent)
            throw new PharmacyException("This indent was already sent or cancelled.");
        var lines = await pharm.TransferLines.Where(l => l.TransferId == transferId).ToListAsync(ct);

        foreach (var line in lines)
        {
            // FEFO at the source outlet, same lock discipline as a sale.
            var allocations = await AllocateFefoAsync(
                pharm, transfer.FromOutletId, line.ProductId, line.RequestedQty,
                "pharm.transfer", transferId, actorId, ct);
            line.SentQty = allocations.Sum(a => a.Qty);
            foreach (var a in allocations)
                pharm.TransferBatches.Add(new TransferBatch
                {
                    TransferId = transferId, SourceBatchId = a.BatchId, ProductId = a.ProductId,
                    BatchNo = a.BatchNo, Expiry = a.Expiry, Qty = a.Qty, Cost = a.UnitCost, Mrp = a.UnitMrp,
                });
        }
        transfer.State = TransferState.Sent;
        transfer.SentAt = clock.GetUtcNow();
        await pharm.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "pharmacy.transfer-send", "pharm.transfer", transferId,
            after: new { transfer.FromOutletId, transfer.ToOutletId, lines = lines.Count });
        await kernel.SaveChangesAsync(ct);
        return transfer;
    }

    public async Task ReceiveTransferAsync(
        PharmDbContext pharm, KernelDbContext kernel, long branchId, long transferId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var affected = await pharm.Database.ExecuteSqlAsync($"""
            UPDATE pharm.transfer SET state = {TransferState.Received},
                   received_at = {clock.GetUtcNow()}, received_by = {actorId}
            WHERE id = {transferId} AND state = {TransferState.Sent}
            """, ct);
        if (affected == 0) throw new PharmacyException("This transfer is not in transit — refresh.");

        var transfer = await pharm.Transfers.AsNoTracking().SingleAsync(t => t.Id == transferId, ct);
        var parcels = await pharm.TransferBatches.Where(b => b.TransferId == transferId).ToListAsync(ct);
        foreach (var p in parcels)
        {
            // The destination gets an identical batch identity — the outlet transfer ledger
            // (5A-11) can trace batch_no across outlets.
            var dest = new Batch
            {
                BranchId = branchId, OutletId = transfer.ToOutletId, ProductId = p.ProductId,
                BatchNo = p.BatchNo, Expiry = p.Expiry, QtyOnHand = p.Qty, Cost = p.Cost, Mrp = p.Mrp,
                ReceivedAt = clock.GetUtcNow(), ReceivedBy = actorId,
            };
            pharm.Batches.Add(dest);
            await pharm.SaveChangesAsync(ct);
            Move(pharm, dest, MoveKind.TransferIn, p.Qty, "pharm.transfer", transferId, null, actorId);
        }
        await pharm.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "pharmacy.transfer-receive", "pharm.transfer", transferId,
            after: new { parcels = parcels.Count });
        await kernel.SaveChangesAsync(ct);
    }

    // ---- stock audit (§11: Count Started → Variance → Approved⚿ → Posted) --

    public async Task PostAuditAsync(
        PharmDbContext pharm, KernelDbContext kernel, long branchId, long auditId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var stockAudit = await pharm.StockAudits.SingleAsync(a => a.Id == auditId, ct);
        if (stockAudit.State != AuditState.Approved)
            throw new PharmacyException("The adjustment needs approval before it can post (§11).");

        var lines = await pharm.StockAuditLines
            .Where(l => l.StockAuditId == auditId && l.CountedQty != l.SystemQty).ToListAsync(ct);
        foreach (var line in lines)
        {
            var delta = line.CountedQty - line.SystemQty;
            // Adjustment is a move over the locked batch — never an edit of history.
            var affected = await pharm.Database.ExecuteSqlAsync($"""
                UPDATE pharm.batch SET qty_on_hand = qty_on_hand + {delta}
                WHERE id = {line.BatchId} AND qty_on_hand + {delta} >= 0
                """, ct);
            if (affected == 0)
                throw new PharmacyException(
                    $"Batch #{line.BatchId} moved since the count — recount before posting.");
            var batch = await pharm.Batches.AsNoTracking().SingleAsync(b => b.Id == line.BatchId, ct);
            Move(pharm, batch, MoveKind.Adjustment, delta, "pharm.stock_audit", auditId,
                $"count {line.CountedQty} vs system {line.SystemQty}", actorId);
        }
        stockAudit.State = AuditState.Posted;
        stockAudit.PostedAt = clock.GetUtcNow();
        await pharm.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "pharmacy.audit-post", "pharm.stock_audit", auditId,
            after: new { adjusted = lines.Count });
        await kernel.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------------

    private void Move(
        PharmDbContext pharm, Batch batch, string kind, int qty, string? refTable, long? refId,
        string? reason, long actorId)
        => pharm.StockMoves.Add(new StockMove
        {
            BranchId = batch.BranchId, OutletId = batch.OutletId,
            BatchId = batch.Id, ProductId = batch.ProductId,
            Kind = kind, Qty = qty, RefTable = refTable, RefId = refId, Reason = reason,
            ActorId = actorId, At = clock.GetUtcNow(),
        });
}
