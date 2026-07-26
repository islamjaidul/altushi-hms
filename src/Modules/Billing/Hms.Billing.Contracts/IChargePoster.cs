namespace Hms.Billing.Contracts;

/// <summary>One line another module posts into billing (04 §3). Price is already resolved (C6).</summary>
public sealed record ChargeToPost(
    string CatalogKind, long CatalogId, string Description, int Qty, long UnitPrice,
    long? RateVersionId, long? DoctorId, long? ReferrerId, long? TestOrderId);

/// <summary>
/// 04 §3: the ONLY write path into money for other modules. Charges land unbilled; the billing
/// counter invoices them (SSE seam). Call inside the caller's transaction — atomicity is free
/// because it is the same database (04 §1 rule 3).
/// </summary>
public interface IChargePoster
{
    Task<IReadOnlyList<long>> PostChargesAsync(
        long branchId, long encounterId, string sourceModule,
        IReadOnlyList<ChargeToPost> lines, long actorId, CancellationToken ct = default);
}
