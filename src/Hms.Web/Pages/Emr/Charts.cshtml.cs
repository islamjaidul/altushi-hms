using Hms.Emr;
using Hms.Emr.Data;
using Hms.Ipd.Data;
using Hms.Kernel.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Emr;

public sealed record WardPatient(long AdmissionId, string AdmissionNo, string Patient, string Bed);

/// <summary>
/// 5A-7: the ward's own paperwork — the medication administration record, the diabetic chart,
/// and the receive note taken at handover. All three are legal records of what a nurse did and
/// when, so every row is attributable and none can be edited after the fact.
/// </summary>
[Authorize(Policy = Perm.EmrChartRecord)]
public class ChartsModel(HmsTx tx, EmrService emr, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? AdmissionId { get; set; }

    [BindProperty] public string? DrugName { get; set; }
    [BindProperty] public string? Dose { get; set; }
    [BindProperty] public string? Route { get; set; }
    [BindProperty] public string? ScheduledDate { get; set; }
    [BindProperty] public string? ScheduledTime { get; set; }

    [BindProperty] public long DoseId { get; set; }
    [BindProperty] public string? Outcome { get; set; }
    [BindProperty] public string? Reason { get; set; }

    [BindProperty] public decimal? Glucose { get; set; }
    [BindProperty] public string? Timing { get; set; }
    [BindProperty] public short? InsulinUnits { get; set; }
    [BindProperty] public string? InsulinRoute { get; set; }

    [BindProperty] public string? ReceivedFrom { get; set; }
    [BindProperty] public string? Condition { get; set; }
    [BindProperty] public string? Belongings { get; set; }

    public IReadOnlyList<WardPatient> Ward { get; private set; } = [];
    public string? PatientName { get; private set; }
    public string? AdmissionNo { get; private set; }
    public IReadOnlyList<MarDose> Doses { get; private set; } = [];
    public IReadOnlyList<GlucoseReading> Glucoses { get; private set; } = [];
    public ReceiveNote? Handover { get; private set; }
    public IReadOnlyDictionary<long, string> Nurses { get; private set; } =
        new Dictionary<long, string>();

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        var admissions = await s.Ipd.Admissions.AsNoTracking()
            .Where(a => a.State == AdmissionState.Admitted
                        || a.State == AdmissionState.DischargeInitiated
                        || a.State == AdmissionState.Blocked)
            .OrderBy(a => a.Id).Take(120).ToListAsync();
        var stays = await s.Ipd.BedStays.AsNoTracking()
            .Where(st => st.ToAt == null).ToListAsync();
        var beds = await s.Ipd.Beds.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Code);
        var patients = await s.Reg.Patients.AsNoTracking()
            .Where(p => admissions.Select(a => a.PatientId).Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.FullName);

        Ward = admissions.Select(a =>
        {
            var stay = stays.FirstOrDefault(st => st.AdmissionId == a.Id);
            return new WardPatient(a.Id, a.AdmissionNo, patients.GetValueOrDefault(a.PatientId, "—"),
                stay is null ? "—" : beds.GetValueOrDefault(stay.BedId, "—"));
        }).ToList();

        if (AdmissionId is not { } id) return 0;
        var admission = admissions.FirstOrDefault(a => a.Id == id);
        if (admission is null) { AdmissionId = null; return 0; }

        AdmissionNo = admission.AdmissionNo;
        PatientName = patients.GetValueOrDefault(admission.PatientId, "—");
        Doses = await s.Emr.MarDoses.AsNoTracking()
            .Where(d => d.AdmissionId == id).OrderBy(d => d.ScheduledAt).Take(120).ToListAsync();
        Glucoses = await s.Emr.GlucoseReadings.AsNoTracking()
            .Where(g => g.AdmissionId == id).OrderByDescending(g => g.At).Take(40).ToListAsync();
        Handover = await s.Emr.ReceiveNotes.AsNoTracking()
            .FirstOrDefaultAsync(r => r.AdmissionId == id);

        var actorIds = Doses.Where(d => d.AdministeredBy is not null)
            .Select(d => d.AdministeredBy!.Value)
            .Concat(Glucoses.Select(g => g.RecordedBy)).Distinct().ToList();
        Nurses = await s.Auth.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        return 0;
    });

    public async Task<IActionResult> OnPostScheduleAsync()
    {
        try
        {
            var when = ParseWhen();
            await tx.RunAsync(async s => await emr.ScheduleDoseAsync(s.Emr, BranchId,
                AdmissionId!.Value, DrugName ?? "", Dose, Route, when, ActorId));
            Toast("Dose scheduled", "schedule");
            return Redirect($"/emr/charts/{AdmissionId}");
        }
        catch (EmrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostAdministerAsync()
    {
        try
        {
            await tx.RunAsync(async s => await emr.AdministerAsync(s.Emr, s.Kernel, BranchId,
                DoseId, Outcome ?? DoseState.Given, Reason, ActorId, ActorName));
            Toast("Chart updated", "task_alt");
            return Redirect($"/emr/charts/{AdmissionId}");
        }
        catch (EmrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostGlucoseAsync()
    {
        try
        {
            var tenths = (short)Math.Round((Glucose ?? 0) * 10, MidpointRounding.AwayFromZero);
            await tx.RunAsync(async s => await emr.RecordGlucoseAsync(s.Emr, BranchId,
                AdmissionId!.Value, tenths, Timing, InsulinUnits, InsulinRoute, ActorId));
            Toast("Reading recorded", "monitoring");
            return Redirect($"/emr/charts/{AdmissionId}");
        }
        catch (EmrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostHandoverAsync()
    {
        await tx.RunAsync(async s => await emr.RecordHandoverAsync(s.Emr, BranchId,
            AdmissionId!.Value, ReceivedFrom, Condition, Belongings, ActorId));
        Toast("Receive note recorded", "task_alt");
        return Redirect($"/emr/charts/{AdmissionId}");
    }

    /// <summary>
    /// Blank date means today and blank time means now — a nurse charting a dose she is giving
    /// right now should not have to type the clock (§7 U13).
    /// </summary>
    private DateTimeOffset ParseWhen()
    {
        var now = Ui.Local(clock.GetUtcNow());
        var date = !string.IsNullOrWhiteSpace(ScheduledDate)
                   && FlexibleDate.TryParse(ScheduledDate, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(now.DateTime);
        var time = !string.IsNullOrWhiteSpace(ScheduledTime)
                   && TimeOnly.TryParse(ScheduledTime, out var t)
            ? t
            : TimeOnly.FromDateTime(now.DateTime);
        // Dhaka midnight as a UTC instant, plus the wall-clock time of day: Npgsql binds
        // timestamptz in UTC only, so the conversion happens once, here.
        return Ui.DhakaMidnightUtc(date).Add(time.ToTimeSpan());
    }
}
