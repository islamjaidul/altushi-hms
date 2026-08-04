using Hms.Billing;
using Hms.Pharmacy;
using Hms.Pharmacy.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

public sealed record SaleItem(long ProductId, int Qty);

/// <summary>
/// Composes a pharmacy sale across the two spines (ADR-0021 #5): FEFO stock allocation in
/// pharm.* and money in bill.* — one charge line **per batch** so every printed price is the
/// batch MRP it actually came from, and refunds can restock the exact batches. Lives in the
/// composition root like <see cref="DiagnosticsRelease"/> because it needs both modules'
/// services (ADR-0003: modules never reference each other).
/// </summary>
public static class PharmacySale
{
    public const string EncounterType = "PHARM";

    /// <summary>The standing patient for anonymous retail. Credit (a due) is refused on it —
    /// an anonymous due can never be followed up.</summary>
    public const string WalkInUhid = "SEH-WALKIN";   // renamed with the series (spec 0050); DevSeed migrates the old row

    public static async Task<long> EnsureWalkInPatientAsync(TxScope s, long branchId, long actorId)
    {
        var existing = await s.Reg.Patients.AsNoTracking()
            .Where(p => p.Uhid == WalkInUhid).Select(p => (long?)p.Id).FirstOrDefaultAsync();
        if (existing is not null) return existing.Value;

        var walkIn = new Hms.Registration.Data.Patient
        {
            BranchId = branchId, Uhid = WalkInUhid, FullName = "Walk-in Customer", Sex = 'O',
            PatientType = "walk-in", UnknownIdentity = true,   // ck_identity: no DOB/age by design
            CreatedAt = DateTimeOffset.UtcNow, CreatedBy = actorId,
        };
        s.Reg.Patients.Add(walkIn);
        await s.Reg.SaveChangesAsync();
        return walkIn.Id;
    }

    public static async Task<long> SaveAsync(
        TxScope s, BillingService billing, StockService stock, TimeProvider clock,
        long branchId, long outletId, OpenSession session, long patientId,
        IReadOnlyList<SaleItem> items, long discount, long? discountApprovalId,
        IReadOnlyList<(long Amount, string Mode, string? Reference)> tenders,
        long actorId, string actorName, Guid? submissionToken = null,
        bool staffSale = false, Hms.Kernel.Audit.AuditWriter? audit = null)
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var encounter = await CounterContext.GetOrCreateEncounterAsync(
            s.Bill, branchId, patientId, session.CounterId, today, EncounterType,
            actorId, clock.GetUtcNow());

        // FEFO first (locks batches), then one charge line per allocation at that batch's MRP.
        var lineAllocations = new List<(Hms.Pharmacy.Contracts.BatchAllocation Allocation, long ChargeLineId)>();
        foreach (var item in items)
        {
            var product = await s.Pharm.Products.AsNoTracking().SingleAsync(p => p.Id == item.ProductId);
            var allocations = await stock.AllocateFefoAsync(
                s.Pharm, outletId, item.ProductId, item.Qty, "bill.encounter", encounter.Id, actorId);
            foreach (var a in allocations)
            {
                var line = await billing.PostChargeAsync(s.Bill, branchId, encounter.Id, "Pharmacy",
                    new NewChargeLine("medicine", product.Id,
                        $"{product.Brand} {product.Strength} {product.Form} (batch {a.BatchNo})",
                        a.Qty, a.UnitMrp), actorId);
                lineAllocations.Add((a, line.Id));
            }
        }

        var invoice = await billing.CreateInvoiceAsync(
            s.Bill, s.Kernel, branchId, encounter.Id, session.Id, patientId,
            0m, discount, discountApprovalId, actorId, actorName,
            submissionToken: submissionToken);

        foreach (var (a, chargeLineId) in lineAllocations)
            s.Pharm.SaleAllocations.Add(new SaleAllocation
            {
                BranchId = branchId, InvoiceId = invoice.Id, ChargeLineId = chargeLineId,
                BatchId = a.BatchId, ProductId = a.ProductId, Qty = a.Qty,
                UnitMrp = a.UnitMrp, UnitCost = a.UnitCost,
            });
        await s.Pharm.SaveChangesAsync();

        // 5A-11 staff-pharmacy variant: the TAG is a fact about the sale, so it is recorded
        // whether or not a discount was asked for. Before spec 0022 it survived only inside a
        // discount's approval reason, so a staff member paying full MRP left no trace at all.
        if (staffSale && audit is not null)
        {
            audit.Append(s.Kernel, branchId, actorId, actorName,
                "pharmacy.sale.staff", "bill.invoice", invoice.Id,
                after: new { invoice.InvoiceNo, patientId, staffSale = true }, tier: 2);
            await s.Kernel.SaveChangesAsync();
        }

        foreach (var (amount, mode, reference) in tenders)
            await billing.CollectAsync(s.Bill, s.Kernel, branchId, invoice.Id, session.Id,
                amount, mode, reference, actorId, actorName);

        return invoice.Id;
    }
}
