using Hms.Appointments.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Appointments;

public sealed class AppointmentsException(string message) : Exception(message);

public sealed class AppointmentsService(TimeProvider clock)
{
    /// <summary>
    /// Issues the next serial for the doctor/day. Constraint-backed (ADR-0015 #1): a concurrent
    /// taker causes a retry on the next number, never a duplicate and never a user-facing crash.
    /// </summary>
    public async Task<Appointment> IssueSerialAsync(
        ApptDbContext appt, long branchId, long patientId, long doctorId, DateOnly onDate,
        long actorId, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var next = await appt.Appointments
                .Where(a => a.DoctorId == doctorId && a.OnDate == onDate && a.State != AppointmentState.Cancelled)
                .Select(a => (int?)a.SerialNo).MaxAsync(ct) ?? 0;

            var row = new Appointment
            {
                BranchId = branchId, PatientId = patientId, DoctorId = doctorId,
                OnDate = onDate, SerialNo = next + 1,
                CreatedAt = clock.GetUtcNow(), CreatedBy = actorId,
            };
            appt.Appointments.Add(row);
            try
            {
                await appt.SaveChangesAsync(ct);
                return row;
            }
            catch (DbUpdateException)
            {
                appt.Entry(row).State = Microsoft.EntityFrameworkCore.EntityState.Detached;   // serial just taken — retry allocation (edge 28)
            }
        }
        throw new AppointmentsException("Could not allocate a serial — the queue is moving fast, try again.");
    }

    public async Task AdvanceAsync(ApptDbContext appt, long id, string from, string to, CancellationToken ct = default)
    {
        var affected = await appt.Database.ExecuteSqlAsync($"""
            UPDATE appt.appointment SET state = {to} WHERE id = {id} AND state = {from}
            """, ct);
        if (affected == 0) throw new AppointmentsException("Appointment already moved on — refresh (edge 28).");
    }
}
