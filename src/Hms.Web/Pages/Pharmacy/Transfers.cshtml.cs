using Hms.Pharmacy;
using Hms.Pharmacy.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Pharmacy;

public sealed record TransferRow(
    long Id, string From, string To, string State, DateTimeOffset CreatedAt,
    IReadOnlyList<(string Product, int Requested, int Sent)> Lines);

/// <summary>
/// 5A-11 multi-outlet: outlets master, stock transfer indent → send (FEFO at source) →
/// receive (identical batch identity at destination), and the outlet transfer ledger.
/// </summary>
[Authorize(Policy = Perm.PharmacyStockManage)]
public class TransfersModel(HmsTx tx, StockService stock, TimeProvider clock) : HmsPageModel
{
    [BindProperty] public string? OutletName { get; set; }
    [BindProperty] public string OutletKind { get; set; } = "sub";
    [BindProperty] public long FromOutletId { get; set; }
    [BindProperty] public long ToOutletId { get; set; }
    [BindProperty] public List<long> ProductIds { get; set; } = [];
    [BindProperty] public List<int> LineQtys { get; set; } = [];
    [BindProperty] public long TransferId { get; set; }

    public IReadOnlyList<Outlet> Outlets { get; private set; } = [];
    public IReadOnlyList<PharmProduct> Products { get; private set; } = [];
    public IReadOnlyList<TransferRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            Outlets = await s.Pharm.Outlets.AsNoTracking().OrderBy(o => o.Id).ToListAsync();
            Products = await s.Pharm.Products.AsNoTracking().Where(p => p.Active).OrderBy(p => p.Brand).ToListAsync();

            var transfers = await s.Pharm.Transfers.AsNoTracking()
                .OrderByDescending(t => t.Id).Take(25).ToListAsync();
            var ids = transfers.Select(t => t.Id).ToList();
            var lines = await s.Pharm.TransferLines.AsNoTracking()
                .Where(l => ids.Contains(l.TransferId)).ToListAsync();
            var outletNames = Outlets.ToDictionary(o => o.Id, o => o.Name);
            var productNames = Products.ToDictionary(p => p.Id, p => $"{p.Brand} {p.Strength}");

            Rows = transfers.Select(t => new TransferRow(t.Id,
                outletNames.GetValueOrDefault(t.FromOutletId, "—"),
                outletNames.GetValueOrDefault(t.ToOutletId, "—"), t.State, t.CreatedAt,
                lines.Where(l => l.TransferId == t.Id)
                    .Select(l => (productNames.GetValueOrDefault(l.ProductId, "—"), l.RequestedQty, l.SentQty))
                    .ToList())).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostOutletAsync()
    {
        if (string.IsNullOrWhiteSpace(OutletName))
        { await LoadAsync(); Fail("The outlet needs a name."); return Page(); }
        await tx.RunAsync(async s =>
        {
            s.Pharm.Outlets.Add(new Outlet { BranchId = BranchId, Name = OutletName!.Trim(), Kind = OutletKind });
            await s.Pharm.SaveChangesAsync();
            return 0;
        });
        Toast($"Outlet added: {OutletName}", "store");
        return Redirect("/pharmacy/transfers");
    }

    public async Task<IActionResult> OnPostIndentAsync()
    {
        var lines = ProductIds.Zip(LineQtys, (p, q) => (p, q)).Where(x => x.p > 0 && x.q > 0).ToList();
        if (FromOutletId == 0 || ToOutletId == 0 || FromOutletId == ToOutletId || lines.Count == 0)
        { await LoadAsync(); Fail("Pick two different outlets and at least one product line."); return Page(); }

        await tx.RunAsync(async s =>
        {
            var transfer = new Transfer
            {
                BranchId = BranchId, FromOutletId = FromOutletId, ToOutletId = ToOutletId,
                CreatedAt = clock.GetUtcNow(), CreatedBy = ActorId,
            };
            s.Pharm.Transfers.Add(transfer);
            await s.Pharm.SaveChangesAsync();
            foreach (var (p, q) in lines)
                s.Pharm.TransferLines.Add(new TransferLine
                { TransferId = transfer.Id, ProductId = p, RequestedQty = q });
            await s.Pharm.SaveChangesAsync();
            return 0;
        });
        Toast("Transfer indent raised", "sync_alt");
        return Redirect("/pharmacy/transfers");
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        try
        {
            await tx.RunAsync(s => stock.SendTransferAsync(s.Pharm, s.Kernel, BranchId, TransferId, ActorId, ActorName));
            Toast("Sent — stock left the source outlet FEFO", "sync_alt");
        }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        return Redirect("/pharmacy/transfers");
    }

    public async Task<IActionResult> OnPostReceiveAsync()
    {
        try
        {
            await tx.RunAsync(s => stock.ReceiveTransferAsync(s.Pharm, s.Kernel, BranchId, TransferId, ActorId, ActorName));
            Toast("Received — batches recreated at the destination outlet", "task_alt");
        }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        return Redirect("/pharmacy/transfers");
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        await tx.RunAsync(async s =>
        {
            await s.Pharm.Database.ExecuteSqlAsync($"""
                UPDATE pharm.transfer SET state = {TransferState.Cancelled}
                WHERE id = {TransferId} AND state = {TransferState.Indent}
                """);
            return 0;
        });
        return Redirect("/pharmacy/transfers");
    }
}
