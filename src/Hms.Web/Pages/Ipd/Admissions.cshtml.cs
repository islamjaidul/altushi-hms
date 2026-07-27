using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ipd;

public sealed record AdmissionRow(
    long Id, string AdmissionNo, string Patient, string? Doctor, string Bed, string State,
    DateTimeOffset AdmittedAt, long Balance, long? PendingBlockApproval, long? ApprovedBlockApproval,
    long? ApprovedReleaseApproval);

/// <summary>
/// Census + the R4 block list. Blocking and releasing are both approval-gated (§11 ⚿):
/// the operator raises the request here; once it lands in the approvals inbox and is granted,
/// the Apply button executes it. Balance = unbilled folio charges − advances (live view).
/// </summary>
[Authorize(Policy = Perm.IpdRead)]
public class AdmissionsModel(HmsTx tx, IpdService ipd, ApprovalEngine approvals) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "inhouse";
    [BindProperty] public long AdmissionId { get; set; }
    [BindProperty] public string? Reason { get; set; }
    [BindProperty] public long ApprovalId { get; set; }

    public IReadOnlyList<AdmissionRow> Rows { get; private set; } = [];
    public bool CanManage => Can("ipd.manage");

    private static readonly string[] InHouse =
        [AdmissionState.Admitted, AdmissionState.Blocked, AdmissionState.DischargeInitiated,
         AdmissionState.ClinicallyCleared, AdmissionState.FinanciallySettled];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            var query = s.Ipd.Admissions.AsNoTracking();
            query = Tab switch
            {
                "blocked" => query.Where(a => a.State == AdmissionState.Blocked),
                "reserved" => query.Where(a => a.State == AdmissionState.Reserved),
                "closed" => query.Where(a => a.State == AdmissionState.Discharged
                    || a.State == AdmissionState.Death || a.State == AdmissionState.Absconded),
                _ => query.Where(a => InHouse.Contains(a.State)),
            };
            var admissions = await query.OrderByDescending(a => a.Id).Take(100).ToListAsync();
            var ids = admissions.Select(a => a.Id).ToList();

            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => admissions.Select(a => a.PatientId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.FullName);
            var doctors = (await s.Appt.Schedules.AsNoTracking().ToListAsync())
                .GroupBy(x => x.DoctorId).ToDictionary(g => g.Key, g => g.First().DoctorName);
            var stays = await s.Ipd.BedStays.AsNoTracking()
                .Where(x => ids.Contains(x.AdmissionId) && x.ToAt == null).ToListAsync();
            var bedIds = stays.Select(x => x.BedId).ToList();
            var beds = await s.Ipd.Beds.AsNoTracking()
                .Where(b => bedIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Code);
            var folios = await s.Ipd.Folios.AsNoTracking()
                .Where(f => ids.Contains(f.AdmissionId)).ToListAsync();
            var folioIds = folios.Select(f => f.Id).ToList();

            // Live balance: unbilled folio charges − net advances (cross-context, in-memory join).
            var charges = await s.Bill.ChargeLines.AsNoTracking()
                .Where(c => c.FolioId != null && folioIds.Contains(c.FolioId.Value) && c.InvoiceId == null)
                .GroupBy(c => c.FolioId!.Value)
                .Select(g => new { FolioId = g.Key, Total = g.Sum(c => c.Amount) })
                .ToDictionaryAsync(x => x.FolioId, x => x.Total);
            var advances = await s.Bill.Receipts.AsNoTracking()
                .Where(r => r.FolioId != null && folioIds.Contains(r.FolioId.Value))
                .GroupBy(r => r.FolioId!.Value)
                .Select(g => new { FolioId = g.Key, Total = g.Sum(r => r.Amount) })
                .ToDictionaryAsync(x => x.FolioId, x => x.Total);
            // Settled folios: the remaining due lives on the settlement invoice.
            var settledInvoiceIds = folios.Where(f => f.SettlementInvoiceId != null)
                .ToDictionary(f => f.AdmissionId, f => f.SettlementInvoiceId!.Value);
            var dues = await s.Bill.Dues.AsNoTracking()
                .Where(d => settledInvoiceIds.Values.Contains(d.InvoiceId))
                .ToDictionaryAsync(d => d.InvoiceId, d => d.Balance);

            var blockRequests = await s.Kernel.ApprovalRequests.AsNoTracking()
                .Where(r => (r.Type == "patient-block" || r.Type == "patient-release")
                            && r.SourceTable == "ipd.admission" && ids.Contains(r.SourceId))
                .OrderByDescending(r => r.Id).ToListAsync();

            Rows = admissions.Select(a =>
            {
                var folio = folios.FirstOrDefault(f => f.AdmissionId == a.Id);
                long balance = 0;
                if (folio is not null)
                {
                    balance = charges.GetValueOrDefault(folio.Id) - advances.GetValueOrDefault(folio.Id);
                    if (settledInvoiceIds.TryGetValue(a.Id, out var inv))
                        balance += dues.GetValueOrDefault(inv);
                }
                var stay = stays.FirstOrDefault(x => x.AdmissionId == a.Id);
                long? pendingBlock = blockRequests.FirstOrDefault(r => r.SourceId == a.Id
                    && r.Type == "patient-block" && r.State == ApprovalState.Pending)?.Id;
                long? approvedBlock = blockRequests.FirstOrDefault(r => r.SourceId == a.Id
                    && r.Type == "patient-block" && r.State == ApprovalState.Approved
                    && a.BlockApprovalId != r.Id)?.Id;
                long? approvedRelease = blockRequests.FirstOrDefault(r => r.SourceId == a.Id
                    && r.Type == "patient-release" && r.State == ApprovalState.Approved)?.Id;

                return new AdmissionRow(a.Id, a.AdmissionNo,
                    patients.GetValueOrDefault(a.PatientId, "—"),
                    a.DoctorId is long d ? doctors.GetValueOrDefault(d) : null,
                    stay is not null ? beds.GetValueOrDefault(stay.BedId, "—") : "—",
                    a.State, a.AdmittedAt, balance, pendingBlock, approvedBlock,
                    a.State == AdmissionState.Blocked ? approvedRelease : null);
            }).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostConfirmReservationAsync()
    {
        if (!CanManage) return Forbid();
        try { await tx.RunAsync(s => ipd.ConfirmReservationAsync(s.Ipd, s.Kernel, AdmissionId, ActorId, ActorName)); }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Reservation confirmed — patient admitted", "person_add");
        return Redirect($"/ipd/folio/{AdmissionId}");
    }

    public async Task<IActionResult> OnPostCancelReservationAsync()
    {
        if (!CanManage) return Forbid();
        try { await tx.RunAsync(s => ipd.CancelReservationAsync(s.Ipd, s.Kernel, AdmissionId, ActorId, ActorName)); }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Reservation cancelled — bed freed", "task_alt");
        return Redirect("/ipd/admissions?Tab=reserved");
    }

    /// <summary>Raise the R4 block request (⚿). Applying happens once approved.</summary>
    public async Task<IActionResult> OnPostRaiseBlockAsync()
    {
        if (!CanManage) return Forbid();
        if (string.IsNullOrWhiteSpace(Reason))
        { await LoadAsync(); Fail("A block needs a reason — it bars all service."); return Page(); }
        var raise = await tx.RunAsync(s => approvals.RaiseAsync(
            s.Kernel, BranchId, "patient-block", "ipd.admission", AdmissionId,
            ActorId, ActorRole, Reason.Trim(), null));
        Toast(raise.AutoApproved ? "Block approved — press Apply" : "Block request sent for approval",
            "fact_check");
        return Redirect("/ipd/admissions");
    }

    public async Task<IActionResult> OnPostApplyBlockAsync()
    {
        if (!CanManage) return Forbid();
        try { await tx.RunAsync(s => ipd.BlockAsync(s.Ipd, s.Kernel, AdmissionId, ApprovalId, ActorId, ActorName)); }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Patient blocked for dues (R4)", "block");
        return Redirect("/ipd/admissions?Tab=blocked");
    }

    public async Task<IActionResult> OnPostRaiseReleaseAsync()
    {
        if (!CanManage) return Forbid();
        if (string.IsNullOrWhiteSpace(Reason))
        { await LoadAsync(); Fail("A release needs a reason (paid, waived, or MD instruction)."); return Page(); }
        var raise = await tx.RunAsync(s => approvals.RaiseAsync(
            s.Kernel, BranchId, "patient-release", "ipd.admission", AdmissionId,
            ActorId, ActorRole, Reason.Trim(), null));
        Toast(raise.AutoApproved ? "Release approved — press Apply" : "Release request sent for approval",
            "fact_check");
        return Redirect("/ipd/admissions?Tab=blocked");
    }

    public async Task<IActionResult> OnPostApplyReleaseAsync()
    {
        if (!CanManage) return Forbid();
        try { await tx.RunAsync(s => ipd.ReleaseAsync(s.Ipd, s.Kernel, AdmissionId, ApprovalId, ActorId, ActorName)); }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Patient released — services resume", "task_alt");
        return Redirect("/ipd/admissions");
    }
}
