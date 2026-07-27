using Hms.Emr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Emr;

public sealed record VitalsRow(
    long EncounterId, string Uhid, string Patient, string? Summary, DateTimeOffset? At);

/// <summary>
/// US5.3: the nurse's pre-checkup screen. One row per visit opened today; entering a reading
/// takes the nurse two fields and a button, because chamber time is the thing being saved.
/// </summary>
[Authorize(Policy = Perm.EmrVitalsRecord)]
public class VitalsModel(HmsTx tx, EmrService emr, TimeProvider clock) : HmsPageModel
{
    [BindProperty] public long EncounterId { get; set; }
    [BindProperty] public short? Systolic { get; set; }
    [BindProperty] public short? Diastolic { get; set; }
    [BindProperty] public short? Pulse { get; set; }
    [BindProperty] public decimal? Temperature { get; set; }
    [BindProperty] public decimal? Weight { get; set; }
    [BindProperty] public short? SpO2 { get; set; }

    public IReadOnlyList<VitalsRow> Rows { get; private set; } = [];
    public DateOnly Today { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var today = Today;
        await tx.RunAsync(async s =>
        {
            var encounters = await s.Bill.Encounters.AsNoTracking()
                .Where(e => e.OnDate == today && e.Type != PharmacySale.EncounterType)
                .OrderByDescending(e => e.Id).Take(120).ToListAsync();
            var ids = encounters.Select(e => e.Id).ToList();
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => encounters.Select(e => e.PatientId).Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);
            var vitals = await s.Emr.Vitals.AsNoTracking()
                .Where(v => v.EncounterId != null && ids.Contains(v.EncounterId!.Value))
                .OrderByDescending(v => v.Id).ToListAsync();

            Rows = encounters.Select(e =>
            {
                var patient = patients.GetValueOrDefault(e.PatientId);
                var v = vitals.FirstOrDefault(x => x.EncounterId == e.Id);
                return new VitalsRow(e.Id, patient?.Uhid ?? "—", patient?.FullName ?? "—",
                    QueueModel.VitalsSummary(v), v?.RecordedAt);
            }).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            await tx.RunAsync(async s =>
            {
                var patientId = await s.Bill.Encounters.AsNoTracking()
                    .Where(e => e.Id == EncounterId).Select(e => e.PatientId).FirstAsync();
                // Tenths, not floats: 37.2 °C has to come back as 37.2 (see EmrDbContext).
                return await emr.RecordVitalsAsync(s.Emr, BranchId, patientId, EncounterId, null,
                    Systolic, Diastolic, Pulse, Tenths(Temperature), Tenths(Weight), SpO2, ActorId);
            });
            Toast("Vitals recorded", "monitoring");
            return Redirect("/emr/vitals");
        }
        catch (EmrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    private static short? Tenths(decimal? value)
        => value is null ? null : (short)Math.Round(value.Value * 10, MidpointRounding.AwayFromZero);
}
