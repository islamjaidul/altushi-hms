using System.ComponentModel.DataAnnotations;
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

    // Spec 0041: this screen predates the 0039 input tier — its boxes reached Postgres unbounded,
    // so a mistyped glucose came back as a 23514 through the fault boundary instead of a sentence.
    // Every bound value now states the same limit its column carries.
    [BindProperty, StringLength(Bounds.Name)] public string? DrugName { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? Dose { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? Route { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? ScheduledDate { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? ScheduledTime { get; set; }

    [BindProperty] public long DoseId { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? Outcome { get; set; }
    [BindProperty, StringLength(Bounds.Note)] public string? Reason { get; set; }

    /// <summary>The note whose prescription the schedule is generated from.</summary>
    [BindProperty] public long NoteId { get; set; }

    /// <summary>Matches ck_glucose_value (0.5–50.0 mmol/L, stored in tenths).</summary>
    [BindProperty, Range(0.5, 50.0, ErrorMessage = "Glucose must be between 0.5 and 50.0 mmol/L")]
    public decimal? Glucose { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? Timing { get; set; }
    /// <summary>Matches ck_glucose_insulin.</summary>
    [BindProperty, Range(0, 200, ErrorMessage = "Insulin must be between 0 and 200 units")]
    public short? InsulinUnits { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? InsulinRoute { get; set; }

    [BindProperty, StringLength(Bounds.Name)] public string? ReceivedFrom { get; set; }
    [BindProperty, StringLength(Bounds.Clinical)] public string? Condition { get; set; }
    [BindProperty, StringLength(Bounds.Note)] public string? Belongings { get; set; }

    public IReadOnlyList<WardPatient> Ward { get; private set; } = [];
    public string? PatientName { get; private set; }
    public string? AdmissionNo { get; private set; }
    public PatientBanner? Banner { get; private set; }

    /// <summary>
    /// Spec 0042 F8: a closed admission's chart opens read-only instead of vanishing. Before
    /// this, discharge orphaned every open dose — invisible, un-closable, in a legal record.
    /// Read-only means: no new scheduling or generation, but a still-open dose keeps its
    /// recording control so the nurse can close it with a reason.
    /// </summary>
    public bool ReadOnly { get; private set; }
    public IReadOnlyList<MarDose> Doses { get; private set; } = [];
    public IReadOnlyList<GlucoseReading> Glucoses { get; private set; } = [];
    public ReceiveNote? Handover { get; private set; }
    public IReadOnlyDictionary<long, string> Nurses { get; private set; } =
        new Dictionary<long, string>();

    /// <summary>Signed indoor prescriptions a schedule can be generated from (spec 0041).</summary>
    public IReadOnlyList<(long Id, DateTimeOffset At, int Drugs)> Prescriptions { get; private set; } = [];

    public DateTimeOffset Now { get; private set; }

    /// <summary>
    /// LC-NUR-06: a dose past its time is visually distinct from one merely waiting. Computed at
    /// render time from the row and the clock — nothing writes an "overdue" state into a chart.
    /// </summary>
    public bool IsOverdue(MarDose d) => MarSchedule.IsOverdue(d.State, d.ScheduledAt, Now);

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        Now = clock.GetUtcNow();
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
        if (admission is null)
        {
            // Not in the live rail — but a closed admission's record must still be readable
            // (spec 0042 F8). Only a genuinely unknown id falls back to the ward list.
            admission = await s.Ipd.Admissions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
            if (admission is null) { AdmissionId = null; return 0; }
            ReadOnly = true;
        }

        AdmissionNo = admission.AdmissionNo;
        var chartPatient = await s.Reg.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == admission.PatientId);
        PatientName = chartPatient?.FullName ?? patients.GetValueOrDefault(admission.PatientId, "—");
        if (chartPatient is not null)
            Banner = PatientBanner.Build(chartPatient, admission.ProvisionalDx,
                DateOnly.FromDateTime(Ui.Local(Now).DateTime));
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

        // Only signed prescriptions: a draft can still change, and a chart built from a draft
        // would be a schedule for medicines the doctor had not yet committed to.
        var notes = await s.Emr.Notes.AsNoTracking()
            .Where(n => n.AdmissionId == id && n.State == NoteState.Final)
            .OrderByDescending(n => n.Id).Take(10).ToListAsync();
        var noteIds = notes.Select(n => n.Id).ToList();
        var drugCounts = (await s.Emr.NoteDrugs.AsNoTracking()
                .Where(d => noteIds.Contains(d.NoteId)).Select(d => d.NoteId).ToListAsync())
            .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
        Prescriptions = notes
            .Select(n => (n.Id, n.FinalisedAt ?? n.CreatedAt, drugCounts.GetValueOrDefault(n.Id)))
            .Where(p => p.Item3 > 0).ToList();
        return 0;
    });

    /// <summary>
    /// US5.5: build the medicine chart from the signed prescription instead of typing every dose.
    /// Pressing it twice is safe — the service skips instants it has already scheduled.
    /// </summary>
    public async Task<IActionResult> OnPostGenerateAsync()
    {
        try
        {
            var result = await tx.RunAsync(async s =>
            {
                var admission = await WardGuard.RequireLiveAsync(s, AdmissionId);
                return await emr.GenerateScheduleAsync(
                    s.Emr, s.Kernel, BranchId, admission.Id, NoteId, ActorId, ActorName);
            });

            var message = result.Inserted switch
            {
                0 => "Nothing new to schedule — this prescription is already on the chart",
                1 => "1 dose scheduled",
                _ => $"{result.Inserted} doses scheduled",
            };
            if (result.Unreadable.Count > 0)
                message += $" · schedule by hand: {string.Join(", ", result.Unreadable)}";
            Toast(message, "schedule");
            return Redirect($"/emr/charts/{AdmissionId}");
        }
        catch (EmrException e) { await LoadAsync(); Fail(e.Message); return Page(); }
    }

    public async Task<IActionResult> OnPostScheduleAsync()
    {
        try
        {
            var when = ParseWhen();
            await tx.RunAsync(async s =>
            {
                var admission = await WardGuard.RequireLiveAsync(s, AdmissionId);
                return await emr.ScheduleDoseAsync(s.Emr, BranchId,
                    admission.Id, DrugName ?? "", Dose, Route, when, ActorId);
            });
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
            await tx.RunAsync(async s =>
            {
                // Close-out stays legal on a closed admission (0042 F8) — the guard only
                // requires that the admission is real, not that it is live.
                await WardGuard.RequireAsync(s, AdmissionId);
                await emr.AdministerAsync(s.Emr, s.Kernel, BranchId,
                    DoseId, Outcome ?? DoseState.Given, Reason, ActorId, ActorName);
                return 0;
            });
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
            await tx.RunAsync(async s =>
            {
                var admission = await WardGuard.RequireLiveAsync(s, AdmissionId);
                return await emr.RecordGlucoseAsync(s.Emr, BranchId,
                    admission.Id, tenths, Timing, InsulinUnits, InsulinRoute, ActorId);
            });
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
        try
        {
            await tx.RunAsync(async s =>
            {
                var admission = await WardGuard.RequireLiveAsync(s, AdmissionId);
                return await emr.RecordHandoverAsync(s.Emr, BranchId,
                    admission.Id, ReceivedFrom, Condition, Belongings, ActorId);
            });
            Toast("Receive note recorded", "task_alt");
            return Redirect($"/emr/charts/{AdmissionId}");
        }
        catch (EmrException e) { await LoadAsync(); Fail(e.Message); return Page(); }
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
