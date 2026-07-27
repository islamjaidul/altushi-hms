using Hms.Ipd;
using Hms.Ipd.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ipd;

public sealed record WardIndentRow(
    long Id, long AdmissionId, string AdmissionNo, string Patient, string State, string? Note,
    DateTimeOffset RequestedAt,
    IReadOnlyList<(string Product, int Requested, int Issued, int Returned)> Items);

/// <summary>
/// 5A-9: the ward's requisition ledger — every indent, its state, and where it went.
/// Raising happens on the folio; issuing happens on the pharmacy side (custody stays split).
/// </summary>
[Authorize(Policy = Perm.IpdServicePost)]
public class IndentsModel(HmsTx tx, FolioService folios) : HmsPageModel
{
    [BindProperty] public long IndentId { get; set; }

    public IReadOnlyList<WardIndentRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            var indents = await s.Ipd.Indents.AsNoTracking()
                .OrderByDescending(i => i.Id).Take(50).ToListAsync();
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

            Rows = indents.Select(i =>
            {
                var admission = admissions.GetValueOrDefault(i.AdmissionId);
                return new WardIndentRow(i.Id, i.AdmissionId, admission?.AdmissionNo ?? "—",
                    admission is not null ? patients.GetValueOrDefault(admission.PatientId, "—") : "—",
                    i.State, i.Note, i.RequestedAt,
                    items.Where(x => x.IndentId == i.Id)
                        .Select(x => (products.GetValueOrDefault(x.ProductId, "—"),
                            x.QtyRequested, x.QtyIssued, x.QtyReturned)).ToList());
            }).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        try
        {
            await tx.RunAsync(s => folios.CancelIndentAsync(s.Ipd, s.Kernel, IndentId, ActorId, ActorName));
        }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Indent cancelled", "close");
        return Redirect("/ipd/indents");
    }
}
