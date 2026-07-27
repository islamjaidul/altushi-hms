using Hms.Appointments.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Public;

public sealed record QueueRow(
    string Doctor, string? Room, int? InChamberSerial, string? InChamberName, int Waiting, int Done);

/// <summary>
/// R3 (spec 0019): the lobby-TV queue monitor. Unauthenticated by design and read-only by
/// construction — no form, no handler, meta-refresh so a JS-less TV browser stays current.
/// Content per the P15 default: doctor, serial, masked name. Never an amount or a diagnosis.
/// </summary>
[AllowAnonymous]
public class QueueModel(HmsTx tx, TimeProvider clock) : PageModel
{
    public IReadOnlyList<QueueRow> Rows { get; private set; } = [];
    public DateOnly Today { get; private set; }

    public async Task OnGetAsync()
    {
        Today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        await tx.RunAsync(async s =>
        {
            var schedules = await s.Appt.Schedules.AsNoTracking().ToListAsync();
            var todays = await s.Appt.Appointments.AsNoTracking()
                .Where(a => a.OnDate == Today).ToListAsync();
            var patientIds = todays
                .Where(a => a.State == AppointmentState.InChamber)
                .Select(a => a.PatientId).Distinct().ToList();
            var names = await s.Reg.Patients.AsNoTracking()
                .Where(p => patientIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.FullName);

            Rows = schedules.GroupBy(x => x.DoctorId).Select(g =>
            {
                var mine = todays.Where(a => a.DoctorId == g.Key).ToList();
                var inChamber = mine.FirstOrDefault(a => a.State == AppointmentState.InChamber);
                return new QueueRow(
                    g.First().DoctorName, g.First().Room,
                    inChamber?.SerialNo,
                    inChamber is not null
                        ? Ui.MaskName(names.GetValueOrDefault(inChamber.PatientId, ""))
                        : null,
                    mine.Count(a => a.State is AppointmentState.Booked or AppointmentState.Arrived),
                    mine.Count(a => a.State == AppointmentState.Done));
            }).OrderBy(r => r.Doctor).ToList();
            return 0;
        });
    }
}
