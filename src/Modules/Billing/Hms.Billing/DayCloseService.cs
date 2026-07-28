using System.Text.Json;
using Hms.Billing.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Billing;

/// <summary>
/// §6.6: only DayClose writes the ledger holding structure; nobody writes ledger entries.
/// Variance is recorded, never blocking (edge 18). The session row lock serializes close
/// against in-flight receipts (G7: day-close vs late receipt).
/// </summary>
public sealed class DayCloseService(
    AuditWriter audit, BusinessDayCalendar businessDay, TimeProvider clock)
{
    public async Task<DayCloseSummary> CloseAsync(
        BillDbContext bill, KernelDbContext kernel, long sessionId, long countedCash,
        long actorId, string actorName, long? carryCloseApprovalId = null,
        CancellationToken ct = default)
    {
        // Serialization point: same row CollectAsync locks.
        var states = await bill.Database.SqlQuery<string>($"""
            SELECT state AS "Value" FROM bill.counter_session WHERE id = {sessionId} FOR UPDATE
            """).ToListAsync(ct);
        if (states.Count == 0) throw new BillingException("Unknown session.");
        if (states[0] is not (SessionState.Active or SessionState.Reopened))
            throw new BillingException("Session is not open — it may already be closed (edge 28).");

        var session = await bill.Sessions.SingleAsync(s => s.Id == sessionId, ct);
        var isCarryClose = carryCloseApprovalId is not null;
        // Spec 0027: both sides must be business days. Comparing the session's Dhaka business
        // day against a UTC calendar date made every session opened between 00:00 and 06:00
        // Dhaka look stale — a night shift was told to fetch a supervisor to close its own
        // drawer, and the test suite was only green for eighteen hours a day.
        if (session.BusinessDay != businessDay.BusinessDayOf(clock.GetUtcNow()) && !isCarryClose)
        {
            // edge 17: a stale session needs the supervised carry-close path, not a silent merge.
            throw new BillingException(
                $"This session belongs to business day {session.BusinessDay:yyyy-MM-dd} — " +
                "it needs a supervised carry-close before a new session can open.");
        }

        var receipts = await bill.Receipts.Where(r => r.CounterSessionId == sessionId).ToListAsync(ct);
        var tenderTotals = receipts.GroupBy(r => r.Tender)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        var cashTotal = tenderTotals.GetValueOrDefault("cash", 0);
        var expectedCash = session.OpeningFloat + cashTotal;
        var variance = countedCash - expectedCash;                    // recorded, not blocked (edge 18)

        // Spec 0032 (M4-D4): a reversed invoice is not billing. Before this filter a cancelled
        // invoice contributed its full Net to a PRINTED financial document with nothing to
        // offset it — cancellation writes no receipt, so the money simply appeared. The three
        // figures below print as a visible subtraction ("Gross billed / Less: discount given /
        // Net billed"), which is why the reversed invoice is dropped whole rather than netted:
        // money handed back has its own line (`refunds`, below) and must not be counted twice.
        // `InvoiceValue` is the same definition the MD dashboard reads.
        var allInvoices = await bill.Invoices.Where(i => i.CounterSessionId == sessionId).ToListAsync(ct);
        var invoices = allInvoices.Where(i => !InvoiceValue.IsReversed(i.State)).ToList();
        var invoiceIds = allInvoices.Select(i => i.Id).ToList();
        var dueOutstanding = await bill.Dues
            .Where(d => invoiceIds.Contains(d.InvoiceId)).SumAsync(d => d.Balance, ct);
        var refunds = receipts.Where(r => r.Amount < 0 && r.InvoiceId != null).Sum(r => -r.Amount);
        var dueCollected = receipts.Where(r => r.Amount > 0 && r.InvoiceId is long inv
            && !invoiceIds.Contains(inv)).Sum(r => r.Amount);         // collections against other days' invoices
        var advancesTaken = receipts.Where(r => r.FolioId != null).Sum(r => r.Amount); // net (spec 0017)

        session.State = SessionState.Closed;
        session.ClosedAt = clock.GetUtcNow();
        session.CountedCash = countedCash;
        session.ExpectedCash = expectedCash;
        session.Variance = variance;
        session.CarryClosed = isCarryClose;
        session.CloseApprovedBy = isCarryClose ? actorId : null;

        var summary = new DayCloseSummary
        {
            BranchId = session.BranchId,
            CounterSessionId = sessionId,
            BusinessDay = session.BusinessDay,                        // attribution rule: opening day
            DeptSplit = "{}",                                         // dept split joins with masters (S5)
            TenderTotals = JsonSerializer.Serialize(tenderTotals),
            Gross = invoices.Sum(i => i.Gross),
            Discount = invoices.Sum(i => i.Discount),
            Net = invoices.Sum(i => i.Net),
            DueCreated = dueOutstanding,
            DueCollected = dueCollected,
            AdvancesTaken = advancesTaken,
            Refunds = refunds,
            Variance = variance,
            Version = 1,
        };
        bill.DayCloses.Add(summary);
        await bill.SaveChangesAsync(ct);

        audit.Append(kernel, session.BranchId, actorId, actorName,
            isCarryClose ? "session.carry_close" : "session.close",
            "bill.counter_session", sessionId,
            after: new { session.BusinessDay, expectedCash, countedCash, variance, tenderTotals });
        await kernel.SaveChangesAsync(ct);
        return summary;
    }
}
