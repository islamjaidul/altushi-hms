using Hms.Pharmacy.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Pharmacy;

public sealed record ProductRow(
    long Id, string Brand, string Generic, string Strength, string Form, string Unit,
    string Company, int ReorderLevel, int OnHand, bool Active);

/// <summary>
/// §5 M11 [M] company (manufacturer) & product registration: product = brand + generic +
/// strength + form + unit. MRP and cost live on batches (ADR-0021 #6), not here — a product
/// row is identity, a batch row is price.
/// </summary>
[Authorize(Policy = Perm.PharmacyPurchaseManage)]
public class ProductsModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty] public string? CompanyName { get; set; }
    [BindProperty] public long CompanyId { get; set; }
    [BindProperty] public string? Brand { get; set; }
    [BindProperty] public string? Generic { get; set; }
    [BindProperty] public string? Strength { get; set; }
    [BindProperty] public string Form { get; set; } = "tablet";
    [BindProperty] public string Unit { get; set; } = "pcs";
    [BindProperty] public int ReorderLevel { get; set; }
    [BindProperty] public long ProductId { get; set; }

    public IReadOnlyList<Company> Companies { get; private set; } = [];
    public IReadOnlyList<ProductRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            Companies = await s.Pharm.Companies.AsNoTracking().Where(c => c.Active)
                .OrderBy(c => c.Name).ToListAsync();
            var companies = Companies.ToDictionary(c => c.Id, c => c.Name);

            var term = Q?.Trim();
            var query = s.Pharm.Products.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(term))
            {
                var like = $"%{term}%";
                query = query.Where(p => EF.Functions.ILike(p.Brand, like)
                                         || EF.Functions.ILike(p.Generic, like));
            }
            var products = await query.OrderBy(p => p.Brand).Take(200).ToListAsync();

            var ids = products.Select(p => p.Id).ToList();
            var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
            var onHand = await s.Pharm.Batches.AsNoTracking()
                .Where(b => ids.Contains(b.ProductId) && b.State == BatchState.InStock && b.Expiry > today)
                .GroupBy(b => b.ProductId)
                .Select(g => new { g.Key, Qty = g.Sum(b => b.QtyOnHand) })
                .ToDictionaryAsync(x => x.Key, x => x.Qty);

            Rows = products.Select(p => new ProductRow(p.Id, p.Brand, p.Generic, p.Strength,
                p.Form, p.Unit, companies.GetValueOrDefault(p.CompanyId, "—"), p.ReorderLevel,
                onHand.GetValueOrDefault(p.Id), p.Active)).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostCompanyAsync()
    {
        if (string.IsNullOrWhiteSpace(CompanyName))
        { await LoadAsync(); Fail("The company needs a name."); return Page(); }
        await tx.RunAsync(async s =>
        {
            if (!await s.Pharm.Companies.AnyAsync(c => c.Name == CompanyName!.Trim()))
                s.Pharm.Companies.Add(new Company { Name = CompanyName!.Trim() });
            await s.Pharm.SaveChangesAsync();
            return 0;
        });
        Toast($"Company saved: {CompanyName}", "factory");
        return Redirect("/pharmacy/products");
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Brand) || string.IsNullOrWhiteSpace(Generic))
        { await LoadAsync(); Fail("Brand and generic are both required — sale search covers both."); return Page(); }
        if (CompanyId == 0)
        { await LoadAsync(); Fail("Pick the manufacturer."); return Page(); }

        await tx.RunAsync(async s =>
        {
            s.Pharm.Products.Add(new PharmProduct
            {
                BranchId = BranchId, CompanyId = CompanyId, Brand = Brand!.Trim(),
                Generic = Generic!.Trim(), Strength = Strength?.Trim() ?? "",
                Form = Form, Unit = Unit, ReorderLevel = Math.Max(0, ReorderLevel),
                CreatedAt = clock.GetUtcNow(), CreatedBy = ActorId,
            });
            await s.Pharm.SaveChangesAsync();
            return 0;
        });
        Toast($"Product registered: {Brand} ({Generic})", "science");
        return Redirect("/pharmacy/products");
    }

    /// <summary>Deactivation, never deletion — a sold product's name must stay printable.</summary>
    public async Task<IActionResult> OnPostToggleAsync()
    {
        await tx.RunAsync(async s =>
        {
            var p = await s.Pharm.Products.SingleAsync(x => x.Id == ProductId);
            p.Active = !p.Active;
            await s.Pharm.SaveChangesAsync();
            return 0;
        });
        return Redirect("/pharmacy/products");
    }
}
