using System.Text.Json;
using Hms.Billing.Contracts;
using Hms.Diagnostics.Data;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Diagnostics;

public sealed record OrderedTest(long TestCatalogId, string Name, int TatMinutes, long Price, long RateVersionId);

public sealed class DiagnosticsException(string message) : Exception(message);

/// <summary>
/// §9A.2 modules 5/6 seam: an order raised anywhere creates unbilled charge lines through the
/// Billing contract; payment flips the order and releases labels + worklist via outbox events.
/// </summary>
public sealed class DiagnosticsService(IChargePoster chargePoster, TimeProvider clock)
{
    public async Task<TestOrder> CreateOrderAsync(
        DiagDbContext diag, long branchId, long patientId, long encounterId,
        IReadOnlyList<OrderedTest> tests, long? orderingDoctorId, long? referrerId,
        long actorId, CancellationToken ct = default)
    {
        if (tests.Count == 0) throw new DiagnosticsException("An order needs at least one test.");

        var now = clock.GetUtcNow();
        var order = new TestOrder
        {
            BranchId = branchId,
            PatientId = patientId,
            EncounterId = encounterId,
            OrderingDoctorId = orderingDoctorId,
            ReferrerId = referrerId,
            State = TestOrderState.Ordered,
            PromisedAt = now.AddMinutes(tests.Max(t => t.TatMinutes)),   // TAT promise (§9A.2)
            CreatedAt = now,
            CreatedBy = actorId,
        };
        diag.Orders.Add(order);
        await diag.SaveChangesAsync(ct);

        var chargeIds = await chargePoster.PostChargesAsync(branchId, encounterId, "Diagnostics",
            tests.Select(t => new ChargeToPost("test", t.TestCatalogId, t.Name, 1, t.Price,
                t.RateVersionId, orderingDoctorId, referrerId, order.Id)).ToList(),
            actorId, ct);

        for (var i = 0; i < tests.Count; i++)
            diag.OrderTests.Add(new OrderTest
            {
                TestOrderId = order.Id,
                TestCatalogId = tests[i].TestCatalogId,
                ChargeLineId = chargeIds[i],
            });
        await diag.SaveChangesAsync(ct);
        return order;
    }

    /// <summary>
    /// §11 indoor branch (5A-9, spec 0017): a folio-parented order is "posted to folio" —
    /// there is no invoice gate, so it is born In-Progress and raises the release event
    /// immediately. Charge lines are posted by the caller (folio posting is the Ipd module's
    /// gate); their ids arrive here so order tests link 1:1, same as the outdoor path.
    /// </summary>
    public async Task<TestOrder> CreateFolioOrderAsync(
        DiagDbContext diag, KernelDbContext kernel, long branchId, long patientId, long folioId,
        IReadOnlyList<OrderedTest> tests, IReadOnlyList<long> chargeLineIds,
        long? orderingDoctorId, long actorId, CancellationToken ct = default)
    {
        if (tests.Count == 0) throw new DiagnosticsException("An order needs at least one test.");
        if (tests.Count != chargeLineIds.Count)
            throw new DiagnosticsException("Every ordered test needs its posted folio charge line.");

        var now = clock.GetUtcNow();
        var order = new TestOrder
        {
            BranchId = branchId,
            PatientId = patientId,
            FolioId = folioId,
            OrderingDoctorId = orderingDoctorId,
            State = TestOrderState.InProgress,
            PromisedAt = now.AddMinutes(tests.Max(t => t.TatMinutes)),
            CreatedAt = now,
            CreatedBy = actorId,
        };
        diag.Orders.Add(order);
        await diag.SaveChangesAsync(ct);

        for (var i = 0; i < tests.Count; i++)
            diag.OrderTests.Add(new OrderTest
            {
                TestOrderId = order.Id,
                TestCatalogId = tests[i].TestCatalogId,
                ChargeLineId = chargeLineIds[i],
            });
        await diag.SaveChangesAsync(ct);

        kernel.Outbox.Add(new OutboxMessage
        {
            EventType = "TestOrderPaid",
            Payload = JsonSerializer.Serialize(new { orderId = order.Id }),
            CreatedAt = now,
        });
        await kernel.SaveChangesAsync(ct);
        return order;
    }

    /// <summary>Called in the billing transaction when the order's invoice is created.</summary>
    public async Task MarkInvoicedAsync(DiagDbContext diag, long orderId, long invoiceId, CancellationToken ct = default)
    {
        var affected = await diag.Database.ExecuteSqlAsync($"""
            UPDATE diag.test_order SET state = 'invoiced', invoice_id = {invoiceId}
            WHERE id = {orderId} AND state = 'ordered'
            """, ct);
        if (affected == 0) throw new DiagnosticsException("Order already invoiced or moved on (edge 28).");
    }

    /// <summary>
    /// Payment ⇒ InProgress + the TestOrderPaid outbox event (labels print, worklist appears) —
    /// same transaction as the receipt (02 §4: a crash cannot lose the side effect).
    /// </summary>
    public async Task MarkPaidAsync(
        DiagDbContext diag, KernelDbContext kernel, long orderId, CancellationToken ct = default)
    {
        var affected = await diag.Database.ExecuteSqlAsync($"""
            UPDATE diag.test_order SET state = 'in_progress'
            WHERE id = {orderId} AND state = 'invoiced'
            """, ct);
        if (affected == 0) throw new DiagnosticsException("Order is not awaiting payment — refresh (edge 28).");

        kernel.Outbox.Add(new OutboxMessage
        {
            EventType = "TestOrderPaid",
            Payload = JsonSerializer.Serialize(new { orderId }),
            CreatedAt = clock.GetUtcNow(),
        });
        await kernel.SaveChangesAsync(ct);
    }
}
