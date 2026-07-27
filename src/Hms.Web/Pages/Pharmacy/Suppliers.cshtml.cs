using Hms.Pharmacy;
using Hms.Pharmacy.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Pharmacy;

public sealed record SupplierRow(long Id, string Name, string? Phone, long Payable);
public sealed record LedgerRow(DateTimeOffset At, string Kind, long Amount, string? Note);

/// <summary>
/// §5 M11 [M] supplier ledger & payment + 5A-11 supplier replacement. The ledger is
/// append-only: purchases raise the payable, payments and return credits reduce it —
/// the balance is a SUM, never a mutable column.
/// </summary>
[Authorize(Policy = Perm.PharmacyPurchaseManage)]
public class SuppliersModel(HmsTx tx, PurchaseService purchase) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? Ledger { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public long SupplierId { get; set; }
    [BindProperty] public long Amount { get; set; }
    [BindProperty] public string? Note { get; set; }

    public IReadOnlyList<SupplierRow> Rows { get; private set; } = [];
    public IReadOnlyList<LedgerRow> Entries { get; private set; } = [];
    public string? LedgerFor { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            var suppliers = await s.Pharm.Suppliers.AsNoTracking().OrderBy(x => x.Name).ToListAsync();
            var balances = await s.Pharm.SupplierLedger.AsNoTracking()
                .GroupBy(e => e.SupplierId)
                .Select(g => new { g.Key, Balance = g.Sum(e => e.Amount) })
                .ToDictionaryAsync(x => x.Key, x => x.Balance);

            Rows = suppliers.Select(x => new SupplierRow(x.Id, x.Name, x.Phone,
                balances.GetValueOrDefault(x.Id))).ToList();

            if (Ledger is { } sid)
            {
                LedgerFor = suppliers.FirstOrDefault(x => x.Id == sid)?.Name;
                Entries = await s.Pharm.SupplierLedger.AsNoTracking()
                    .Where(e => e.SupplierId == sid)
                    .OrderByDescending(e => e.At).Take(100)
                    .Select(e => new LedgerRow(e.At, e.Kind, e.Amount, e.Note))
                    .ToListAsync();
            }
            return 0;
        });
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Name)) { await LoadAsync(); Fail("The supplier needs a name."); return Page(); }
        await tx.RunAsync(async s =>
        {
            s.Pharm.Suppliers.Add(new Supplier { Name = Name!.Trim(), Phone = Phone?.Trim() });
            await s.Pharm.SaveChangesAsync();
            return 0;
        });
        Toast($"Supplier added: {Name}", "local_shipping");
        return Redirect("/pharmacy/suppliers");
    }

    public async Task<IActionResult> OnPostPayAsync()
    {
        try
        {
            await tx.RunAsync(s => purchase.RecordPaymentAsync(
                s.Pharm, s.Kernel, BranchId, SupplierId, Amount, Note, ActorId, ActorName));
            Toast($"Payment of {Ui.Money(Amount)} recorded", "payments");
        }
        catch (PharmacyException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        return Redirect($"/pharmacy/suppliers?Ledger={SupplierId}");
    }
}
