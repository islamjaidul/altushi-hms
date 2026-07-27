using Hms.Admin;
using Hms.Billing;
using Hms.Ipd;
using Hms.Ot;
using Hms.Ot.Data;
using Hms.Pharmacy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ot;

/// <summary>
/// One case, from "patient sent for" to "completed and billed". US7.2's guarantee lives on this
/// screen's completion button: the charges derive from the catalogue and the team, in the same
/// transaction as the state change, so an operation cannot be completed without being billed.
/// </summary>
[Authorize(Policy = Perm.OtRecord)]
public class CaseModel(
    HmsTx tx, OtService ot, BillingService billing, FolioService folios, RateResolver rates,
    StockService stock, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    [BindProperty] public string? Findings { get; set; }
    [BindProperty] public string? ProcedurePerformed { get; set; }
    [BindProperty] public string? AnaesthesiaType { get; set; }
    [BindProperty] public string? Reason { get; set; }
    [BindProperty] public long ProductId { get; set; }
    [BindProperty] public int Qty { get; set; } = 1;

    public OtCase? Case { get; private set; }
    public string PatientName { get; private set; } = "—";
    public string Uhid { get; private set; } = "—";
    public string TheatreName { get; private set; } = "—";
    public IReadOnlyList<CaseTeamMember> Team { get; private set; } = [];
    public IReadOnlyList<CaseConsumable> Consumables { get; private set; } = [];
    public IReadOnlyList<(long Id, string Name)> Products { get; private set; } = [];
    public long BilledSoFar { get; private set; }

    public async Task<IActionResult> OnGetAsync()
        => await LoadAsync() ? Page() : NotFound();

    private async Task<bool> LoadAsync() => await tx.RunAsync(async s =>
    {
        Case = await s.Ot.Cases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == Id);
        if (Case is null) return false;

        var patient = await s.Reg.Patients.AsNoTracking().FirstAsync(p => p.Id == Case.PatientId);
        PatientName = patient.FullName;
        Uhid = patient.Uhid;
        TheatreName = await s.Ot.Theatres.AsNoTracking()
            .Where(t => t.Id == Case.TheatreId).Select(t => t.Name).FirstAsync();
        Team = await s.Ot.Team.AsNoTracking().Where(t => t.CaseId == Id).ToListAsync();
        Consumables = await s.Ot.Consumables.AsNoTracking()
            .Where(c => c.CaseId == Id).OrderBy(c => c.Id).ToListAsync();
        Products = (await s.Pharm.Products.AsNoTracking()
                .Where(p => p.Active).OrderBy(p => p.Brand).Take(200).ToListAsync())
            .Select(p => (p.Id, $"{p.Brand} {p.Strength} {p.Form}")).ToList();

        BilledSoFar = Team.Sum(t => t.AmountPosted)
                      + Consumables.Sum(c => c.UnitPrice * c.Qty);
        return true;
    });

    public async Task<IActionResult> OnPostReadyAsync() =>
        await Run(async s => await ot.MarkPatientReadyAsync(s.Ot, Id), "Patient sent for", "schedule");

    public async Task<IActionResult> OnPostStartAsync() =>
        await Run(async s => await ot.StartAsync(s.Ot, Id), "Case started", "schedule");

    public async Task<IActionResult> OnPostCompleteAsync()
    {
        try
        {
            var summary = await tx.RunAsync(async s =>
            {
                await ot.CompleteAsync(s.Ot, s.Kernel, Id, BranchId, Findings, ProcedurePerformed,
                    AnaesthesiaType, ActorId, ActorName);
                // Re-read: the state guard above ran in SQL, so the row we post against must be
                // the row the database now holds (spec 0024's lesson, applied on the way in).
                var otCase = await OtService.GetAsync(s.Ot, Id);
                return await OtBilling.PostCompletionChargesAsync(s, billing, folios, rates, clock,
                    BranchId, otCase, ActorId);
            });

            var total = summary.Sum(x => x.Amount);
            Toast($"Case completed — {Ui.Money(total)} posted to the bill", "task_alt");
            return Redirect($"/ot/case/{Id}");
        }
        catch (Exception e) when (e is OtException or BillingException or IpdException)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCancelAsync() =>
        await Run(async s => await ot.CancelAsync(s.Ot, s.Kernel, Id, BranchId, Reason ?? "",
            ActorId, ActorName), "Case cancelled", "close");

    public async Task<IActionResult> OnPostPostponeAsync() =>
        await Run(async s => await ot.PostponeAsync(s.Ot, s.Kernel, Id, BranchId, Reason ?? "",
            ActorId, ActorName), "Case postponed", "schedule");

    public async Task<IActionResult> OnPostConsumableAsync()
    {
        try
        {
            var posted = await tx.RunAsync(async s =>
            {
                var otCase = await OtService.GetAsync(s.Ot, Id);
                var outletId = await s.Pharm.Outlets.AsNoTracking()
                    .Where(o => o.Kind == "main").Select(o => o.Id).FirstAsync();
                return await OtBilling.IssueConsumableAsync(s, billing, folios, stock, clock,
                    BranchId, otCase, outletId, ProductId, Qty, ActorId);
            });
            Toast($"{Ui.Money(posted.Sum(p => p.Amount))} of consumables issued", "outbox");
            return Redirect($"/ot/case/{Id}");
        }
        catch (Exception e) when (e is OtBillingException or PharmacyException or IpdException)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    private async Task<IActionResult> Run(Func<TxScope, Task> body, string toast, string icon)
    {
        try
        {
            await tx.RunAsync(body);
            Toast(toast, icon);
            return Redirect($"/ot/case/{Id}");
        }
        catch (OtException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }
}
