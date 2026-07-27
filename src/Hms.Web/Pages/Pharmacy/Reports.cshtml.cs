using Hms.Pharmacy.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Pharmacy;

public sealed record SalesStatRow(DateOnly Day, int Sales, int Units, long Takings, long Cost);
public sealed record PurchaseStatRow(DateOnly Day, int Receipts, int Units, long Value);
public sealed record ReorderRow(string Product, string Company, int OnHand, int ReorderLevel, int Sold30d);
public sealed record MoveRow(DateTimeOffset At, string Product, string BatchNo, string Kind, int Qty, string? Reason);

/// <summary>
/// §5 M11 [M]: daily/periodic sales & purchase statements, the periodical stock ledger
/// (a query over the append-only <c>stock_move</c>, ADR-0021 #2) and the auto reorder
/// shortlist (US11.3: reorder level vs on-hand vs 30-day sales velocity).
/// </summary>
[Authorize(Policy = Perm.PharmacyRead)]
public class ReportsModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }

    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
    public IReadOnlyList<SalesStatRow> Sales { get; private set; } = [];
    public IReadOnlyList<PurchaseStatRow> Purchases { get; private set; } = [];
    public IReadOnlyList<ReorderRow> Reorder { get; private set; } = [];
    public IReadOnlyList<MoveRow> Ledger { get; private set; } = [];

    public long TotalTakings => Sales.Sum(r => r.Takings);
    public long TotalProfit => Sales.Sum(r => r.Takings - r.Cost);
    public long TotalPurchases => Purchases.Sum(r => r.Value);

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        // The shared date contract (ADR-0020): typed dates always win; default = last 7 days.
        FromDate = Hms.Kernel.Time.FlexibleDate.Parse(From) ?? today.AddDays(-6);
        ToDate = Hms.Kernel.Time.FlexibleDate.Parse(To) ?? today;
        if (ToDate < FromDate) (FromDate, ToDate) = (ToDate, FromDate);
        var start = Ui.DhakaMidnightUtc(FromDate);
        var end = Ui.DhakaMidnightUtc(ToDate.AddDays(1));

        await tx.RunAsync(async s =>
        {
            // Sales: allocations pin qty, MRP and cost per sold line (ADR-0021 #5); grouping by
            // Dhaka day mirrors the billing reports.
            var moves = await s.Pharm.StockMoves.AsNoTracking()
                .Where(m => m.At >= start && m.At < end).ToListAsync();
            var saleAlloc = await s.Pharm.SaleAllocations.AsNoTracking().ToListAsync();
            var allocByLine = saleAlloc.GroupBy(a => a.InvoiceId).ToDictionary(g => g.Key, g => g.ToList());

            var invoices = await s.Bill.Invoices.AsNoTracking()
                .Where(i => i.CreatedAt >= start && i.CreatedAt < end).Select(i => new { i.Id, i.CreatedAt })
                .ToListAsync();
            Sales = invoices
                .Where(i => allocByLine.ContainsKey(i.Id))
                .GroupBy(i => DateOnly.FromDateTime(Ui.Local(i.CreatedAt).DateTime))
                .Select(g =>
                {
                    var allocations = g.SelectMany(i => allocByLine[i.Id]).ToList();
                    return new SalesStatRow(g.Key, g.Count(),
                        allocations.Sum(a => a.Qty),
                        allocations.Sum(a => (long)a.Qty * a.UnitMrp),
                        allocations.Sum(a => (long)a.Qty * a.UnitCost));
                })
                .OrderByDescending(r => r.Day).ToList();

            Purchases = moves.Where(m => m.Kind == MoveKind.Receive)
                .GroupBy(m => DateOnly.FromDateTime(Ui.Local(m.At).DateTime))
                .Select(g => new PurchaseStatRow(g.Key, g.Count(), g.Sum(m => m.Qty), 0))
                .OrderByDescending(r => r.Day).ToList();
            // Value at cost joins the batch rows the receives created.
            var receiveBatchIds = moves.Where(m => m.Kind == MoveKind.Receive).Select(m => m.BatchId).Distinct().ToList();
            var costs = await s.Pharm.Batches.AsNoTracking()
                .Where(b => receiveBatchIds.Contains(b.Id))
                .Select(b => new { b.Id, b.Cost }).ToDictionaryAsync(b => b.Id, b => b.Cost);
            Purchases = moves.Where(m => m.Kind == MoveKind.Receive)
                .GroupBy(m => DateOnly.FromDateTime(Ui.Local(m.At).DateTime))
                .Select(g => new PurchaseStatRow(g.Key, g.Count(), g.Sum(m => m.Qty),
                    g.Sum(m => m.Qty * costs.GetValueOrDefault(m.BatchId))))
                .OrderByDescending(r => r.Day).ToList();

            // Reorder shortlist (US11.3): sellable on-hand ≤ ROL, with 30-day velocity shown.
            var today2 = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
            var products = await s.Pharm.Products.AsNoTracking().Where(p => p.Active).ToListAsync();
            var companies = await s.Pharm.Companies.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);
            var onHand = await s.Pharm.Batches.AsNoTracking()
                .Where(b => b.State == BatchState.InStock && b.Expiry > today2)
                .GroupBy(b => b.ProductId)
                .Select(g => new { g.Key, Qty = g.Sum(b => b.QtyOnHand) })
                .ToDictionaryAsync(x => x.Key, x => x.Qty);
            var cutoff = clock.GetUtcNow().AddDays(-30);
            var sold = await s.Pharm.StockMoves.AsNoTracking()
                .Where(m => m.Kind == MoveKind.Sale && m.At >= cutoff)
                .GroupBy(m => m.ProductId)
                .Select(g => new { g.Key, Qty = -g.Sum(m => m.Qty) })
                .ToDictionaryAsync(x => x.Key, x => x.Qty);
            Reorder = products
                .Where(p => onHand.GetValueOrDefault(p.Id) <= p.ReorderLevel)
                .Select(p => new ReorderRow($"{p.Brand} {p.Strength} {p.Form}",
                    companies.GetValueOrDefault(p.CompanyId, "—"),
                    onHand.GetValueOrDefault(p.Id), p.ReorderLevel, sold.GetValueOrDefault(p.Id)))
                .OrderBy(r => r.OnHand - r.ReorderLevel).ToList();

            // Periodical stock ledger over the ranged moves.
            var productNames = products.ToDictionary(p => p.Id, p => $"{p.Brand} {p.Strength}");
            var batchNos = await s.Pharm.Batches.AsNoTracking()
                .Where(b => moves.Select(m => m.BatchId).Distinct().Contains(b.Id))
                .Select(b => new { b.Id, b.BatchNo }).ToDictionaryAsync(b => b.Id, b => b.BatchNo);
            Ledger = moves.OrderByDescending(m => m.At).Take(200)
                .Select(m => new MoveRow(m.At, productNames.GetValueOrDefault(m.ProductId, "—"),
                    batchNos.GetValueOrDefault(m.BatchId, "—"), m.Kind, m.Qty, m.Reason)).ToList();
            return 0;
        });
    }
}
