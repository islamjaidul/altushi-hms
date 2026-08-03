using System.ComponentModel.DataAnnotations;
using Hms.Admin;
using Hms.Billing;
using Hms.Emr;
using Hms.Emr.Data;
using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Emr;

public sealed record IndoorRx(long Id, string State, DateTimeOffset At, int Drugs, string Doctor);

/// <summary>
/// The ward-round prescription (spec 0041).
///
/// <c>emr.note</c> has carried an <c>AdmissionId</c> since spec 0024 and the check constraint
/// admits it, but nothing ever wrote one: <see cref="ConsultModel"/> is keyed to an outdoor
/// encounter, so indoor prescribing had no screen and every MAR dose was typed by hand. That is
/// the missing half of US5.5 — a chart generated from a prescription needs a prescription.
///
/// Deliberately thinner than the OPD consult: no billing, no test ordering, no favourites. A
/// consultant on a ward round writes drugs and a line of clinical text; the money for an indoor
/// stay is the folio's job (M6), not this screen's.
/// </summary>
[Authorize(Policy = Perm.EmrNoteWrite)]
public class IndoorModel(
    HmsTx tx, EmrService emr, BillingService billing, FolioService folios, RateResolver rates,
    AuditWriter audit, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? AdmissionId { get; set; }

    [BindProperty, StringLength(Bounds.Clinical)] public string? Complaint { get; set; }
    [BindProperty, StringLength(Bounds.Clinical)] public string? Diagnosis { get; set; }
    [BindProperty, StringLength(Bounds.Clinical)] public string? Advice { get; set; }

    [BindProperty] public List<long?> DrugProductId { get; set; } = [];
    [BindProperty] public List<string?> DrugName { get; set; } = [];
    [BindProperty] public List<string?> DrugDose { get; set; } = [];
    [BindProperty] public List<string?> DrugFrequency { get; set; } = [];
    [BindProperty] public List<string?> DrugDuration { get; set; } = [];
    [BindProperty] public List<string?> DrugInstruction { get; set; } = [];

    public IReadOnlyList<WardPatient> Ward { get; private set; } = [];
    public string? PatientName { get; private set; }
    public string? AdmissionNo { get; private set; }
    public PatientBanner? Banner { get; private set; }
    public IReadOnlyList<IndoorRx> Prescriptions { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        var admissions = await s.Ipd.Admissions.AsNoTracking()
            .Where(a => a.State == AdmissionState.Admitted
                        || a.State == AdmissionState.DischargeInitiated
                        || a.State == AdmissionState.Blocked)
            .OrderBy(a => a.Id).Take(120).ToListAsync();
        var stays = await s.Ipd.BedStays.AsNoTracking().Where(st => st.ToAt == null).ToListAsync();
        var beds = await s.Ipd.Beds.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code);
        var patients = await s.Reg.Patients.AsNoTracking()
            .Where(p => admissions.Select(a => a.PatientId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id);

        Ward = admissions.Select(a =>
        {
            var stay = stays.FirstOrDefault(st => st.AdmissionId == a.Id);
            return new WardPatient(a.Id, a.AdmissionNo,
                patients.TryGetValue(a.PatientId, out var p) ? p.FullName : "—",
                stay is null ? "—" : beds.GetValueOrDefault(stay.BedId, "—"));
        }).ToList();

        if (AdmissionId is not { } id) return 0;
        var admission = admissions.FirstOrDefault(a => a.Id == id);
        if (admission is null) { AdmissionId = null; return 0; }

        AdmissionNo = admission.AdmissionNo;
        if (patients.TryGetValue(admission.PatientId, out var patient))
        {
            PatientName = patient.FullName;
            Banner = PatientBanner.Build(patient, admission.ProvisionalDx,
                DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime));
        }

        var notes = await s.Emr.Notes.AsNoTracking()
            .Where(n => n.AdmissionId == id).OrderByDescending(n => n.Id).Take(20).ToListAsync();
        var noteIds = notes.Select(n => n.Id).ToList();
        var counts = (await s.Emr.NoteDrugs.AsNoTracking()
                .Where(d => noteIds.Contains(d.NoteId)).Select(d => d.NoteId).ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        var doctors = await s.Auth.Users.AsNoTracking()
            .Where(u => notes.Select(n => n.DoctorId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        Prescriptions = notes.Select(n => new IndoorRx(
            n.Id, n.State, n.FinalisedAt ?? n.CreatedAt, counts.GetValueOrDefault(n.Id),
            doctors.GetValueOrDefault(n.DoctorId, "—"))).ToList();
        return 0;
    });

    public async Task<IActionResult> OnPostSaveAsync() => await WriteAsync(sign: false);

    public async Task<IActionResult> OnPostSignAsync() => await WriteAsync(sign: true);

    private async Task<IActionResult> WriteAsync(bool sign)
    {
        var drugs = CollectDrugs();
        // Parallel lists carry no annotation, so the bound is judged here — the Consult precedent.
        for (var i = 0; i < drugs.Count; i++)
        {
            var d = drugs[i];
            if (new[] { d.DrugName, d.Dose, d.Frequency, d.Duration, d.Instruction }
                .Any(t => t?.Length > Bounds.Name))
            {
                await LoadAsync();
                Fail($"Drug line {i + 1}: keep each entry under {Bounds.Name} characters.");
                return Page();
            }
        }

        try
        {
            var visitNote = await tx.RunAsync(async s =>
            {
                // A prescription is NEW clinical work — the admission must be live. A doctor
                // could previously prescribe on a discharged or deceased patient by posting the
                // id (spec 0042 F10).
                var admission = await WardGuard.RequireLiveAsync(s, AdmissionId);

                // AUD-VAL-21b: a drug line naming a product id the formulary does not have must
                // not print as a medicine (same check as the OPD consult).
                var drugIds = drugs.Where(d => d.ProductId is > 0)
                    .Select(d => d.ProductId!.Value).Distinct().ToList();
                if (drugIds.Count > 0)
                {
                    var known = await s.Pharm.Products.AsNoTracking()
                        .Where(p => drugIds.Contains(p.Id)).CountAsync();
                    if (known != drugIds.Count)
                        throw new EmrException("A medicine line points at a product that is "
                            + "not in the formulary — re-pick it from the list.");
                }

                var note = await emr.OpenDraftAsync(s.Emr, BranchId, admission.PatientId,
                    null, admission.Id, ActorId);
                await emr.SaveDraftAsync(s.Emr, note.Id,
                    new NoteBody(Complaint, null, Diagnosis, Advice, null), drugs);
                if (!sign) return (note.Id, (IpdBilling.VisitResult?)null);

                await emr.FinaliseAsync(s.Emr, s.Kernel, note.Id, BranchId, ActorId, ActorName);
                // §5 M6 [M]: the signature IS the visit record — one folio charge per doctor
                // per day, idempotent, priced by the rate plan (spec 0042 F1).
                var visit = await IpdBilling.PostConsultantVisitAsync(s, billing, folios, rates,
                    audit, clock, BranchId, admission.Id, ActorId, note.Id, ActorId, ActorName);
                return (note.Id, (IpdBilling.VisitResult?)visit);
            });

            var message = !sign ? "Draft saved"
                : visitNote.Item2 is { Charged: true } ? "Prescription signed · visit charge posted"
                : "Prescription signed";
            Toast(message, "task_alt");
            return Redirect($"/emr/indoor/{AdmissionId}");
        }
        catch (EmrException e) { await LoadAsync(); Fail(e.Message); return Page(); }
        catch (IpdException e) { await LoadAsync(); Fail(e.Message); return Page(); }
    }

    private List<DrugLine> CollectDrugs()
    {
        var lines = new List<DrugLine>();
        for (var i = 0; i < DrugName.Count; i++)
        {
            var name = DrugName[i];
            if (string.IsNullOrWhiteSpace(name)) continue;
            var productId = i < DrugProductId.Count ? DrugProductId[i] : null;
            lines.Add(new DrugLine(productId is > 0 ? productId : null, name.Trim(),
                At(DrugDose, i), At(DrugFrequency, i), At(DrugDuration, i), At(DrugInstruction, i)));
        }
        return lines;

        static string? At(List<string?> list, int i)
            => i < list.Count && !string.IsNullOrWhiteSpace(list[i]) ? list[i]!.Trim() : null;
    }
}
