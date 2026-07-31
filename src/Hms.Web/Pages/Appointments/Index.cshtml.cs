using Hms.Appointments;
using Hms.Appointments.Data;
using Hms.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Appointments;

/// <summary>
/// <paramref name="TodayCount"/> and <paramref name="NextSerial"/> are two different questions
/// and were one field until spec 0032 (M3-D2).
/// <list type="bullet">
/// <item><description><b>TodayCount</b> — patients on this doctor's queue today. A cancelled
/// appointment is not a patient seen, so it is excluded. This is what reads next to
/// "Capacity 40".</description></item>
/// <item><description><b>NextSerial</b> — the number the next patient will actually be given.
/// A consumed serial is never reissued, so cancelled rows very much count. This comes from
/// <see cref="AppointmentsService.NextSerialFor"/>, the same arithmetic the allocator uses.</description></item>
/// </list>
/// Serving both from one count made the dropdown offer a number the allocator would not issue
/// the moment anything was cancelled — and the receptionist reads that number aloud.
/// </summary>
public sealed record DoctorCard(
    long DoctorId, string Name, string? Room, int TodayCount, int NextSerial, int MaxSerials);

public sealed record QueueRow(
    long Id, int SerialNo, string PatientName, string Uhid, string? Phone,
    string DoctorName, string State, DateTimeOffset CreatedAt);


/// <summary>
/// §9A.2 module 2 (deliberately lite) / 05 §5 screen 9. The serial constraint is surfaced as
/// "next free is N" rather than as an error after the fact (ADR-0015 #1).
/// </summary>
[Authorize(Policy = Perm.AppointmentsRead)]
public class IndexModel(
    HmsTx tx, AppointmentsService appointments, SmsQueue sms,
    OrgIdentity hospital, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? PatientId { get; set; }
    [BindProperty] public long DoctorId { get; set; }

    public DateOnly Today { get; private set; }
    public IReadOnlyList<DoctorCard> Doctors { get; private set; } = [];
    public IReadOnlyList<QueueRow> Queue { get; private set; } = [];
    public string? SelectedPatientName { get; private set; }

    public int Waiting => Queue.Count(q => q.State is AppointmentState.Booked or AppointmentState.Arrived);
    public int Done => Queue.Count(q => q.State == AppointmentState.Done);

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var today = Today;

        (Doctors, Queue, SelectedPatientName) = await tx.RunAsync(async s =>
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
                    AppointmentsService.NextSerialFor(appts.Where(a => a.DoctorId == g.Key)),
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

            // Re-render after a failed post: echo the already-chosen patient (§7 U9 banner).
            var selectedName = PatientId is > 0
                ? await s.Reg.Patients.AsNoTracking()
                    .Where(p => p.Id == PatientId).Select(p => p.FullName + " — " + p.Uhid)
                    .FirstOrDefaultAsync()
                : null;

            return ((IReadOnlyList<DoctorCard>)byDoctor, (IReadOnlyList<QueueRow>)rows, selectedName);
        });
    }

    /// <summary>
    /// §12 splits reading the queue from writing to it: `appointments.read` is what puts the
    /// board on a screen, `appointments.create` is what lets a serial be issued against it.
    /// The page policy can only carry the coarser of the two, because a role may legitimately
    /// watch the queue without running it — so the mutating handlers carry the finer one, the
    /// same way `Lis/Board.cshtml.cs` splits the bench from the signatory.
    ///
    /// Until spec 0030 this permission was declared in Perm.cs, granted to the Receptionist in
    /// DevSeed.cs, and enforced nowhere: `appointments.read` alone could issue and advance
    /// serials. No seeded role could reach it — the Receptionist holds both — but §12 is
    /// editable data at /admin/users, so a view-only grant silently conferred write.
    /// </summary>
    private bool CanIssue => Can("appointments.create");

    public async Task<IActionResult> OnPostIssueAsync()
    {
        if (!CanIssue) return Forbid();
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

                await SmsSender.SendAsync(s, sms, BranchId,
                    Hms.Notifications.Data.SmsEvent.Appointment,
                    patient.Phone, new Dictionary<string, string?>
                    {
                        ["hospital"] = hospital.Name,
                        ["patient"] = patient.FullName,
                        ["serial"] = row.SerialNo.ToString(),
                        ["doctor"] = doctor?.DoctorName ?? "the doctor",
                    });
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
        // Advancing to Done, Cancelled or NoShow ends the appointment. It does NOT free the
        // number: a serial is consumed at issue, because the patient was told it by SMS the
        // moment it was allocated (spec 0032, M3-D1). Ending it is still a write to the queue,
        // so it needs the same grant as issuing.
        if (!CanIssue) return Forbid();
        try
        {
            await tx.RunAsync(s => appointments.AdvanceAsync(s.Appt, id, from, to));
            Toast(to switch
            {
                AppointmentState.InChamber => "Called in — patient is with the doctor",
                AppointmentState.Done => "Consultation finished",
                AppointmentState.Cancelled => "Serial cancelled",
                AppointmentState.NoShow => "Marked as a no-show — the next patient keeps their own serial",
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
