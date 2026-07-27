using Hms.Emr;
using Hms.Emr.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Emr;

/// <summary>
/// §5 M5 [M]: the prescription as the patient receives it, on the doctor's own layout. Also the
/// only place a signed note can be corrected — the correction is a new note (§10 immutability),
/// so the printed sheet in the patient's hand always matches a note that still exists.
/// </summary>
[Authorize(Policy = Perm.EmrRead)]
public class PrescriptionModel(HmsTx tx, EmrService emr) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long Id { get; set; }

    public string PatientName { get; private set; } = "—";
    public string Uhid { get; private set; } = "—";
    public string AgeSex { get; private set; } = "—";
    public string DoctorName { get; private set; } = "—";
    public string? DoctorRegistration { get; private set; }
    public DateTimeOffset At { get; private set; }
    public string State { get; private set; } = NoteState.Draft;
    public long? SupersedesId { get; private set; }
    public long? SupersededById { get; private set; }
    public string? Complaint { get; private set; }
    public string? OnExamination { get; private set; }
    public string? Diagnosis { get; private set; }
    public string? Advice { get; private set; }
    public DateOnly? FollowUpOn { get; private set; }
    public string? VitalsSummary { get; private set; }
    public IReadOnlyList<DrugRow> Drugs { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
        => await LoadAsync() ? Page() : NotFound();

    private async Task<bool> LoadAsync() => await tx.RunAsync(async s =>
    {
        var note = await s.Emr.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == Id);
        if (note is null) return false;

        var patient = await s.Reg.Patients.AsNoTracking().FirstAsync(p => p.Id == note.PatientId);
        var today = DateOnly.FromDateTime(note.CreatedAt.UtcDateTime);
        PatientName = patient.FullName;
        Uhid = patient.Uhid;
        AgeSex = $"{Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today)} · " +
                 Ui.SexWord(patient.Sex);

        var doctor = await s.Auth.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == note.DoctorId);
        DoctorName = doctor?.DisplayName ?? "—";
        // The consultant register line, when the hospital keeps one for this doctor. Absent is
        // rendered as absent — a printed registration number nobody verified is worse than none.
        DoctorRegistration = await s.Adm.ReportingConsultants.AsNoTracking()
            .Where(c => c.Name == DoctorName).Select(c => c.Degrees + " · BMDC " + c.BmdcNo)
            .FirstOrDefaultAsync();

        At = note.FinalisedAt ?? note.CreatedAt;
        State = note.State;
        SupersedesId = note.SupersedesId;
        SupersededById = await s.Emr.Notes.AsNoTracking()
            .Where(n => n.SupersedesId == note.Id).Select(n => (long?)n.Id).FirstOrDefaultAsync();
        Complaint = note.Complaint;
        OnExamination = note.OnExamination;
        Diagnosis = note.Diagnosis;
        Advice = note.Advice;
        FollowUpOn = note.FollowUpOn;

        VitalsSummary = QueueModel.VitalsSummary(await s.Emr.Vitals.AsNoTracking()
            .Where(v => v.EncounterId == note.EncounterId && note.EncounterId != null)
            .OrderByDescending(v => v.Id).FirstOrDefaultAsync());

        Drugs = (await s.Emr.NoteDrugs.AsNoTracking()
                .Where(d => d.NoteId == note.Id).OrderBy(d => d.Ordinal).ToListAsync())
            .Select(d => new DrugRow(d.ProductId, d.DrugName, d.Dose, d.Frequency, d.Duration,
                d.Instruction)).ToList();
        return true;
    });

    /// <summary>
    /// Correction = a new note naming this one (hard rule 4's shape, clinically). The page's
    /// policy is read-level, so the write permission is checked here — Razor Pages does not
    /// allow a per-handler [Authorize], and a reader must not be able to POST this.
    /// </summary>
    public async Task<IActionResult> OnPostCorrectAsync()
    {
        if (!Can("emr.note.write"))
        {
            await LoadAsync();
            Fail("Only a prescriber can write a correction.");
            return Page();
        }
        try
        {
            var replacement = await tx.RunAsync(async s =>
                await emr.SupersedeAsync(s.Emr, s.Kernel, Id, BranchId, ActorId, ActorName));
            Toast("Correcting prescription opened — the original stays on record", "edit_note");
            return replacement.EncounterId is { } encounterId
                ? Redirect($"/emr/consult/{encounterId}")
                : Redirect($"/emr/prescription/{replacement.Id}");
        }
        catch (EmrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }
}
