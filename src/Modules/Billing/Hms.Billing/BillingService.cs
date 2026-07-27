using Hms.Billing.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Billing;

public sealed record NewChargeLine(
    string CatalogKind, long CatalogId, string Description, int Qty, long UnitPrice,
    long? RateVersionId = null, long? DoctorId = null, long? ReferrerId = null, long? TestOrderId = null);

public sealed class BillingException(string message) : Exception(message);

/// <summary>
/// The money core (03 §4/§6, ADR-0015). Every public operation expects the caller's ambient
/// transaction spanning BillDbContext + KernelDbContext on ONE connection (G19) — invoice,
/// number, audit and due commit together or not at all.
/// </summary>
public sealed class BillingService(
    NumberSeriesService numbers, AuditWriter audit, FiscalCalendar fiscal,
    BusinessDayCalendar businessDay, TimeProvider clock)
{
    // ---- sessions ----------------------------------------------------------

    public async Task<CounterSession> OpenSessionAsync(
        BillDbContext bill, long branchId, long counterId, long operatorId, long openingFloat,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var session = new CounterSession
        {
            BranchId = branchId,
            CounterId = counterId,
            OperatorId = operatorId,
            BusinessDay = businessDay.BusinessDayOf(now),
            OpenedAt = now,
            OpeningFloat = openingFloat,
            State = SessionState.Active,
        };
        bill.Sessions.Add(session);
        try
        {
            await bill.SaveChangesAsync(ct);
        }
        catch (DbUpdateException e) when (e.InnerException?.Message.Contains("one_open_session") == true)
        {
            // ADR-0015 #1: uniqueness by constraint, surfaced comprehensibly (edge 17 path follows)
            throw new BillingException(
                "This counter already has an open session. Close it (or carry-close yesterday's) first.");
        }
        return session;
    }

    // ---- charges & invoicing ----------------------------------------------

    public async Task<ChargeLine> PostChargeAsync(
        BillDbContext bill, long branchId, long encounterId, string sourceModule, NewChargeLine line,
        long actorId, CancellationToken ct = default)
    {
        if (line.Qty <= 0 || line.UnitPrice < 0)
            throw new BillingException("Quantity must be positive and price non-negative.");
        var charge = new ChargeLine
        {
            BranchId = branchId,
            EncounterId = encounterId,
            SourceModule = sourceModule,
            CatalogKind = line.CatalogKind,
            CatalogId = line.CatalogId,
            DescriptionSnapshot = line.Description,
            Qty = line.Qty,
            UnitPrice = line.UnitPrice,
            RateVersionId = line.RateVersionId,
            Amount = line.Qty * line.UnitPrice,          // exact: integers throughout (03 §6.1)
            DoctorId = line.DoctorId,
            ReferrerId = line.ReferrerId,
            TestOrderId = line.TestOrderId,
            InvoiceId = null,                            // unbilled until invoiced
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actorId,
        };
        bill.ChargeLines.Add(charge);
        await bill.SaveChangesAsync(ct);
        return charge;
    }

    /// <summary>
    /// The folio-parented twin of <see cref="PostChargeAsync"/> (M6 seam, spec 0017). Negative
    /// quantities are reserved for the return path (discharge-time medicine return) — a return
    /// is a new negative line pointing at what it reverses, never an edit (hard rule 4).
    /// State guards (open/blocked/locked) belong to the folio's owner (Ipd module) — this
    /// method trusts the caller holds the folio row lock.
    /// </summary>
    public async Task<ChargeLine> PostFolioChargeAsync(
        BillDbContext bill, long branchId, long folioId, string sourceModule, NewChargeLine line,
        long actorId, bool allowNegativeQty = false, CancellationToken ct = default)
    {
        if ((line.Qty <= 0 && !allowNegativeQty) || line.Qty == 0 || line.UnitPrice < 0)
            throw new BillingException("Quantity must be positive and price non-negative.");
        var charge = new ChargeLine
        {
            BranchId = branchId,
            FolioId = folioId,
            SourceModule = sourceModule,
            CatalogKind = line.CatalogKind,
            CatalogId = line.CatalogId,
            DescriptionSnapshot = line.Description,
            Qty = line.Qty,
            UnitPrice = line.UnitPrice,
            RateVersionId = line.RateVersionId,
            Amount = line.Qty * line.UnitPrice,
            DoctorId = line.DoctorId,
            ReferrerId = line.ReferrerId,
            TestOrderId = line.TestOrderId,
            InvoiceId = null,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actorId,
        };
        bill.ChargeLines.Add(charge);
        await bill.SaveChangesAsync(ct);
        return charge;
    }

    /// <summary>
    /// An advance is a folio-parented receipt: it rides the counter session like any other
    /// money into the drawer, so tender totals and expected cash need no special-casing.
    /// A negative amount is the settlement-time return of excess advance (approval-free by
    /// design: it is the documented change-giving of settlement, audited like everything else).
    /// </summary>
    public async Task<Receipt> CollectAdvanceAsync(
        BillDbContext bill, KernelDbContext kernel, long branchId, long folioId, long sessionId,
        long amount, string tender, string? tenderRef, long actorId, string actorName,
        CancellationToken ct = default)
    {
        if (amount == 0) throw new BillingException("Amount cannot be zero.");

        var sessionStates = await bill.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM bill.counter_session WHERE id = {sessionId} FOR UPDATE
            """).ToListAsync(ct);
        if (sessionStates.Count == 0 ||
            sessionStates[0] is not (SessionState.Active or SessionState.Reopened))
            throw new BillingException("Advances need an open counter session (02 §2.4).");

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var (_, receiptNo) = await numbers.IssueAsync(
            kernel, branchId, "receipt", fiscal.FiscalYearOf(today), "RCP-{fy}-{n:D6}", ct);

        var receipt = new Receipt
        {
            BranchId = branchId,
            ReceiptNo = receiptNo,
            FolioId = folioId,
            CounterSessionId = sessionId,
            Amount = amount,
            Tender = tender,
            TenderRef = tenderRef,
            OperatorId = actorId,
            At = clock.GetUtcNow(),
        };
        bill.Receipts.Add(receipt);
        await bill.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName,
            amount > 0 ? "advance.collect" : "advance.return", "bill.receipt", receipt.Id,
            after: new { receiptNo, folioId, amount, tender });
        await kernel.SaveChangesAsync(ct);
        return receipt;
    }

    /// <summary>Net advance held on a folio = advances taken − advance money returned.</summary>
    public async Task<long> AdvanceHeldAsync(BillDbContext bill, long folioId, CancellationToken ct = default)
        => await bill.Receipts.Where(r => r.FolioId == folioId)
            .SumAsync(r => (long?)r.Amount, ct) ?? 0;

    /// <summary>
    /// US6.2: the settlement invoice assembles itself from the folio's unbilled lines.
    /// Advances held reduce the due at birth; the applied amount is returned to the caller
    /// (the Ipd module records it on the folio). The caller holds the folio row lock and has
    /// already posted the service-charge line and caught up bed days.
    /// </summary>
    public async Task<(Invoice Invoice, long AdvanceApplied)> CreateFolioInvoiceAsync(
        BillDbContext bill, KernelDbContext kernel,
        long branchId, long folioId, long sessionId, long patientId,
        decimal discountPercent, long discountFlat, long? discountApprovalId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var charges = await bill.ChargeLines
            .Where(c => c.FolioId == folioId && c.InvoiceId == null)
            .ToListAsync(ct);
        if (charges.Count == 0) throw new BillingException("No unbilled charges on this folio.");

        var gross = charges.Sum(c => c.Amount);
        if (gross < 0) throw new BillingException("Folio total is negative — check return postings.");
        var discount = discountFlat + (discountPercent > 0
            ? RoundHalfUp(gross * discountPercent / 100m) : 0);
        if (discount > gross) throw new BillingException("Discount cannot exceed the gross amount.");
        var net = gross - discount;

        var advanceHeld = await AdvanceHeldAsync(bill, folioId, ct);
        var advanceApplied = Math.Min(net, advanceHeld);

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var fy = fiscal.FiscalYearOf(today);
        var (_, invoiceNo) = await numbers.IssueAsync(
            kernel, branchId, "invoice", fy, "INV-{fy}-{n:D6}", ct);

        var invoice = new Invoice
        {
            BranchId = branchId,
            InvoiceNo = invoiceNo,
            FiscalYear = fy,
            PatientId = patientId,
            FolioId = folioId,
            CounterSessionId = sessionId,
            Gross = gross,
            Discount = discount,
            DiscountApprovalId = discountApprovalId,
            Net = net,
            State = advanceApplied == net ? InvoiceState.Paid
                  : advanceApplied > 0 ? InvoiceState.PartiallyPaid : InvoiceState.Billed,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actorId,
        };
        bill.Invoices.Add(invoice);
        await bill.SaveChangesAsync(ct);

        foreach (var c in charges)
        {
            c.InvoiceId = invoice.Id;
            bill.InvoiceLines.Add(new InvoiceLine
            {
                InvoiceId = invoice.Id,
                ChargeLineId = c.Id,
                DescriptionSnapshot = c.DescriptionSnapshot,
                Qty = c.Qty,
                UnitPrice = c.UnitPrice,
                Amount = c.Amount,
                RateVersionId = c.RateVersionId,
                DoctorId = c.DoctorId,
                ReferrerId = c.ReferrerId,
            });
        }
        bill.Dues.Add(new Due { InvoiceId = invoice.Id, Balance = net - advanceApplied });
        await bill.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "invoice.create", "bill.invoice", invoice.Id,
            after: new { invoice.InvoiceNo, folioId, gross, discount, net, advanceApplied });
        await kernel.SaveChangesAsync(ct);
        return (invoice, advanceApplied);
    }

    /// <summary>03 §6 step 2: percentage discounts round half-up to whole taka, ONCE, at the total.</summary>
    public static long RoundHalfUp(decimal value) => (long)Math.Floor(value + 0.5m);

    /// <summary>
    /// Freezes the encounter's unbilled charge lines into an invoice: number issued in-transaction,
    /// invariant enforced by CHECK, due row created, audit appended. Caller commits.
    /// </summary>
    public async Task<Invoice> CreateInvoiceAsync(
        BillDbContext bill, KernelDbContext kernel,
        long branchId, long encounterId, long sessionId, long patientId,
        decimal discountPercent, long discountFlat, long? discountApprovalId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var charges = await bill.ChargeLines
            .Where(c => c.EncounterId == encounterId && c.InvoiceId == null)
            .ToListAsync(ct);
        if (charges.Count == 0) throw new BillingException("No unbilled charges on this encounter.");

        var gross = charges.Sum(c => c.Amount);
        var discount = discountFlat + (discountPercent > 0
            ? RoundHalfUp(gross * discountPercent / 100m) : 0);
        if (discount > gross) throw new BillingException("Discount cannot exceed the gross amount.");
        var net = gross - discount;                      // tax dormant (ADR-0018); rounding_adj 0

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var fy = fiscal.FiscalYearOf(today);
        var (_, invoiceNo) = await numbers.IssueAsync(
            kernel, branchId, "invoice", fy, "INV-{fy}-{n:D6}", ct);

        var invoice = new Invoice
        {
            BranchId = branchId,
            InvoiceNo = invoiceNo,
            FiscalYear = fy,
            PatientId = patientId,
            EncounterId = encounterId,
            CounterSessionId = sessionId,
            Gross = gross,
            Discount = discount,
            DiscountApprovalId = discountApprovalId,
            Net = net,
            State = InvoiceState.Billed,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actorId,
        };
        bill.Invoices.Add(invoice);
        await bill.SaveChangesAsync(ct);

        foreach (var c in charges)
        {
            c.InvoiceId = invoice.Id;
            bill.InvoiceLines.Add(new InvoiceLine
            {
                InvoiceId = invoice.Id,
                ChargeLineId = c.Id,
                DescriptionSnapshot = c.DescriptionSnapshot,
                Qty = c.Qty,
                UnitPrice = c.UnitPrice,
                Amount = c.Amount,
                RateVersionId = c.RateVersionId,
                DoctorId = c.DoctorId,
                ReferrerId = c.ReferrerId,
            });
        }
        bill.Dues.Add(new Due { InvoiceId = invoice.Id, Balance = net });
        await bill.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "invoice.create", "bill.invoice", invoice.Id,
            after: new { invoice.InvoiceNo, gross, discount, net });
        await kernel.SaveChangesAsync(ct);
        return invoice;
    }

    // ---- payment & dues ----------------------------------------------------

    /// <summary>
    /// ADR-0015 #2: the due row is locked (SELECT … FOR UPDATE) for the transaction, so parallel
    /// collections on one invoice serialize — over-collection is impossible, not just unlikely.
    /// </summary>
    public async Task<Receipt> CollectAsync(
        BillDbContext bill, KernelDbContext kernel,
        long branchId, long invoiceId, long sessionId, long amount, string tender, string? tenderRef,
        long actorId, string actorName, long? refundOfReceipt = null, long? approvalId = null,
        CancellationToken ct = default)
    {
        if (amount == 0) throw new BillingException("Amount cannot be zero.");

        var balances = await bill.Database.SqlQuery<long>($"""
            SELECT balance AS "Value" FROM bill.due WHERE invoice_id = {invoiceId} FOR UPDATE
            """).ToListAsync(ct);
        if (balances.Count == 0) throw new BillingException("No due row for this invoice.");
        var balance = balances[0];

        if (amount > 0 && amount > balance)
            throw new BillingException($"Amount exceeds due balance (৳ {balance}).");

        // Lock the session row: day-close and late receipts serialize here (ADR-0015; G7 race).
        var sessionStates = await bill.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM bill.counter_session WHERE id = {sessionId} FOR UPDATE
            """).ToListAsync(ct);
        if (sessionStates.Count == 0 ||
            sessionStates[0] is not (SessionState.Active or SessionState.Reopened))
            throw new BillingException("Receipts need an open counter session (02 §2.4).");

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var (_, receiptNo) = await numbers.IssueAsync(
            kernel, branchId, "receipt", fiscal.FiscalYearOf(today), "RCP-{fy}-{n:D6}", ct);

        var receipt = new Receipt
        {
            BranchId = branchId,
            ReceiptNo = receiptNo,
            InvoiceId = invoiceId,
            CounterSessionId = sessionId,
            Amount = amount,
            Tender = tender,
            TenderRef = tenderRef,
            OperatorId = actorId,
            At = clock.GetUtcNow(),
            RefundOfReceipt = refundOfReceipt,
            ApprovalId = approvalId,
        };
        bill.Receipts.Add(receipt);

        var newBalance = balance - amount;
        await bill.Database.ExecuteSqlAsync(
            $"UPDATE bill.due SET balance = {newBalance} WHERE invoice_id = {invoiceId}", ct);

        var invoice = await bill.Invoices.SingleAsync(i => i.Id == invoiceId, ct);
        invoice.State = newBalance == 0 ? InvoiceState.Paid : InvoiceState.PartiallyPaid;
        await bill.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "receipt.collect", "bill.receipt", receipt.Id,
            after: new { receiptNo, invoiceId, amount, tender, newBalance });
        await kernel.SaveChangesAsync(ct);
        return receipt;
    }

    // ---- reversals (§11: Cancelled⚿ pre-payment, Refunded⚿ post-payment) -----

    /// <summary>
    /// C5/G11 and hard rule 4: money is never deleted. A refund is a NEGATIVE receipt that
    /// points at what it reverses, so the original receipt stays on the record and the two
    /// together tell the truth. Approval-gated by the caller (§12 refund chain).
    /// </summary>
    public async Task<Receipt> RefundAsync(
        BillDbContext bill, KernelDbContext kernel, long branchId, long invoiceId, long sessionId,
        long amount, string reason, long actorId, string actorName, long approvalId,
        CancellationToken ct = default)
    {
        if (amount <= 0) throw new BillingException("A refund amount must be positive.");
        if (string.IsNullOrWhiteSpace(reason)) throw new BillingException("A refund needs a reason.");

        // Same row lock as collection: a refund and a collection on one invoice serialize.
        var balances = await bill.Database.SqlQuery<long>($"""
            SELECT balance AS "Value" FROM bill.due WHERE invoice_id = {invoiceId} FOR UPDATE
            """).ToListAsync(ct);
        if (balances.Count == 0) throw new BillingException("No due row for this invoice.");

        var receipts = await bill.Receipts.Where(r => r.InvoiceId == invoiceId).ToListAsync(ct);
        var netPaid = receipts.Sum(r => r.Amount);
        if (netPaid <= 0)
            throw new BillingException("Nothing has been paid on this invoice — cancel it instead of refunding.");
        if (amount > netPaid)
            throw new BillingException($"Only {netPaid} has been paid — a refund cannot exceed it.");

        var sessionStates = await bill.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM bill.counter_session WHERE id = {sessionId} FOR UPDATE
            """).ToListAsync(ct);
        if (sessionStates.Count == 0 ||
            sessionStates[0] is not (SessionState.Active or SessionState.Reopened))
            throw new BillingException("Refunds need an open counter session (02 §2.4).");

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var (_, receiptNo) = await numbers.IssueAsync(
            kernel, branchId, "receipt", fiscal.FiscalYearOf(today), "RCP-{fy}-{n:D6}", ct);

        var original = receipts.Where(r => r.Amount > 0).OrderByDescending(r => r.Amount).FirstOrDefault();
        var refund = new Receipt
        {
            BranchId = branchId,
            ReceiptNo = receiptNo,
            InvoiceId = invoiceId,
            CounterSessionId = sessionId,
            Amount = -amount,                              // negative = money leaving the drawer
            Tender = original?.Tender ?? "cash",
            TenderRef = null,
            OperatorId = actorId,
            At = clock.GetUtcNow(),
            RefundOfReceipt = original?.Id,
            ApprovalId = approvalId,
        };
        bill.Receipts.Add(refund);

        var invoice = await bill.Invoices.SingleAsync(i => i.Id == invoiceId, ct);
        invoice.State = InvoiceState.Refunded;
        await bill.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "receipt.refund", "bill.receipt", refund.Id,
            after: new { receiptNo, invoiceId, amount, reason, approvalId }, tier: 2);
        await kernel.SaveChangesAsync(ct);
        return refund;
    }

    /// <summary>
    /// §11: cancellation is the pre-payment exit. The invoice is never removed — it is marked
    /// cancelled and its due cleared, so the number it consumed stays accounted for (ADR-0004).
    /// </summary>
    public async Task CancelInvoiceAsync(
        BillDbContext bill, KernelDbContext kernel, long branchId, long invoiceId,
        string reason, long actorId, string actorName, long approvalId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new BillingException("A cancellation needs a reason.");

        var paid = await bill.Receipts.Where(r => r.InvoiceId == invoiceId)
            .SumAsync(r => (long?)r.Amount, ct) ?? 0;
        if (paid > 0)
            throw new BillingException(
                "Money has already been taken on this invoice — it must be refunded, not cancelled.");

        // State-guarded: an invoice someone else just cancelled loses here, comprehensibly.
        var affected = await bill.Database.ExecuteSqlAsync($"""
            UPDATE bill.invoice SET state = 'cancelled'
            WHERE id = {invoiceId} AND state IN ('billed', 'draft')
            """, ct);
        if (affected == 0)
            throw new BillingException("This invoice is not in a cancellable state — refresh and check.");

        await bill.Database.ExecuteSqlAsync(
            $"UPDATE bill.due SET balance = 0 WHERE invoice_id = {invoiceId}", ct);

        audit.Append(kernel, branchId, actorId, actorName, "invoice.cancel", "bill.invoice", invoiceId,
            after: new { reason, approvalId }, tier: 2);
        await kernel.SaveChangesAsync(ct);
    }
}
