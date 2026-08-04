using Hms.Emr;
using Hms.Emr.Data;
using Hms.Ipd.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ipd;

public sealed record StationPatient(
    long AdmissionId, string AdmissionNo, string Bed, string Patient, string? AgeSex,
    string? Allergies, string? Diagnosis,
    string? Vitals, DateTimeOffset? VitalsAt,
    int DueDoses, int OverdueDoses, int OpenTasks, int OverdueTasks, int PendingIndents,
    bool AwaitingReceive);

public sealed record StationWard(
    long Id, string Name, string Class,
    IReadOnlyList<StationPatient> Patients, IReadOnlyList<DutyAssignment> Duty);

public sealed record RosterLine(string Shift, string Employee, string Hours);

/// <summary>
/// R5 (spec 0041): the ward nurse's one screen. It writes nothing — every number here is a
/// pointer into the screen that owns it (the chart, the task list, the folio), so the console
/// cannot become a second way to record clinical facts.
///
/// The load reads each module's context separately and joins in memory. That is not a style
/// choice: two <c>DbContext</c> instances cannot appear in one EF query even inside one
/// transaction, and <c>CrossContextQueryTests</c> fails the build for trying (ADR-0003, G19).
/// </summary>
[Authorize(Policy = Perm.IpdRead)]
public class StationModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? WardId { get; set; }

    public IReadOnlyList<StationWard> Wards { get; private set; } = [];
    public IReadOnlyList<(long Id, string Name)> AllWards { get; private set; } = [];
    public IReadOnlyList<RosterLine> Roster { get; private set; } = [];
    public bool RosterPublished { get; private set; }
    /// <summary>Spec 0046: the signed-in user's departments — empty means unscoped (all wards).</summary>
    public IReadOnlyList<string> MyDepartments { get; private set; } = [];
    public int AwaitingCount { get; private set; }
    public int TotalOverdue => Wards.Sum(w => w.Patients.Sum(p => p.OverdueDoses));
    public int TotalPatients => Wards.Sum(w => w.Patients.Count);

    public async Task OnGetAsync() => await tx.RunAsync(async s =>
    {
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(Ui.Local(now).DateTime);

        // ---- ipd: who is in which bed ------------------------------------------------------
        var wards = await s.Ipd.Wards.AsNoTracking().Where(w => w.Active)
            .OrderBy(w => w.Id).ToListAsync();

        // ---- spec 0046: department scoping — an assigned nurse sees only her departments ----
        // Membership is read per request (not a claim), so an assignment change is live on the
        // next load. Users with no assignment (matron, admin) keep the whole-hospital view.
        var myDeptIds = await s.Ipd.DepartmentStaff.AsNoTracking()
            .Where(d => d.Active && d.UserId == ActorId)
            .Select(d => d.DepartmentId).ToListAsync();
        if (myDeptIds.Count > 0)
        {
            MyDepartments = await s.Ipd.Departments.AsNoTracking()
                .Where(d => myDeptIds.Contains(d.Id))
                .OrderBy(d => d.Name).Select(d => d.Name).ToListAsync();
            wards = wards.Where(w => w.DepartmentId is { } dept && myDeptIds.Contains(dept))
                .ToList();
        }
        AllWards = wards.Select(w => (w.Id, w.Name)).ToList();
        if (WardId is { } only && wards.All(w => w.Id != only)) WardId = null;
        var shown = WardId is { } pick ? wards.Where(w => w.Id == pick).ToList() : wards;
        var shownIds = shown.Select(w => w.Id).ToList();

        var beds = await s.Ipd.Beds.AsNoTracking()
            .Where(b => shownIds.Contains(b.WardId)).OrderBy(b => b.Code).ToListAsync();
        var bedIds = beds.Select(b => b.Id).ToList();
        var stays = await s.Ipd.BedStays.AsNoTracking()
            .Where(st => st.ToAt == null && bedIds.Contains(st.BedId)).ToListAsync();
        var admissionIds = stays.Select(st => st.AdmissionId).ToList();
        var admissions = await s.Ipd.Admissions.AsNoTracking()
            .Where(a => admissionIds.Contains(a.Id)
                        && (a.State == AdmissionState.Admitted
                            || a.State == AdmissionState.DischargeInitiated
                            || a.State == AdmissionState.Blocked))
            .OrderBy(a => a.Id).Take(120).ToListAsync();
        var liveIds = admissions.Select(a => a.Id).ToList();

        var indents = await s.Ipd.Indents.AsNoTracking()
            .Where(i => liveIds.Contains(i.AdmissionId) && i.State == IndentState.Requested)
            .Select(i => i.AdmissionId).ToListAsync();
        var duty = await s.Ipd.DutyAssignments.AsNoTracking()
            .Where(d => d.OnDate == today && d.Active && shownIds.Contains(d.WardId))
            .OrderBy(d => d.ShiftLabel).ThenBy(d => d.StaffName).ToListAsync();

        // ---- reg: the patient banner --------------------------------------------------------
        var patientIds = admissions.Select(a => a.PatientId).Distinct().ToList();
        var patients = await s.Reg.Patients.AsNoTracking()
            .Where(p => patientIds.Contains(p.Id)).ToListAsync();

        // ---- emr: what is due, overdue, and waiting -----------------------------------------
        var doses = await s.Emr.MarDoses.AsNoTracking()
            .Where(d => liveIds.Contains(d.AdmissionId) && d.State == DoseState.Scheduled)
            .Select(d => new { d.AdmissionId, d.ScheduledAt, d.State }).ToListAsync();
        var tasks = await s.Emr.CareTasks.AsNoTracking()
            .Where(t => liveIds.Contains(t.AdmissionId) && t.State == TaskState.Open)
            .Select(t => new { t.AdmissionId, t.DueAt }).ToListAsync();
        // Vitals are only news for a day; an older reading on a ward board is furniture.
        var since = now.AddHours(-24);
        var recentVitals = await s.Emr.Vitals.AsNoTracking()
            .Where(v => v.AdmissionId != null && liveIds.Contains(v.AdmissionId!.Value)
                        && v.RecordedAt >= since)
            .OrderByDescending(v => v.RecordedAt).ToListAsync();
        // Spec 0046: the receive note is the "this ward has accepted the patient" fact —
        // its absence puts the tile in the New — awaiting receive bucket.
        var received = await s.Emr.ReceiveNotes.AsNoTracking()
            .Where(r => liveIds.Contains(r.AdmissionId))
            .Select(r => r.AdmissionId).ToListAsync();

        Wards = shown.Select(w =>
        {
            var wardBeds = beds.Where(b => b.WardId == w.Id).ToList();
            var tiles = new List<StationPatient>();
            foreach (var bed in wardBeds)
            {
                var stay = stays.FirstOrDefault(st => st.BedId == bed.Id);
                var admission = stay is null
                    ? null : admissions.FirstOrDefault(a => a.Id == stay.AdmissionId);
                if (admission is null) continue;               // free/cleaning beds are the board's job

                var patient = patients.FirstOrDefault(p => p.Id == admission.PatientId);
                var vitals = recentVitals.FirstOrDefault(v => v.AdmissionId == admission.Id);
                var mine = doses.Where(d => d.AdmissionId == admission.Id).ToList();
                var myTasks = tasks.Where(t => t.AdmissionId == admission.Id).ToList();

                tiles.Add(new StationPatient(
                    admission.Id, admission.AdmissionNo, bed.Code,
                    patient?.FullName ?? "—",
                    patient is null ? null
                        : $"{Ui.AgeDisplay(patient.Dob, patient.AgeYears, patient.AgeMonths, today)}"
                          + $" · {Ui.SexShort(patient.Sex)} · {patient.Uhid}",
                    patient?.Allergies, admission.ProvisionalDx,
                    Pages.Emr.QueueModel.VitalsSummary(vitals), vitals?.RecordedAt,
                    mine.Count,
                    mine.Count(d => MarSchedule.IsOverdue(d.State, d.ScheduledAt, now)),
                    myTasks.Count,
                    myTasks.Count(t => t.DueAt is not null && t.DueAt < now),
                    indents.Count(id => id == admission.Id),
                    !received.Contains(admission.Id)));
            }

            return new StationWard(w.Id, w.Name, w.Class, tiles,
                duty.Where(d => d.WardId == w.Id).ToList());
        }).ToList();
        AwaitingCount = Wards.Sum(w => w.Patients.Count(p => p.AwaitingReceive));

        // ---- hr: today's published roster, where there is one -------------------------------
        // The ERP host carries the hr schema but a hospital may run the roster on the HRM SKU
        // (or not at all). An empty panel with a sentence beats a panel that looks broken.
        var rosters = await s.Hr.Rosters.AsNoTracking()
            .Where(r => r.Published && r.FromDate <= today && r.ToDate >= today)
            .Select(r => r.Id).ToListAsync();
        RosterPublished = rosters.Count > 0;
        if (RosterPublished)
        {
            var entries = await s.Hr.RosterEntries.AsNoTracking()
                .Where(e => rosters.Contains(e.RosterId) && e.OnDate == today && !e.WeeklyOff)
                .Take(60).ToListAsync();
            var shifts = await s.Hr.Shifts.AsNoTracking().ToDictionaryAsync(x => x.Id);
            var staffIds = entries.Select(e => e.EmployeeId).Distinct().ToList();
            var staff = await s.Hr.Employees.AsNoTracking()
                .Where(e => staffIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName);

            Roster = entries.Select(e =>
            {
                shifts.TryGetValue(e.ShiftId, out var shift);
                return new RosterLine(
                    shift?.Name ?? "—",
                    staff.GetValueOrDefault(e.EmployeeId, "—"),
                    shift is null ? "—" : $"{shift.StartsAt:HH\\:mm}–{shift.EndsAt:HH\\:mm}");
            }).OrderBy(r => r.Shift).ThenBy(r => r.Employee).ToList();
        }

        return 0;
    });
}
