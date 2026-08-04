using System.ComponentModel.DataAnnotations;
using Hms.Emr;
using Hms.Kernel.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Emr;

/// <summary>
/// Spec 0046: the ward's front door. A newly admitted patient is received exactly once —
/// arrival vitals and the handover record land in one transaction — and only by nursing staff
/// of the ward's department (staff with no department assignment, e.g. the matron or admin,
/// may receive anywhere; the same gate is applied server-side on the POST, not just hidden in
/// the station UI). Re-opening a received patient updates the same handover record
/// (<see cref="EmrService.RecordHandoverAsync"/> is an upsert per admission); vitals are always
/// a new row, never an overwrite — a falling BP is a sequence.
/// </summary>
[Authorize(Policy = Perm.EmrVitalsRecord)]
public class ReceiveModel(HmsTx tx, EmrService emr, AuditWriter audit, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long AdmissionId { get; set; }

    // Same physiological bounds as /emr/vitals (AUD-VAL-20) — a blank field is "not taken".
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
    // Bounds mirror the emr.receive_note length CHECKs (Hardening0039).
    [BindProperty, StringLength(200, ErrorMessage = "Received from — at most 200 characters")]
    public string? ReceivedFrom { get; set; }
    [BindProperty, StringLength(10000, ErrorMessage = "Condition on arrival — at most 10000 characters")]
    public string? Condition { get; set; }
    [BindProperty, StringLength(4000, ErrorMessage = "Belongings — at most 4000 characters")]
    public string? Belongings { get; set; }

    public string Patient { get; private set; } = "—";
    public string? AgeSex { get; private set; }
    public string? Allergies { get; private set; }
    public string? Diagnosis { get; private set; }
    public string AdmissionNo { get; private set; } = "—";
    public string Bed { get; private set; } = "—";
    public string WardName { get; private set; } = "—";
    public string? DepartmentName { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }
    /// <summary>True when the admission is not receivable by this user — the form is hidden.</summary>
    public bool Blocked { get; private set; }

    public async Task OnGetAsync()
    {
        var error = await tx.RunAsync(LoadAsync);
        if (error is not null) { Blocked = true; Fail(error); }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var error = await tx.RunAsync(async s =>
            {
                var gate = await LoadAsync(s);
                if (gate is not null) return gate;

                var admission = await WardGuard.RequireLiveAsync(s, AdmissionId);
                var note = await emr.RecordHandoverAsync(
                    s.Emr, BranchId, admission.Id, ReceivedFrom, Condition, Belongings, ActorId);
                var anyVitals = Systolic is not null || Diastolic is not null || Pulse is not null
                                || Temperature is not null || Weight is not null || SpO2 is not null;
                if (anyVitals)
                    await emr.RecordVitalsAsync(s.Emr, BranchId, admission.PatientId, null,
                        admission.Id, Systolic, Diastolic, Pulse, Tenths(Temperature),
                        Tenths(Weight), SpO2, ActorId);

                // RecordHandoverAsync itself appends no audit row; receiving a patient is a
                // clinical event with a name on it, so the row is written here (guardrails §4).
                audit.Append(s.Kernel, BranchId, ActorId, ActorName,
                    "emr.receive", "emr.receive_note", note.Id,
                    after: new { admissionId = admission.Id, vitals = anyVitals }, tier: 2);
                await s.Kernel.SaveChangesAsync();
                return null;
            });
            if (error is not null) { Blocked = true; Fail(error); return Page(); }
        }
        catch (EmrException e) { Fail(e.Message); return Page(); }
        Toast("Patient received — handover recorded", "monitoring");
        return Redirect("/ipd/station");
    }

    /// <summary>Loads the banner and runs every receive precondition; null means receivable.</summary>
    private async Task<string?> LoadAsync(TxScope s)
    {
        var admission = await s.Ipd.Admissions.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == AdmissionId);
        if (admission is null) return "No such admission.";
        if (!WardGuard.IsLive(admission.State))
            return "This admission is closed — there is nothing to receive.";

        var stay = await s.Ipd.BedStays.AsNoTracking()
            .FirstOrDefaultAsync(st => st.AdmissionId == admission.Id && st.ToAt == null);
        var bed = stay is null ? null
            : await s.Ipd.Beds.AsNoTracking().FirstOrDefaultAsync(b => b.Id == stay.BedId);
        var ward = bed is null ? null
            : await s.Ipd.Wards.AsNoTracking().FirstOrDefaultAsync(w => w.Id == bed.WardId);
        if (bed is null || ward is null)
            return "This patient has no current bed — receiving happens on the ward.";

        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var patient = await s.Reg.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == admission.PatientId);
        Patient = patient?.FullName ?? "—";
        AgeSex = patient is null ? null
            : $"{Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today)}"
              + $" · {Ui.SexShort(patient.Sex)} · {patient.Uhid}";
        Allergies = patient?.Allergies;
        Diagnosis = admission.ProvisionalDx;
        AdmissionNo = admission.AdmissionNo;
        Bed = bed.Code;
        WardName = ward.Name;
        DepartmentName = ward.DepartmentId is { } dept
            ? await s.Ipd.Departments.AsNoTracking()
                .Where(d => d.Id == dept).Select(d => d.Name).FirstOrDefaultAsync()
            : null;

        var existing = await s.Emr.ReceiveNotes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.AdmissionId == admission.Id);
        if (existing is not null)
        {
            ReceivedAt = existing.ReceivedAt;
            ReceivedFrom ??= existing.ReceivedFrom;
            Condition ??= existing.Condition;
            Belongings ??= existing.Belongings;
        }

        // Spec 0046: the department gate. Assigned staff receive only their departments'
        // patients; the check runs on GET (to hide the form) and again inside the POST tx.
        var myDeptIds = await s.Ipd.DepartmentStaff.AsNoTracking()
            .Where(d => d.Active && d.UserId == ActorId)
            .Select(d => d.DepartmentId).ToListAsync();
        if (myDeptIds.Count > 0 && (ward.DepartmentId is not { } wd || !myDeptIds.Contains(wd)))
            return $"{Patient} is in {WardName}"
                   + (DepartmentName is null ? "" : $" — {DepartmentName}")
                   + ", outside your department. That department's station receives this patient.";
        return null;
    }

    private static short? Tenths(decimal? value)
        => value is null ? null : (short)Math.Round(value.Value * 10, MidpointRounding.AwayFromZero);
}
