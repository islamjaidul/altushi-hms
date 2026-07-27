namespace Hms.Pharmacy.Contracts;

/// <summary>
/// What a pharmacy sale took from stock, per charge line — the cross-module fact other
/// modules may read (refund restocking, profit reporting) without touching pharm tables.
/// </summary>
public sealed record BatchAllocation(
    long BatchId, long ProductId, string BatchNo, DateOnly Expiry, int Qty, long UnitMrp, long UnitCost,
    bool NearExpiry);
