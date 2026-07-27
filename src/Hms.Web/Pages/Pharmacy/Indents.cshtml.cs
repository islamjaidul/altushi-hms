using Hms.Billing;
using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Pharmacy;
using Hms.Pharmacy.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Pharmacy;

public sealed record IssueQueueRow(
    long Id, string AdmissionNo, string Patient, string? Note, DateTimeOffset RequestedAt,
    IReadOnlyList<(string Product, int Requested, int Available)> Items);

/// <summary>
/// The pharmacy side of 5A-9: requested ward indents issue FEFO from an outlet — expired
/// batches are invisible by predicate, folio lines carry each batch's MRP, and the exact
/// allocations are kept so discharge-time returns restock what actually left.
/// </summary>
[Authorize(Policy = Perm.PharmacyStockManage)]
public class IndentsModel(
    HmsTx tx, BillingService billing, FolioService folios, StockService stock,
    TimeProvider clock) : HmsPageModel
{
    [BindProperty] public long IndentId { get; set; }
    [BindProperty] public long OutletId { get; set; }

    public IReadOnlyList<IssueQueueRow> Queue { get; private set; } = [];
    public IReadOnlyList<Outlet> Outlets { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        await tx.RunAsync(async s =>
        {
            Outlets = await s.Pharm.Outlets.AsNoTracking()
                .Where(o => o.Active).OrderBy(o => o.Id).ToListAsync();

            var indents = await s.Ipd.Indents.AsNoTracking()
                .Where(i => i.State == IndentState.Requested)
                .OrderBy(i => i.Id).Take(50).ToListAsync();
            var indentIds = indents.Select(i => i.Id).ToList();
            var admissionIds = indents.Select(i => i.AdmissionId).Distinct().ToList();
            var admissions = await s.Ipd.Admissions.AsNoTracking()
                .Where(a => admissionIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => admissions.Values.Select(a => a.PatientId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.FullName);
            var items = await s.Ipd.IndentItems.AsNoTracking()
                .Where(i => indentIds.Contains(i.IndentId)).ToListAsync();
            var productIds = items.Select(i => i.ProductId).Distinct().ToList();
            var products = await s.Pharm.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => $"{p.Brand} {p.Strength}");

            // Sellable stock per product over all outlets (issue screen shows the main figure).
            var stockByProduct = await s.Pharm.Batches.AsNoTracking()
                .Where(b => productIds.Contains(b.ProductId) && b.State == BatchState.InStock
                            && b.Expiry > today)
                .GroupBy(b => b.ProductId)
                .Select(g => new { ProductId = g.Key, Qty = g.Sum(b => b.QtyOnHand) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Qty);

            Queue = indents.Select(i =>
            {
                var admission = admissions.GetValueOrDefault(i.AdmissionId);
                return new IssueQueueRow(i.Id, admission?.AdmissionNo ?? "—",
                    admission is not null ? patients.GetValueOrDefault(admission.PatientId, "—") : "—",
                    i.Note, i.RequestedAt,
                    items.Where(x => x.IndentId == i.Id)
                        .Select(x => (products.GetValueOrDefault(x.ProductId, "—"), x.QtyRequested,
                            stockByProduct.GetValueOrDefault(x.ProductId))).ToList());
            }).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostIssueAsync()
    {
        if (OutletId == 0) { await LoadAsync(); Fail("Pick the issuing outlet."); return Page(); }
        try
        {
            await tx.RunAsync(s => IpdBilling.IssueIndentAsync(
                s, billing, folios, stock, BranchId, IndentId, OutletId, ActorId));
        }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        catch (BillingException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Issued FEFO — folio charged at batch MRP", "receipt_long");
        return Redirect("/pharmacy/indents");
    }
}
