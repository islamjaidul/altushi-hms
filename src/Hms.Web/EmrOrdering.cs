using Hms.Admin;
using Hms.Billing;
using Hms.Diagnostics;
using Hms.Emr.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

/// <summary>
/// The M5 → M8 seam (US5.4). Ordering a test from a prescription must produce exactly what the
/// counter's own order screen produces — a real test order with unbilled charge lines on the
/// patient's visit — so the patient walks to the counter and pays, and nobody re-types a chit.
///
/// It lives at the composition root, not in Hms.Emr, because it spans three modules
/// (Emr + Diagnostics + Billing). Same reasoning as <see cref="IpdBilling"/> (ADR-0003).
/// </summary>
public static class EmrOrdering
{
    public sealed record PendingOrder(long OrderId, string State, IReadOnlyList<string> Tests, long Total);

    /// <summary>
    /// Orders the given catalog tests on the note's visit. Returns the new order id, or null if
    /// nothing priceable was selected — a doctor ticking a test with no live price is a masters
    /// problem, and refusing silently would hide it.
    /// </summary>
    public static async Task<long?> OrderTestsAsync(
        TxScope s, RateResolver rates, BillingService billing, TimeProvider clock,
        long branchId, Note note, IReadOnlyList<long> testCatalogIds, long doctorId,
        CancellationToken ct = default)
    {
        if (note.EncounterId is not { } encounterId)
            throw new InvalidOperationException("Indoor orders post to the folio, not to a visit.");

        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var tests = new List<OrderedTest>();
        foreach (var testId in testCatalogIds.Distinct())
        {
            var catalogItem = await s.Adm.TestCatalog.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == testId, ct);
            if (catalogItem is null) continue;
            var rate = await rates.ResolveAsync(s.Adm, "test", testId, today);
            if (rate.Price <= 0) continue;
            tests.Add(new OrderedTest(testId, catalogItem.Name, catalogItem.TatMinutes,
                rate.Price, rate.RateVersionId));
        }
        if (tests.Count == 0) return null;

        // The poster binds to this transaction's BillDbContext, exactly as the request pipeline
        // would; CreateOrderAsync posts the unbilled charge lines through it.
        var diagnostics = new DiagnosticsService(new ChargePoster(s.Bill, billing), clock);
        var order = await diagnostics.CreateOrderAsync(s.Diag, branchId, note.PatientId,
            encounterId, tests, doctorId, null, doctorId, ct);
        return order.Id;
    }

    /// <summary>
    /// What the doctor sees after ordering, and what the counter will see: the orders raised on
    /// this visit that have not yet been billed. Cross-schema, so it is joined in memory.
    /// </summary>
    public static async Task<IReadOnlyList<PendingOrder>> PendingForEncounterAsync(
        TxScope s, long encounterId, CancellationToken ct = default)
    {
        var orders = await s.Diag.Orders.AsNoTracking()
            .Where(o => o.EncounterId == encounterId)
            .OrderByDescending(o => o.Id).ToListAsync(ct);
        if (orders.Count == 0) return [];

        var orderIds = orders.Select(o => o.Id).ToList();
        var orderTests = await s.Diag.OrderTests.AsNoTracking()
            .Where(t => orderIds.Contains(t.TestOrderId)).ToListAsync(ct);
        var catalogIds = orderTests.Select(t => t.TestCatalogId).Distinct().ToList();
        var names = await s.Adm.TestCatalog.AsNoTracking()
            .Where(t => catalogIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var chargeIds = orderTests.Select(t => t.ChargeLineId).ToList();
        var amounts = await s.Bill.ChargeLines.AsNoTracking()
            .Where(c => chargeIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Amount, ct);

        return orders.Select(o =>
        {
            var mine = orderTests.Where(t => t.TestOrderId == o.Id).ToList();
            return new PendingOrder(o.Id, o.State,
                mine.Select(t => names.GetValueOrDefault(t.TestCatalogId, "—")).ToList(),
                mine.Sum(t => amounts.GetValueOrDefault(t.ChargeLineId)));
        }).ToList();
    }
}
