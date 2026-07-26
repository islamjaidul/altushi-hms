using Hms.Appointments;
using Hms.Appointments.Data;
using Hms.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Appointments;

public sealed record DoctorCard(long DoctorId, string Name, string? Room, int TodayCount, int MaxSerials);

public sealed record QueueRow(
    long Id, int SerialNo, string PatientName, string Uhid, string? Phone,
    string DoctorName, string State, DateTimeOffset CreatedAt);

public sealed record PatientOption(long Id, string Label);

/// <summary>
/// §9A.2 module 2 (deliberately lite) / 05 §5 screen 9. The serial constraint is surfaced as
/// "next free is N" rather than as an error after the fact (ADR-0015 #1).
/// </summary>
[Authorize(Policy = Perm.AppointmentsRead)]
public class IndexModel(
    HmsTx tx, AppointmentsService appointments, SmsQueue sms,
    HospitalIdentity hospital, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? PatientId { get; set; }
    [BindProperty] public long DoctorId { get; set; }

    public DateOnly Today { get; private set; }
    public IReadOnlyList<DoctorCard> Doctors { get; private set; } = [];
    public IReadOnlyList<QueueRow> Queue { get; private set; } = [];
    public IReadOnlyList<PatientOption> Patients { get; private set; } = [];

    public int Waiting => Queue.Count(q => q.State is AppointmentState.Booked or AppointmentState.Arrived);
    public int Done => Queue.Count(q => q.State == AppointmentState.Done);

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var today = Today;

        (Doctors, Queue, Patients) = await tx.RunAsync(async s =>
        {
            var schedules = await s.Appt.Schedules.AsNoTracking().ToListAsync();
            var appts = await s.Appt.Appointments.AsNoTracking()
                .Where(a => a.OnDate == today)
                .OrderBy(a => a.SerialNo)
                .ToListAsync();

            var patientIds = appts.Select(a => a.PatientId).Distinct().ToList();
            var patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

            var byDoctor = schedules
                .GroupBy(x => x.DoctorId)
                .Select(g => new DoctorCard(
                    g.Key, g.First().DoctorName, g.First().Room,
                    appts.Count(a => a.DoctorId == g.Key && a.State != AppointmentState.Cancelled),
                    g.First().MaxSerials))
                .OrderBy(d => d.Name)
                .ToList();

            var names = schedules.GroupBy(x => x.DoctorId)
                .ToDictionary(g => g.Key, g => g.First().DoctorName);

            var rows = appts.Select(a => new QueueRow(
                a.Id, a.SerialNo,
                patients.TryGetValue(a.PatientId, out var p) ? p.FullName : "(patient removed)",
                patients.TryGetValue(a.PatientId, out var p2) ? p2.Uhid : "—",
                patients.TryGetValue(a.PatientId, out var p3) ? p3.Phone : null,
                names.GetValueOrDefault(a.DoctorId, "Doctor"),
                a.State, a.CreatedAt)).ToList();

            // The picker is a short recent list; the type-ahead endpoint covers the long tail (§7 U5).
            var options = await s.Reg.Patients.AsNoTracking()
                .Where(p => p.Active && p.MergedInto == null)
                .OrderByDescending(p => p.Id).Take(50)
                .Select(p => new PatientOption(p.Id, p.FullName + " — " + p.Uhid))
                .ToListAsync();

            return ((IReadOnlyList<DoctorCard>)byDoctor, (IReadOnlyList<QueueRow>)rows,
                    (IReadOnlyList<PatientOption>)options);
        });
    }

    public async Task<IActionResult> OnPostIssueAsync()
    {
        if (PatientId is null or 0) { await LoadAsync(); Fail("Select a patient first."); return Page(); }
        if (DoctorId == 0) { await LoadAsync(); Fail("Select a doctor."); return Page(); }

        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        try
        {
            var (serial, patientName) = await tx.RunAsync(async s =>
            {
                var row = await appointments.IssueSerialAsync(
                    s.Appt, BranchId, PatientId!.Value, DoctorId, today, ActorId);
                var patient = await s.Reg.Patients.SingleAsync(p => p.Id == PatientId.Value);
                var doctor = await s.Appt.Schedules.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.DoctorId == DoctorId);

                sms.QueueAppointment(s.Notif, BranchId, hospital.Name, patient.FullName,
                    row.SerialNo, doctor?.DoctorName ?? "the doctor", patient.Phone);
                await s.Notif.SaveChangesAsync();
                return (row.SerialNo, patient.FullName);
            });

            Toast($"Serial {serial} — {patientName} · SMS sent", "event_available");
            return Redirect("/appointments");
        }
        catch (AppointmentsException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    /// <summary>Advance is a state-guarded UPDATE — a second click on a stale page loses safely.</summary>
    public async Task<IActionResult> OnPostAdvanceAsync(long id, string from, string to)
    {
        try
        {
            await tx.RunAsync(s => appointments.AdvanceAsync(s.Appt, id, from, to));
            Toast(to switch
            {
                AppointmentState.InChamber => "Called in — patient is with the doctor",
                AppointmentState.Done => "Consultation finished",
                AppointmentState.Cancelled => "Serial cancelled",
                _ => "Queue updated",
            }, "arrow_forward");
        }
        catch (AppointmentsException e)
        {
            Toast(e.Message, "error");
        }
        return Redirect("/appointments");
    }
}
