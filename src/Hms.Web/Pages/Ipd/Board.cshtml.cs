using Hms.Ipd;
using Hms.Ipd.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ipd;

public sealed record BedTile(
    long Id, string Code, string State, string? StateReason,
    long? AdmissionId, string? PatientName, string? AdmissionNo);

public sealed record WardGroup(long Id, string Name, string Class, IReadOnlyList<BedTile> Beds);

public sealed record OccupancyRow(string Class, int Total, int Occupied, int Reserved, int Free);

/// <summary>
/// US2.1/US6.4: the live bed board — one screen answers "is a cabin free?" and "what is
/// tonight's occupancy?". Color is state; occupied tiles lead to the folio.
/// </summary>
[Authorize(Policy = Perm.IpdRead)]
public class BoardModel(HmsTx tx, IpdService ipd) : HmsPageModel
{
    [BindProperty] public long BedId { get; set; }
    [BindProperty] public string? Reason { get; set; }

    public IReadOnlyList<WardGroup> Wards { get; private set; } = [];
    public IReadOnlyList<OccupancyRow> Occupancy { get; private set; } = [];
    public bool CanManage => Can("ipd.manage");

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            var wards = await s.Ipd.Wards.AsNoTracking().Where(w => w.Active)
                .OrderBy(w => w.Id).ToListAsync();
            var beds = await s.Ipd.Beds.AsNoTracking().OrderBy(b => b.Code).ToListAsync();

            // Which admission holds each bed: the open stay of an in-house admission.
            var openStays = await s.Ipd.BedStays.AsNoTracking().Where(x => x.ToAt == null).ToListAsync();
            var admissionIds = openStays.Select(x => x.AdmissionId).ToList();
            var admissions = await s.Ipd.Admissions.AsNoTracking()
                .Where(a => admissionIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
            var patientIds = admissions.Values.Select(a => a.PatientId).Distinct().ToList();
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.FullName);

            var stayByBed = openStays.ToDictionary(x => x.BedId);

            Wards = wards.Select(w => new WardGroup(w.Id, w.Name, w.Class,
                beds.Where(b => b.WardId == w.Id).Select(b =>
                {
                    stayByBed.TryGetValue(b.Id, out var stay);
                    var admission = stay is not null ? admissions.GetValueOrDefault(stay.AdmissionId) : null;
                    return new BedTile(b.Id, b.Code, b.State, b.StateReason,
                        admission?.Id,
                        admission is not null ? patients.GetValueOrDefault(admission.PatientId) : null,
                        admission?.AdmissionNo);
                }).ToList())).ToList();

            Occupancy = wards.GroupBy(w => w.Class).Select(g =>
            {
                var classBeds = beds.Where(b => g.Select(w => w.Id).Contains(b.WardId)).ToList();
                return new OccupancyRow(g.Key, classBeds.Count,
                    classBeds.Count(b => b.State == BedState.Occupied),
                    classBeds.Count(b => b.State == BedState.Reserved),
                    classBeds.Count(b => b.State == BedState.Free));
            }).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostCleaningDoneAsync()
    {
        if (!CanManage) return Forbid();
        try { await tx.RunAsync(s => ipd.CleaningDoneAsync(s.Ipd, BedId)); }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Bed is free again", "task_alt");
        return Redirect("/ipd/board");
    }

    public async Task<IActionResult> OnPostOutOfServiceAsync()
    {
        if (!CanManage) return Forbid();
        if (string.IsNullOrWhiteSpace(Reason))
        { await LoadAsync(); Fail("Out-of-service needs a reason."); return Page(); }
        try { await tx.RunAsync(s => ipd.SetOutOfServiceAsync(s.Ipd, BedId, Reason.Trim())); }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Bed marked out of service", "warning");
        return Redirect("/ipd/board");
    }

    public async Task<IActionResult> OnPostReturnToServiceAsync()
    {
        if (!CanManage) return Forbid();
        try { await tx.RunAsync(s => ipd.ReturnToServiceAsync(s.Ipd, BedId)); }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        Toast("Bed returned to service", "task_alt");
        return Redirect("/ipd/board");
    }
}
