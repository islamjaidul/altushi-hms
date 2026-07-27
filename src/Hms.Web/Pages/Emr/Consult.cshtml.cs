using Hms.Admin;
using Hms.Billing;
using Hms.Emr;
using Hms.Emr.Data;
using Hms.Kernel.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Emr;

public sealed record DrugRow(long? ProductId, string Name, string? Dose, string? Frequency,
    string? Duration, string? Instruction);
public sealed record HistoryLine(DateTimeOffset At, string Doctor, string? Diagnosis, long NoteId);
public sealed record ResultLine(string Test, string Value, bool Abnormal, DateTimeOffset At);

/// <summary>
/// §5 M5's core screen and US5.1's three-minute target. Everything the consultant needs is on
/// one page — vitals the nurse took, the last visit, recent verified results, the drug picker,
/// and the test order — because a doctor who has to navigate is a doctor who reaches for the pad.
/// </summary>
[Authorize(Policy = Perm.EmrNoteWrite)]
public class ConsultModel(
    HmsTx tx, EmrService emr, RateResolver rates, BillingService billing, TimeProvider clock)
    : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long EncounterId { get; set; }

    [BindProperty] public string? Complaint { get; set; }
    [BindProperty] public string? OnExamination { get; set; }
    [BindProperty] public string? Diagnosis { get; set; }
    [BindProperty] public string? Advice { get; set; }
    [BindProperty] public string? FollowUp { get; set; }

    [BindProperty] public List<long?> DrugProductId { get; set; } = [];
    [BindProperty] public List<string?> DrugName { get; set; } = [];
    [BindProperty] public List<string?> DrugDose { get; set; } = [];
    [BindProperty] public List<string?> DrugFrequency { get; set; } = [];
    [BindProperty] public List<string?> DrugDuration { get; set; } = [];
    [BindProperty] public List<string?> DrugInstruction { get; set; } = [];

    [BindProperty] public List<long> Tests { get; set; } = [];
    [BindProperty] public long TemplateId { get; set; }
    [BindProperty] public string? TemplateName { get; set; }

    public long NoteId { get; private set; }
    public string NoteState { get; private set; } = Hms.Emr.Data.NoteState.Draft;
    public long PatientId { get; private set; }
    public string PatientName { get; private set; } = "—";
    public string Uhid { get; private set; } = "—";
    public string? AgeSex { get; private set; }
    public string? VitalsSummary { get; private set; }
    public IReadOnlyList<DrugRow> Drugs { get; private set; } = [];
    public IReadOnlyList<(long Id, string Name)> TestCatalog { get; private set; } = [];
    public IReadOnlyList<EmrOrdering.PendingOrder> Orders { get; private set; } = [];
    public IReadOnlyList<Favourite> Favourites { get; private set; } = [];
    public IReadOnlyList<NoteTemplate> Templates { get; private set; } = [];
    public IReadOnlyList<HistoryLine> PastVisits { get; private set; } = [];
    public IReadOnlyList<ResultLine> RecentResults { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return NotFound();
        return Page();
    }

    /// <summary>Opens (or resumes) the draft and loads everything around it.</summary>
    private async Task<bool> LoadAsync()
    {
        var found = await tx.RunAsync(async s =>
        {
            var encounter = await s.Bill.Encounters.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == EncounterId);
            if (encounter is null) return false;

            PatientId = encounter.PatientId;
            var patient = await s.Reg.Patients.AsNoTracking().FirstAsync(p => p.Id == PatientId);
            var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
            PatientName = patient.FullName;
            Uhid = patient.Uhid;
            AgeSex = $"{Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today)} · " +
                     Ui.SexShort(patient.Sex);

            // Reopening a visit must show what is already there. Opening a draft unconditionally
            // would start a *second* prescription for the same visit every time the doctor came
            // back to the screen — found by the end-to-end thread, spec 0024.
            var note = await s.Emr.Notes.AsNoTracking()
                .Where(n => n.EncounterId == EncounterId
                            && n.State != Hms.Emr.Data.NoteState.Superseded)
                .OrderByDescending(n => n.Id).FirstOrDefaultAsync()
                ?? await emr.OpenDraftAsync(s.Emr, BranchId, PatientId, EncounterId, null, ActorId);
            NoteId = note.Id;
            NoteState = note.State;
            Complaint ??= note.Complaint;
            OnExamination ??= note.OnExamination;
            Diagnosis ??= note.Diagnosis;
            Advice ??= note.Advice;
            FollowUp ??= note.FollowUpOn is { } f ? Ui.DateLong(f) : null;

            Drugs = (await s.Emr.NoteDrugs.AsNoTracking()
                    .Where(d => d.NoteId == note.Id).OrderBy(d => d.Ordinal).ToListAsync())
                .Select(d => new DrugRow(d.ProductId, d.DrugName, d.Dose, d.Frequency,
                    d.Duration, d.Instruction)).ToList();

            VitalsSummary = QueueModel.VitalsSummary(await s.Emr.Vitals.AsNoTracking()
                .Where(v => v.EncounterId == EncounterId)
                .OrderByDescending(v => v.Id).FirstOrDefaultAsync());

            TestCatalog = (await s.Adm.TestCatalog.AsNoTracking()
                    .Where(t => t.Active).OrderBy(t => t.Name).Take(200).ToListAsync())
                .Select(t => (t.Id, t.Name)).ToList();
            Orders = await EmrOrdering.PendingForEncounterAsync(s, EncounterId);
            Favourites = await s.Emr.Favourites.AsNoTracking()
                .Where(f => f.DoctorId == ActorId).OrderBy(f => f.DrugName).ToListAsync();
            Templates = await s.Emr.Templates.AsNoTracking()
                .Where(t => t.DoctorId == ActorId && t.Active).OrderBy(t => t.Name).ToListAsync();

            // The longitudinal context US5.2 asks for: what was said last time, and what the lab
            // has verified since. Both are cross-schema reads, joined here in memory (ADR-0003).
            var past = await s.Emr.Notes.AsNoTracking()
                .Where(n => n.PatientId == PatientId && n.Id != note.Id
                            && n.State == Hms.Emr.Data.NoteState.Final)
                .OrderByDescending(n => n.Id).Take(5).ToListAsync();
            var doctorIds = past.Select(p => p.DoctorId).Distinct().ToList();
            var doctors = await s.Auth.Users.AsNoTracking()
                .Where(u => doctorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);
            PastVisits = past.Select(p => new HistoryLine(
                p.FinalisedAt ?? p.CreatedAt, doctors.GetValueOrDefault(p.DoctorId, "—"),
                p.Diagnosis, p.Id)).ToList();

            RecentResults = await EmrRecord.RecentResultsAsync(s, PatientId, 8);
            return true;
        });
        return found;
    }

    // ---- handlers ----------------------------------------------------------

    public async Task<IActionResult> OnPostSaveAsync() => await SaveThen(finalise: false);

    public async Task<IActionResult> OnPostFinaliseAsync() => await SaveThen(finalise: true);

    private async Task<IActionResult> SaveThen(bool finalise)
    {
        try
        {
            var noteId = await tx.RunAsync(async s =>
            {
                var note = await CurrentDraftAsync(s);
                await emr.SaveDraftAsync(s.Emr, note.Id, Body(), CollectDrugs());

                if (Tests.Count > 0)
                    await EmrOrdering.OrderTestsAsync(s, rates, billing, clock, BranchId,
                        note, Tests, ActorId);

                if (finalise)
                    await emr.FinaliseAsync(s.Emr, s.Kernel, note.Id, BranchId, ActorId, ActorName);
                return note.Id;
            });

            if (finalise)
            {
                Toast("Prescription signed", "verified");
                return Redirect($"/emr/prescription/{noteId}");
            }
            Toast("Saved — not yet signed", "save");
            return Redirect($"/emr/consult/{EncounterId}");
        }
        catch (EmrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    /// <summary>US5.1 AC: one action fills every section, and every field stays editable.</summary>
    public async Task<IActionResult> OnPostApplyTemplateAsync()
    {
        await tx.RunAsync(async s =>
        {
            var template = await s.Emr.Templates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == TemplateId && t.DoctorId == ActorId);
            if (template is null) return 0;

            var note = await CurrentDraftAsync(s);
            await emr.SaveDraftAsync(s.Emr, note.Id,
                new NoteBody(template.Complaint, template.OnExamination, template.Diagnosis,
                    template.Advice, null),
                EmrService.TemplateDrugs(template));
            return 0;
        });
        Toast("Template applied — edit anything that does not fit", "edit_note");
        return Redirect($"/emr/consult/{EncounterId}");
    }

    /// <summary>Saves what is on screen as a reusable template (the other half of US5.1).</summary>
    public async Task<IActionResult> OnPostSaveTemplateAsync()
    {
        try
        {
            await tx.RunAsync(async s =>
                await emr.SaveTemplateAsync(s.Emr, ActorId, TemplateName ?? "", Body(), CollectDrugs()));
            Toast("Saved to your templates", "edit_note");
        }
        catch (EmrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
        return Redirect($"/emr/consult/{EncounterId}");
    }

    /// <summary>
    /// The draft this screen writes into. A visit whose latest note is already signed has no
    /// draft to write into, and must not silently grow a second prescription — the correction
    /// path (supersede) is the only way forward, and it is offered on the printed sheet.
    /// </summary>
    private async Task<Hms.Emr.Data.Note> CurrentDraftAsync(TxScope s)
    {
        var latest = await s.Emr.Notes.AsNoTracking()
            .Where(n => n.EncounterId == EncounterId && n.State != Hms.Emr.Data.NoteState.Superseded)
            .OrderByDescending(n => n.Id).FirstOrDefaultAsync();
        if (latest is { State: Hms.Emr.Data.NoteState.Final })
            throw new EmrException(
                "This prescription is already signed. Open it and write a correction — " +
                "the original stays on the record.");

        var patientId = await s.Bill.Encounters.AsNoTracking()
            .Where(e => e.Id == EncounterId).Select(e => e.PatientId).FirstAsync();
        return await emr.OpenDraftAsync(s.Emr, BranchId, patientId, EncounterId, null, ActorId);
    }

    private NoteBody Body()
    {
        DateOnly? followUp = null;
        if (!string.IsNullOrWhiteSpace(FollowUp) && FlexibleDate.TryParse(FollowUp, out var parsed))
            followUp = parsed;
        return new NoteBody(Complaint, OnExamination, Diagnosis, Advice, followUp);
    }

    private List<DrugLine> CollectDrugs()
    {
        var lines = new List<DrugLine>();
        for (var i = 0; i < DrugName.Count; i++)
        {
            var name = DrugName[i];
            if (string.IsNullOrWhiteSpace(name)) continue;
            lines.Add(new DrugLine(
                At(DrugProductId, i), name!, At(DrugDose, i), At(DrugFrequency, i),
                At(DrugDuration, i), At(DrugInstruction, i)));
        }
        return lines;

        static T? At<T>(List<T?> list, int i) => i < list.Count ? list[i] : default;
    }
}
