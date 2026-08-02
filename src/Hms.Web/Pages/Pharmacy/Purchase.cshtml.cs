using Hms.Kernel.Approvals;
using Hms.Pharmacy;
using Hms.Pharmacy.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Pharmacy;

public sealed record PoRow(
    long Id, string Supplier, string Outlet, string State, DateTimeOffset CreatedAt,
    IReadOnlyList<PoLineRow> Lines);
public sealed record PoLineRow(long Id, string Product, int Qty, int ReceivedQty, long ExpectedCost);

/// <summary>
/// §5 M11 [M] purchase order → stock receive → return, on the §11 PO machine:
/// Requested → Approved⚿ → Ordered → Partially Received → Received → Closed · Cancelled.
/// Receiving is per line (GRN): batch no, expiry, qty, actual cost, MRP — the batch is the
/// price record from that moment on (ADR-0021 #6).
/// </summary>
[Authorize(Policy = Perm.PharmacyPurchaseManage)]
public class PurchaseModel(
    HmsTx tx, PurchaseService purchase, ApprovalEngine approvals) : HmsPageModel
{
    [BindProperty] public long SupplierId { get; set; }
    [BindProperty] public long OutletId { get; set; }
    [BindProperty] public List<long> ProductIds { get; set; } = [];
    [BindProperty] public List<int> LineQtys { get; set; } = [];
    [BindProperty] public List<long> LineCosts { get; set; } = [];
    [BindProperty] public long PoId { get; set; }
    [BindProperty] public long PoLineId { get; set; }
    [BindProperty] public string? BatchNo { get; set; }
    [BindProperty] public string? Expiry { get; set; }
    [BindProperty] public int Qty { get; set; }
    [BindProperty] public long UnitCost { get; set; }
    [BindProperty] public long UnitMrp { get; set; }

    public IReadOnlyList<Supplier> Suppliers { get; private set; } = [];
    public IReadOnlyList<Outlet> Outlets { get; private set; } = [];
    public IReadOnlyList<PharmProduct> Products { get; private set; } = [];
    public IReadOnlyList<PoRow> Orders { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            Suppliers = await s.Pharm.Suppliers.AsNoTracking().Where(x => x.Active).OrderBy(x => x.Name).ToListAsync();
            Outlets = await s.Pharm.Outlets.AsNoTracking().Where(x => x.Active).OrderBy(x => x.Id).ToListAsync();
            Products = await s.Pharm.Products.AsNoTracking().Where(p => p.Active).OrderBy(p => p.Brand).ToListAsync();

            var orders = await s.Pharm.PurchaseOrders.AsNoTracking()
                .OrderByDescending(o => o.Id).Take(30).ToListAsync();
            var ids = orders.Select(o => o.Id).ToList();
            var lines = await s.Pharm.PurchaseOrderLines.AsNoTracking()
                .Where(l => ids.Contains(l.PurchaseOrderId)).ToListAsync();
            var supplierNames = Suppliers.ToDictionary(x => x.Id, x => x.Name);
            var outletNames = Outlets.ToDictionary(x => x.Id, x => x.Name);
            var productNames = Products.ToDictionary(x => x.Id, x => $"{x.Brand} {x.Strength} {x.Form}");

            Orders = orders.Select(o => new PoRow(o.Id,
                supplierNames.GetValueOrDefault(o.SupplierId, "—"),
                outletNames.GetValueOrDefault(o.OutletId, "—"), o.State, o.CreatedAt,
                lines.Where(l => l.PurchaseOrderId == o.Id)
                    .Select(l => new PoLineRow(l.Id, productNames.GetValueOrDefault(l.ProductId, "—"),
                        l.Qty, l.ReceivedQty, l.ExpectedCost)).ToList())).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        // Spec 0039 WP1 (AUD-VAL-18): the three lists are parallel rows of one grid. Ragged
        // lists are a crafted post, not an operator mistake — refuse, never zip-and-guess.
        if (ProductIds.Count != LineQtys.Count || ProductIds.Count != LineCosts.Count)
        { await LoadAsync(); Fail("The order lines came back garbled — re-enter them and try again."); return Page(); }

        var lines = new List<NewPoLine>();
        for (var i = 0; i < ProductIds.Count; i++)
        {
            if (ProductIds[i] == 0 && LineQtys[i] <= 0) continue;      // untouched blank row
            if (ProductIds[i] == 0)
            { await LoadAsync(); Fail($"Line {i + 1}: pick the product for the quantity you entered."); return Page(); }
            if (LineQtys[i] is < 1 or > 9_999)
            { await LoadAsync(); Fail($"Line {i + 1}: the quantity must be between 1 and 9,999."); return Page(); }
            if (LineCosts[i] is < 0 or > 100_000_000)
            { await LoadAsync(); Fail($"Line {i + 1}: the unit cost must be a whole-taka amount between 0 and 10,00,00,000."); return Page(); }
            lines.Add(new NewPoLine(ProductIds[i], LineQtys[i], LineCosts[i]));
        }
        if (SupplierId == 0 || OutletId == 0 || lines.Count == 0)
        { await LoadAsync(); Fail("Pick a supplier, an outlet and at least one product line."); return Page(); }

        // AUD-VAL-18h: every id on the order must name a row that exists — the dropdown
        // constrains the browser, not the request.
        var refused = await tx.RunAsync(async s =>
        {
            if (!await s.Pharm.Suppliers.AnyAsync(x => x.Id == SupplierId && x.Active))
                return "That supplier is not on the supplier list — pick one from the list.";
            if (!await s.Pharm.Outlets.AnyAsync(x => x.Id == OutletId && x.Active))
                return "That outlet does not exist — pick one from the list.";
            var ids = lines.Select(l => l.ProductId).Distinct().ToList();
            var known = await s.Pharm.Products.AsNoTracking()
                .Where(p => ids.Contains(p.Id) && p.Active).CountAsync();
            return known == ids.Count
                ? null
                : "A product on this order is not in the catalogue any more — pick it from the list again.";
        });
        if (refused is not null) { await LoadAsync(); Fail(refused); return Page(); }

        try
        {
            var po = await tx.RunAsync(s => purchase.CreateAsync(
                s.Pharm, s.Kernel, BranchId, SupplierId, OutletId, lines, ActorId, ActorName));
            // ⚿ Requested → Approved rides the approval engine; small POs auto-approve by policy.
            var raise = await tx.RunAsync(s => approvals.RaiseAsync(
                s.Kernel, BranchId, "purchase-order", "pharm.purchase_order", po.Id,
                ActorId, ActorRole, "purchase order", lines.Sum(l => l.Qty * l.ExpectedCost)));
            if (raise.AutoApproved)
                await tx.RunAsync(s => purchase.ApproveAsync(s.Pharm, s.Kernel, po.Id, raise.ApprovalId!.Value));
            Toast(raise.AutoApproved
                ? $"PO #{po.Id} created and approved under your limit"
                : $"PO #{po.Id} created — approval requested", "receipt_long");
        }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        return Redirect("/pharmacy/purchase");
    }

    public async Task<IActionResult> OnPostAdvanceAsync()
    {
        try
        {
            await tx.RunAsync(async s =>
            {
                var po = await s.Pharm.PurchaseOrders.AsNoTracking().SingleAsync(p => p.Id == PoId);
                switch (po.State)
                {
                    case PoState.Requested:
                        // The approver may have decided in the inbox since this page rendered.
                        var approval = await s.Kernel.ApprovalRequests.AsNoTracking()
                            .Where(a => a.Type == "purchase-order" && a.SourceTable == "pharm.purchase_order"
                                        && a.SourceId == PoId && a.State == Hms.Kernel.Data.ApprovalState.Approved)
                            .OrderByDescending(a => a.Id).FirstOrDefaultAsync()
                            ?? throw new PharmacyException("Still waiting for approval (§11 ⚿).");
                        await purchase.ApproveAsync(s.Pharm, s.Kernel, PoId, approval.Id);
                        break;
                    case PoState.Approved:
                        await purchase.OrderAsync(s.Pharm, PoId);
                        break;
                    case PoState.Received:
                        await purchase.CloseAsync(s.Pharm, PoId);
                        break;
                    default:
                        throw new PharmacyException("Nothing to advance from here — receive the lines.");
                }
                return 0;
            });
            Toast("Purchase order advanced", "task_alt");
        }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        return Redirect("/pharmacy/purchase");
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        try
        {
            await tx.RunAsync(s => purchase.CancelAsync(s.Pharm, PoId));
            Toast("Purchase order cancelled", "close");
        }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        return Redirect("/pharmacy/purchase");
    }

    public async Task<IActionResult> OnPostReceiveAsync()
    {
        if (string.IsNullOrWhiteSpace(BatchNo))
        { await LoadAsync(); Fail("The batch number comes off the carton — it is the recall identity."); return Page(); }
        if (!Hms.Kernel.Time.FlexibleDate.TryParse(Expiry, out var expiry))
        { await LoadAsync(); Fail("Couldn't read the expiry — try 12/03/2028, 2028-03-12 or 12 Mar 2028."); return Page(); }

        try
        {
            await tx.RunAsync(s => purchase.ReceiveLineAsync(
                s.Pharm, s.Kernel, BranchId, PoId, PoLineId, BatchNo!.Trim(), expiry,
                Qty, UnitCost, UnitMrp, ActorId, ActorName));
            Toast($"Received {Qty} × {BatchNo} — on the shelf and on the supplier ledger", "inventory_2");
        }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        return Redirect("/pharmacy/purchase");
    }
}
