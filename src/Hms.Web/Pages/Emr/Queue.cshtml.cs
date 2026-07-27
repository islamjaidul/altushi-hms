using Hms.Emr.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Emr;

public sealed record ConsultRow(
    long EncounterId, long PatientId, string Uhid, string Patient, string? AgeSex,
    string Type, bool HasVitals, string? VitalsSummary, long? NoteId, string? NoteState);

/// <summary>
/// §5 M5 [M]: the doctor's worklist. A visit appears here once the counter has opened it —
/// which is also when the patient has paid — so the chamber list is the paid list, and the
/// doctor never has to ask.
/// </summary>
[Authorize(Policy = Perm.EmrRead)]
public class QueueModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    public IReadOnlyList<ConsultRow> Rows { get; private set; } = [];
    public DateOnly Today { get; private set; }

    public async Task OnGetAsync()
    {
        Today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var today = Today;

        await tx.RunAsync(async s =>
        {
            var encounters = await s.Bill.Encounters.AsNoTracking()
                .Where(e => e.OnDate == today && e.Type != PharmacySale.EncounterType)
                .OrderByDescending(e => e.Id).Take(120).ToListAsync();
            if (encounters.Count == 0) return 0;

            var encounterIds = encounters.Select(e => e.Id).ToList();
            var patientIds = encounters.Select(e => e.PatientId).Distinct().ToList();

            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
            // Latest reading per visit: the doctor wants the current numbers, and the history
            // screen keeps the rest.
            var vitals = await s.Emr.Vitals.AsNoTracking()
                .Where(v => v.EncounterId != null && encounterIds.Contains(v.EncounterId!.Value))
                .OrderByDescending(v => v.Id).ToListAsync();
            var notes = await s.Emr.Notes.AsNoTracking()
                .Where(n => n.EncounterId != null && encounterIds.Contains(n.EncounterId!.Value)
                            && n.State != NoteState.Superseded)
                .OrderByDescending(n => n.Id).ToListAsync();

            Rows = encounters.Select(e =>
            {
                var patient = patients.GetValueOrDefault(e.PatientId);
                var v = vitals.FirstOrDefault(x => x.EncounterId == e.Id);
                var n = notes.FirstOrDefault(x => x.EncounterId == e.Id);
                return new ConsultRow(
                    e.Id, e.PatientId, patient?.Uhid ?? "—", patient?.FullName ?? "—",
                    patient is null
                        ? null
                        : $"{Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today)} · " +
                          Ui.SexShort(patient.Sex),
                    e.Type, v is not null, VitalsSummary(v), n?.Id, n?.State);
            }).ToList();
            return 0;
        });
    }

    /// <summary>One line the doctor can read at a glance — U12: numbers with their units.</summary>
    public static string? VitalsSummary(Vitals? v)
    {
        if (v is null) return null;
        var parts = new List<string>();
        if (v.Systolic is not null && v.Diastolic is not null) parts.Add($"BP {v.Systolic}/{v.Diastolic}");
        if (v.Pulse is not null) parts.Add($"pulse {v.Pulse}");
        if (v.TemperatureTenthsC is not null) parts.Add($"temp {v.TemperatureTenthsC / 10m:0.0}°C");
        if (v.SpO2 is not null) parts.Add($"SpO₂ {v.SpO2}%");
        if (v.WeightTenthsKg is not null) parts.Add($"{v.WeightTenthsKg / 10m:0.0} kg");
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
