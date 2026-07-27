using System.Text.Json;
using Hms.Emr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Emr;

public sealed class EmrException(string message) : Exception(message);

public sealed record DrugLine(
    long? ProductId, string DrugName, string? Dose, string? Frequency, string? Duration,
    string? Instruction);

public sealed record NoteBody(
    string? Complaint, string? OnExamination, string? Diagnosis, string? Advice,
    DateOnly? FollowUpOn);

/// <summary>
/// §5 M5. The clinical record's own rules and nothing else: what is written, when it becomes
/// immutable, and how a mistake is corrected. Ordering tests spans Diagnostics and Billing and
/// therefore lives at the composition root, not here (ADR-0003).
///
/// Every method expects the caller's ambient transaction (G19).
/// </summary>
public sealed class EmrService(AuditWriter audit, TimeProvider clock)
{
    // ---- vitals -------------------------------------------------------------

    /// <summary>
    /// US5.3: the nurse records vitals before the doctor sees the patient. Repeating a reading
    /// for the same visit is a new row, not an overwrite — a falling blood pressure is a
    /// sequence, and the doctor needs to see the sequence.
    /// </summary>
    public async Task<Vitals> RecordVitalsAsync(
        EmrDbContext emr, long branchId, long patientId, long? encounterId, long? admissionId,
        short? systolic, short? diastolic, short? pulse, short? temperatureTenths,
        short? weightTenths, short? spo2, long actorId, CancellationToken ct = default)
    {
        if ((encounterId is null) == (admissionId is null))
            throw new EmrException("Vitals belong to exactly one visit or one admission.");
        if (systolic is null && diastolic is null && pulse is null && temperatureTenths is null
            && weightTenths is null && spo2 is null)
            throw new EmrException("Nothing was recorded — fill at least one reading.");
        if (systolic is not null && diastolic is not null && systolic <= diastolic)
            throw new EmrException("Systolic must be higher than diastolic — check the two numbers.");

        var vitals = new Vitals
        {
            BranchId = branchId, PatientId = patientId, EncounterId = encounterId,
            AdmissionId = admissionId, Systolic = systolic, Diastolic = diastolic, Pulse = pulse,
            TemperatureTenthsC = temperatureTenths, WeightTenthsKg = weightTenths, SpO2 = spo2,
            RecordedAt = clock.GetUtcNow(), RecordedBy = actorId,
        };
        emr.Vitals.Add(vitals);
        await emr.SaveChangesAsync(ct);
        return vitals;
    }

    // ---- the note -----------------------------------------------------------

    /// <summary>
    /// One draft per doctor per visit: reopening the consultation screen resumes what is already
    /// there rather than starting a second note beside it.
    /// </summary>
    public async Task<Note> OpenDraftAsync(
        EmrDbContext emr, long branchId, long patientId, long? encounterId, long? admissionId,
        long doctorId, CancellationToken ct = default)
    {
        if ((encounterId is null) == (admissionId is null))
            throw new EmrException("A note belongs to exactly one visit or one admission.");

        var existing = await emr.Notes.FirstOrDefaultAsync(n =>
            n.State == NoteState.Draft && n.DoctorId == doctorId
            && n.EncounterId == encounterId && n.AdmissionId == admissionId, ct);
        if (existing is not null) return existing;

        var note = new Note
        {
            BranchId = branchId, PatientId = patientId, EncounterId = encounterId,
            AdmissionId = admissionId, DoctorId = doctorId,
            CreatedAt = clock.GetUtcNow(), CreatedBy = doctorId,
        };
        emr.Notes.Add(note);
        await emr.SaveChangesAsync(ct);
        return note;
    }

