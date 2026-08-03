using System.ComponentModel.DataAnnotations;
using Hms.Emr;
using Hms.Emr.Data;
using Hms.Ipd.Data;
using Hms.Kernel.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Emr;

/// <summary>
/// R5 (spec 0041): the ward's to-do list, made attributable. Turning a patient, removing a
/// cannula and prepping for theatre are nursing work that lived on paper, so nobody could say
/// afterwards whether it was done, by whom, or why it was dropped.
///
/// One policy covers the page and its handlers (the Charts precedent): everything here is
/// nursing work, and a nurse who may open the list may work it.
/// </summary>
[Authorize(Policy = Perm.EmrTaskManage)]
public class TasksModel(HmsTx tx, EmrService emr, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? AdmissionId { get; set; }

    [BindProperty, StringLength(Bounds.Name)] public string? Title { get; set; }
    [BindProperty, StringLength(Bounds.Note)] public string? Details { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? Kind { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? DueDate { get; set; }
    [BindProperty, StringLength(Bounds.Code)] public string? DueTime { get; set; }

    [BindProperty] public long TaskId { get; set; }
    [BindProperty, StringLength(Bounds.Note)] public string? Reason { get; set; }

    public IReadOnlyList<WardPatient> Ward { get; private set; } = [];
    public string? PatientName { get; private set; }
    public string? AdmissionNo { get; private set; }
    public PatientBanner? Banner { get; private set; }
    /// <summary>Closed admission: list stays readable, open tasks stay closable (0042 F8).</summary>
    public bool ReadOnly { get; private set; }
    public IReadOnlyList<CareTask> Open { get; private set; } = [];
    public IReadOnlyList<CareTask> History { get; private set; } = [];
    public IReadOnlyDictionary<long, string> Nurses { get; private set; } =
        new Dictionary<long, string>();

    public DateTimeOffset Now { get; private set; }
    public bool IsLate(CareTask t) => t.DueAt is not null && t.DueAt < Now;

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        Now = clock.GetUtcNow();

        var admissions = await s.Ipd.Admissions.AsNoTracking()
            .Where(a => a.State == AdmissionState.Admitted
                        || a.State == AdmissionState.DischargeInitiated
                        || a.State == AdmissionState.Blocked)
            .OrderBy(a => a.Id).Take(120).ToListAsync();
        var stays = await s.Ipd.BedStays.AsNoTracking().Where(st => st.ToAt == null).ToListAsync();
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
            admission = await s.Ipd.Admissions.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);
            if (admission is null) { AdmissionId = null; return 0; }
            ReadOnly = true;                                  // closed, not gone (0042 F8)
        }

        AdmissionNo = admission.AdmissionNo;
        var taskPatient = await s.Reg.Patients.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == admission.PatientId);
        PatientName = taskPatient?.FullName ?? "—";
        if (taskPatient is not null)
            Banner = PatientBanner.Build(taskPatient, admission.ProvisionalDx,
                DateOnly.FromDateTime(Ui.Local(Now).DateTime));

        var all = await s.Emr.CareTasks.AsNoTracking()
            .Where(t => t.AdmissionId == id).OrderByDescending(t => t.Id).Take(120).ToListAsync();
        // Open first and by due time — the next thing to do is the top row, not the newest row.
        Open = [.. all.Where(t => t.State == TaskState.Open)
            .OrderBy(t => t.DueAt ?? DateTimeOffset.MaxValue).ThenBy(t => t.Id)];
        History = [.. all.Where(t => t.State != TaskState.Open)];

        var actorIds = all.Select(t => t.CreatedBy)
            .Concat(all.Where(t => t.CompletedBy is not null).Select(t => t.CompletedBy!.Value))
            .Distinct().ToList();
        Nurses = await s.Auth.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        return 0;
    });

    public async Task<IActionResult> OnPostCreateAsync()
    {
        try
        {
            await tx.RunAsync(async s =>
            {
                var admission = await WardGuard.RequireLiveAsync(s, AdmissionId);
                return await emr.CreateTaskAsync(s.Emr, s.Kernel, BranchId,
                    admission.Id, Title, Details, Kind, ParseDue(), ActorId, ActorName);
            });
            Toast("Task added", "task_alt");
            return Redirect($"/emr/tasks/{AdmissionId}");
        }
        catch (EmrException e) { await LoadAsync(); Fail(e.Message); return Page(); }
    }

    public async Task<IActionResult> OnPostDoneAsync()
    {
        try
        {
            await tx.RunAsync(async s =>
            {
                await WardGuard.RequireAsync(s, AdmissionId);   // close-out: existing, any state
                await emr.CompleteTaskAsync(s.Emr, s.Kernel, BranchId, TaskId, ActorId, ActorName);
                return 0;
            });
            Toast("Task closed", "task_alt");
            return Redirect($"/emr/tasks/{AdmissionId}");
        }
        catch (EmrException e) { await LoadAsync(); Fail(e.Message); return Page(); }
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        try
        {
            await tx.RunAsync(async s =>
            {
                await WardGuard.RequireAsync(s, AdmissionId);
                await emr.CancelTaskAsync(s.Emr, s.Kernel, BranchId, TaskId, Reason, ActorId, ActorName);
                return 0;
            });
            Toast("Task cancelled", "task_alt");
            return Redirect($"/emr/tasks/{AdmissionId}");
        }
        catch (EmrException e) { await LoadAsync(); Fail(e.Message); return Page(); }
    }

    /// <summary>
    /// A due time is optional — most ward tasks are "this shift". Blank date with a time means
    /// today, so the nurse types one box, not two (§7 U13).
    /// </summary>
    private DateTimeOffset? ParseDue()
    {
        if (string.IsNullOrWhiteSpace(DueDate) && string.IsNullOrWhiteSpace(DueTime)) return null;

        var now = Ui.Local(clock.GetUtcNow());
        var date = !string.IsNullOrWhiteSpace(DueDate) && FlexibleDate.TryParse(DueDate, out var parsed)
            ? parsed
            : DateOnly.FromDateTime(now.DateTime);
        var time = !string.IsNullOrWhiteSpace(DueTime) && TimeOnly.TryParse(DueTime, out var t)
            ? t
            : TimeOnly.FromDateTime(now.DateTime);
        return Ui.DhakaMidnightUtc(date).Add(time.ToTimeSpan());
    }
}
