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
        var lines = ProductIds.Zip(LineQtys, (p, q) => (p, q)).Zip(LineCosts, (x, c) => (x.p, x.q, c))
            .Where(x => x.p > 0 && x.q > 0)
            .Select(x => new NewPoLine(x.p, x.q, x.c)).ToList();
        if (SupplierId == 0 || OutletId == 0 || lines.Count == 0)
        { await LoadAsync(); Fail("Pick a supplier, an outlet and at least one product line."); return Page(); }

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
