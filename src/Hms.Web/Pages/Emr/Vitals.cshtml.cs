using System.ComponentModel.DataAnnotations;
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
    // AUD-VAL-20: physiological bounds — a blank field is "not taken" ([Range] skips null), but
    // a filled one must be a reading a human can have. Temperature and weight are stored as
    // tenths (short), so the °C/kg bounds here also keep the stored tenths in range.
    [BindProperty, Range(40, 300, ErrorMessage = "Systolic (BP) must be between 40 and 300")]
    public short? Systolic { get; set; }
    [BindProperty, Range(20, 200, ErrorMessage = "Diastolic (BP) must be between 20 and 200")]
    public short? Diastolic { get; set; }
    [BindProperty, Range(20, 300, ErrorMessage = "Pulse must be between 20 and 300")]
    public short? Pulse { get; set; }
    [BindProperty, Range(25.0, 46.0, ErrorMessage = "Temperature is in °C — between 25.0 and 46.0")]
    public decimal? Temperature { get; set; }
    [BindProperty, Range(0.0, 500.0, ErrorMessage = "Weight is in kg — between 0 and 500")]
    public decimal? Weight { get; set; }
    [BindProperty, Range(0, 100, ErrorMessage = "SpO2 is a percentage — between 0 and 100")]
    public short? SpO2 { get; set; }

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
            var known = await tx.RunAsync(async s =>
            {
                // AUD-VAL-20g: an encounter id nothing points at is a stale screen, not a crash.
                var patientId = await s.Bill.Encounters.AsNoTracking()
                    .Where(e => e.Id == EncounterId).Select(e => (long?)e.PatientId).FirstOrDefaultAsync();
                if (patientId is null) return false;
                // Tenths, not floats: 37.2 °C has to come back as 37.2 (see EmrDbContext).
                await emr.RecordVitalsAsync(s.Emr, BranchId, patientId.Value, EncounterId, null,
                    Systolic, Diastolic, Pulse, Tenths(Temperature), Tenths(Weight), SpO2, ActorId);
                return true;
            });
            if (!known)
            {
                await LoadAsync();
                Fail("That visit is not on today's list any more — reload the screen and pick the row again.");
                return Page();
            }
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
