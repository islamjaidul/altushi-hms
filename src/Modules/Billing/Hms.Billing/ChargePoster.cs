using Hms.Billing.Contracts;
using Hms.Billing.Data;

namespace Hms.Billing;

/// <summary>IChargePoster over the ambient BillDbContext (scoped per request/transaction).</summary>
public sealed class ChargePoster(BillDbContext bill, BillingService billing) : IChargePoster
{
    public async Task<IReadOnlyList<long>> PostChargesAsync(
        long branchId, long encounterId, string sourceModule,
        IReadOnlyList<ChargeToPost> lines, long actorId, CancellationToken ct = default)
    {
        var ids = new List<long>(lines.Count);
        foreach (var l in lines)
        {
            var charge = await billing.PostChargeAsync(bill, branchId, encounterId, sourceModule,
                new NewChargeLine(l.CatalogKind, l.CatalogId, l.Description, l.Qty, l.UnitPrice,
                    l.RateVersionId, l.DoctorId, l.ReferrerId, l.TestOrderId), actorId, ct);
            ids.Add(charge.Id);
        }
        return ids;
    }
}
