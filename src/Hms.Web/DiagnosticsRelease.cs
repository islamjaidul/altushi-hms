using Hms.Billing;
using Hms.Diagnostics;
using Hms.Diagnostics.Data;
using Hms.Lis;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

/// <summary>
/// The §9A.2 seam: <em>payment</em> releases the lab. Until an order's invoice is settled in
/// full it raises no tube and appears on no worklist; the moment it is settled — whether at the
/// counter or days later at due collection — the order moves to in-progress, its samples are
/// created and their labels print.
///
/// This lives in the host rather than in Diagnostics because it composes three modules
/// (diag + lis + bill) and no module may reach into another's schema (ADR-0003 / 04 §1).
/// </summary>
public static class DiagnosticsRelease
{
    /// <summary>
    /// Releases every fully-paid, not-yet-released diagnostic order on this invoice.
    /// Idempotent: the state-guarded UPDATE inside <c>MarkPaidAsync</c> means a second call
    /// (double-click, retried collection) releases nothing twice.
    /// </summary>
    public static async Task<int> ReleasePaidOrdersAsync(
        TxScope s, BillingService billing, LisService lis, TimeProvider clock,
        long branchId, long invoiceId, long actorId, CancellationToken ct = default)
    {
        var balance = await s.Bill.Dues.AsNoTracking()
            .Where(d => d.InvoiceId == invoiceId)
            .Select(d => (long?)d.Balance)
            .FirstOrDefaultAsync(ct) ?? 0;
        if (balance > 0) return 0;                      // still owing — the lab stays untouched

        var orders = await s.Diag.Orders
            .Where(o => o.InvoiceId == invoiceId && o.State == TestOrderState.Invoiced)
            .Select(o => o.Id)
            .ToListAsync(ct);
        if (orders.Count == 0) return 0;

        var diagnostics = new DiagnosticsService(new ChargePoster(s.Bill, billing), clock);
        var released = 0;

        foreach (var orderId in orders)
        {
            await diagnostics.MarkPaidAsync(s.Diag, s.Kernel, orderId, ct);
            await CreateSamplesAsync(s, lis, branchId, orderId, actorId, ct);
            released++;
        }
        return released;
    }

    /// <summary>
    /// Edge 33: one tube can carry several tests and one test can need several tubes, so samples
    /// are grouped by sample type — CBC + ESR share the EDTA tube; imaging raises none at all.
    /// </summary>
    public static async Task CreateSamplesAsync(
        TxScope s, LisService lis, long branchId, long orderId, long actorId, CancellationToken ct)
    {
        var orderTests = await s.Diag.OrderTests.AsNoTracking()
            .Where(ot => ot.TestOrderId == orderId)
            .Select(ot => new { ot.Id, ot.TestCatalogId })
            .ToListAsync(ct);
        if (orderTests.Count == 0) return;

        var catalogIds = orderTests.Select(ot => ot.TestCatalogId).Distinct().ToList();
        var sampleTypes = await s.Adm.TestCatalog.AsNoTracking()
            .Where(t => catalogIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.SampleTypes, ct);

        var groups = orderTests
            .Select(ot => new
            {
                ot.Id,
                Type = sampleTypes.TryGetValue(ot.TestCatalogId, out var types) && types.Length > 0
                    ? types[0] : null,
            })
            .Where(x => x.Type is not null)
            .GroupBy(x => x.Type!);

        foreach (var g in groups)
        {
            var sample = await lis.CreateSampleAsync(
                s.Lis, branchId, g.Key, g.Select(x => x.Id).ToList(), ct: ct);
            await lis.PrintLabelAsync(s.Lis, sample.Id, actorId, ct);
        }
    }
}