    public async Task SaveDraftAsync(
        EmrDbContext emr, long noteId, NoteBody body, IReadOnlyList<DrugLine> drugs,
        CancellationToken ct = default)
    {
        var note = await GetAsync(emr, noteId, ct);
        if (note.State != NoteState.Draft)
            throw new EmrException(
                "This prescription is already signed. Write a correcting prescription instead — " +
                "the original stays on the record.");

        note.Complaint = Trim(body.Complaint);
        note.OnExamination = Trim(body.OnExamination);
        note.Diagnosis = Trim(body.Diagnosis);
        note.Advice = Trim(body.Advice);
        note.FollowUpOn = body.FollowUpOn;

        var existing = await emr.NoteDrugs.Where(d => d.NoteId == noteId).ToListAsync(ct);
        emr.NoteDrugs.RemoveRange(existing);        // a draft's drug list is scratch, not record
        var ordinal = 0;
        foreach (var d in drugs.Where(d => !string.IsNullOrWhiteSpace(d.DrugName)))
            emr.NoteDrugs.Add(new NoteDrug
            {
                NoteId = noteId, ProductId = d.ProductId, DrugName = d.DrugName.Trim(),
                Dose = Trim(d.Dose), Frequency = Trim(d.Frequency), Duration = Trim(d.Duration),
                Instruction = Trim(d.Instruction), Ordinal = ordinal++,
            });
        await emr.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Signing the prescription. State-guarded (ADR-0015): a second finalise from a stale screen
    /// changes no rows and is told so, instead of quietly re-signing.
    /// </summary>
    public async Task FinaliseAsync(
        EmrDbContext emr, KernelDbContext kernel, long noteId, long branchId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var note = await GetAsync(emr, noteId, ct);
        if (string.IsNullOrWhiteSpace(note.Diagnosis) && string.IsNullOrWhiteSpace(note.Complaint))
            throw new EmrException("Write at least a complaint or a diagnosis before signing.");

        var now = clock.GetUtcNow();
        var affected = await emr.Database.ExecuteSqlAsync($"""
            UPDATE emr.note SET state = 'final', finalised_at = {now}, finalised_by = {actorId}
            WHERE id = {noteId} AND state = 'draft'
            """, ct);
        if (affected == 0)
            throw new EmrException("This prescription was already signed — reload the screen.");
        // The guard above is raw SQL, so the context still believes the note is a draft. Left
        // stale, a save later in the same transaction would sail past the immutability check.
        await emr.Entry(note).ReloadAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "emr.note.finalise", "emr.note", noteId,
            after: new { note.PatientId, note.Diagnosis }, tier: 2);
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>
    /// §10: a prescription is immutable once the visit closes, so a correction is a **new** note
    /// that names the one it replaces — the clinical equivalent of a reversal (hard rule 4). The
    /// original keeps its text, its signature and its print.
    /// </summary>
    public async Task<Note> SupersedeAsync(
        EmrDbContext emr, KernelDbContext kernel, long noteId, long branchId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var original = await GetAsync(emr, noteId, ct);
        if (original.State != NoteState.Final)
            throw new EmrException("Only a signed prescription can be corrected.");

        var replacement = new Note
        {
            BranchId = original.BranchId, PatientId = original.PatientId,
            EncounterId = original.EncounterId, AdmissionId = original.AdmissionId,
            DoctorId = actorId, SupersedesId = original.Id,
            Complaint = original.Complaint, OnExamination = original.OnExamination,
            Diagnosis = original.Diagnosis, Advice = original.Advice,
            FollowUpOn = original.FollowUpOn,
            CreatedAt = clock.GetUtcNow(), CreatedBy = actorId,
        };
        emr.Notes.Add(replacement);
        await emr.SaveChangesAsync(ct);

        foreach (var d in await emr.NoteDrugs.AsNoTracking()
                     .Where(d => d.NoteId == original.Id).OrderBy(d => d.Ordinal).ToListAsync(ct))
            emr.NoteDrugs.Add(new NoteDrug
            {
                NoteId = replacement.Id, ProductId = d.ProductId, DrugName = d.DrugName,
                Dose = d.Dose, Frequency = d.Frequency, Duration = d.Duration,
                Instruction = d.Instruction, Ordinal = d.Ordinal,
            });

        var affected = await emr.Database.ExecuteSqlAsync($"""
            UPDATE emr.note SET state = 'superseded' WHERE id = {noteId} AND state = 'final'
            """, ct);
        if (affected == 0)
            throw new EmrException("That prescription has already been corrected — reload the screen.");
        await emr.SaveChangesAsync(ct);
        await emr.Entry(original).ReloadAsync(ct);      // same staleness as finalise, same fix

        audit.Append(kernel, branchId, actorId, actorName, "emr.note.supersede", "emr.note",
            replacement.Id, after: new { supersedes = noteId }, tier: 2);
        await kernel.SaveChangesAsync(ct);
        return replacement;
    }

    // ---- templates and favourites ------------------------------------------

    public static IReadOnlyList<DrugLine> TemplateDrugs(NoteTemplate template)
        => JsonSerializer.Deserialize<List<DrugLine>>(template.Drugs) ?? [];

    public async Task<NoteTemplate> SaveTemplateAsync(
        EmrDbContext emr, long doctorId, string name, NoteBody body,
        IReadOnlyList<DrugLine> drugs, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new EmrException("A template needs a name.");
        var trimmed = name.Trim();
        var template = await emr.Templates.FirstOrDefaultAsync(
            t => t.DoctorId == doctorId && t.Name == trimmed, ct)
            ?? new NoteTemplate { DoctorId = doctorId, Name = trimmed };

        template.Complaint = Trim(body.Complaint);
        template.OnExamination = Trim(body.OnExamination);
        template.Diagnosis = Trim(body.Diagnosis);
        template.Advice = Trim(body.Advice);
        template.Drugs = JsonSerializer.Serialize(
            drugs.Where(d => !string.IsNullOrWhiteSpace(d.DrugName)).ToList());
        template.Active = true;
        if (template.Id == 0) emr.Templates.Add(template);
        await emr.SaveChangesAsync(ct);
        return template;
    }

    public async Task AddFavouriteAsync(
        EmrDbContext emr, long doctorId, long productId, string drugName,
        string? dose, string? frequency, string? duration, CancellationToken ct = default)
    {
        if (await emr.Favourites.AnyAsync(f => f.DoctorId == doctorId && f.ProductId == productId, ct))
            return;
        emr.Favourites.Add(new Favourite
        {
            DoctorId = doctorId, ProductId = productId, DrugName = drugName,
            Dose = dose, Frequency = frequency, Duration = duration,
        });
        await emr.SaveChangesAsync(ct);
    }

    // ---- 5A-7 nursing charts -------------------------------------------------

    public async Task<MarDose> ScheduleDoseAsync(
        EmrDbContext emr, long branchId, long admissionId, string drugName, string? dose,
        string? route, DateTimeOffset scheduledAt, long actorId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(drugName)) throw new EmrException("Which medicine?");
        var row = new MarDose
        {
            BranchId = branchId, AdmissionId = admissionId, DrugName = drugName.Trim(),
            Dose = Trim(dose), Route = Trim(route), ScheduledAt = scheduledAt,
            CreatedAt = clock.GetUtcNow(), CreatedBy = actorId,
        };
        emr.MarDoses.Add(row);
        await emr.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>
    /// Giving a dose is attributable and single-shot. The state guard is what stops two nurses
    /// on two screens both recording the same dose as given (ADR-0015).
    /// </summary>
    public async Task AdministerAsync(
        EmrDbContext emr, KernelDbContext kernel, long branchId, long doseId, string outcome,
        string? reason, long actorId, string actorName, CancellationToken ct = default)
    {
        if (outcome is not (DoseState.Given or DoseState.Missed or DoseState.Refused))
            throw new EmrException("A dose is given, missed or refused.");
        if (outcome != DoseState.Given && string.IsNullOrWhiteSpace(reason))
            throw new EmrException("Say why the dose was not given — the chart is a legal record.");

        var now = clock.GetUtcNow();
        var affected = await emr.Database.ExecuteSqlAsync($"""
            UPDATE emr.mar_dose
            SET state = {outcome}, state_reason = {reason}, administered_at = {now},
                administered_by = {actorId}
            WHERE id = {doseId} AND state = 'scheduled'
            """, ct);
        if (affected == 0)
            throw new EmrException("This dose is already recorded — reload the chart.");

        audit.Append(kernel, branchId, actorId, actorName, $"emr.mar.{outcome}", "emr.mar_dose",
            doseId, after: new { outcome, reason }, tier: 2);
        await kernel.SaveChangesAsync(ct);
    }

    public async Task<GlucoseReading> RecordGlucoseAsync(
        EmrDbContext emr, long branchId, long admissionId, short glucoseTenths, string? timing,
        short? insulinUnits, string? insulinRoute, long actorId, CancellationToken ct = default)
    {
        if (glucoseTenths <= 0) throw new EmrException("Enter the glucose reading.");
        var row = new GlucoseReading
        {
            BranchId = branchId, AdmissionId = admissionId, At = clock.GetUtcNow(),
            GlucoseTenths = glucoseTenths, Timing = Trim(timing), InsulinUnits = insulinUnits,
            InsulinRoute = Trim(insulinRoute), RecordedBy = actorId,
        };
        emr.GlucoseReadings.Add(row);
        await emr.SaveChangesAsync(ct);
        return row;
    }

    /// <summary>One handover per admission; recording it twice updates the same record.</summary>
    public async Task<ReceiveNote> RecordHandoverAsync(
        EmrDbContext emr, long branchId, long admissionId, string? from, string? condition,
        string? belongings, long actorId, CancellationToken ct = default)
    {
        var note = await emr.ReceiveNotes.FirstOrDefaultAsync(r => r.AdmissionId == admissionId, ct)
                   ?? new ReceiveNote { BranchId = branchId, AdmissionId = admissionId };
        note.ReceivedFrom = Trim(from);
        note.Condition = Trim(condition);
        note.Belongings = Trim(belongings);
        note.ReceivedAt = clock.GetUtcNow();
        note.ReceivedBy = actorId;
        if (note.Id == 0) emr.ReceiveNotes.Add(note);
        await emr.SaveChangesAsync(ct);
        return note;
    }

    // ---- helpers -------------------------------------------------------------

    public static async Task<Note> GetAsync(EmrDbContext emr, long noteId, CancellationToken ct = default)
        => await emr.Notes.FirstOrDefaultAsync(n => n.Id == noteId, ct)
           ?? throw new EmrException("No such prescription.");

    private static string? Trim(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
