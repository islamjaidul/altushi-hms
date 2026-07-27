using Hms.Pharmacy;
using Hms.Pharmacy.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Pharmacy;

/// <summary>
/// §5 M11 [S] pharmacy dashboard (US11.4): today's takings and profit, stock value at cost,
/// what is near expiry, and what is short. The MD sees pharmacy income on the main dashboard
/// through the money spine; this is the pharmacist's own morning view.
/// </summary>
[Authorize(Policy = Perm.PharmacyRead)]
public class DashboardModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    public long TodayTakings { get; private set; }
    public long TodayProfit { get; private set; }
    public int TodaySales { get; private set; }
    public long StockValueAtCost { get; private set; }
    public int NearExpiryBatches { get; private set; }
    public int ExpiredAwaitingAction { get; private set; }
    public int ShortItems { get; private set; }
    public long SupplierPayable { get; private set; }

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var near = today.AddDays(StockService.NearExpiryDays);
        var start = Ui.DhakaMidnightUtc(today);
        var end = Ui.DhakaMidnightUtc(today.AddDays(1));

        await tx.RunAsync(async s =>
        {
            var todaysInvoices = await s.Bill.Invoices.AsNoTracking()
                .Where(i => i.CreatedAt >= start && i.CreatedAt < end)
                .Select(i => i.Id).ToListAsync();
            var allocations = await s.Pharm.SaleAllocations.AsNoTracking()
                .Where(a => todaysInvoices.Contains(a.InvoiceId)).ToListAsync();
            TodaySales = allocations.Select(a => a.InvoiceId).Distinct().Count();
            TodayTakings = allocations.Sum(a => (long)a.Qty * a.UnitMrp);
            TodayProfit = allocations.Sum(a => (long)a.Qty * (a.UnitMrp - a.UnitCost));

            var batches = await s.Pharm.Batches.AsNoTracking().ToListAsync();
            StockValueAtCost = batches
                .Where(b => b.State == BatchState.InStock && b.Expiry > today)
                .Sum(b => (long)b.QtyOnHand * b.Cost);
            NearExpiryBatches = batches.Count(b =>
                b.State == BatchState.InStock && b.Expiry > today && b.Expiry <= near && b.QtyOnHand > 0);
            ExpiredAwaitingAction = batches.Count(b =>
                (b.State == BatchState.InStock && b.Expiry <= today && b.QtyOnHand > 0)
                || (b.State == BatchState.Quarantined && b.QtyOnHand > 0));

            var products = await s.Pharm.Products.AsNoTracking().Where(p => p.Active).ToListAsync();
            var onHand = batches
                .Where(b => b.State == BatchState.InStock && b.Expiry > today)
                .GroupBy(b => b.ProductId).ToDictionary(g => g.Key, g => g.Sum(b => b.QtyOnHand));
            ShortItems = products.Count(p => onHand.GetValueOrDefault(p.Id) <= p.ReorderLevel);

            SupplierPayable = await s.Pharm.SupplierLedger.AsNoTracking().SumAsync(e => (long?)e.Amount) ?? 0;
            return 0;
        });
    }
}
