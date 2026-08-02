using Hms.Appointments.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Appointments;

public sealed class AppointmentsException(string message) : Exception(message);

public sealed class AppointmentsService(TimeProvider clock)
{
    /// <summary>
    /// The number the next patient will be given for this doctor-day — <b>the</b> definition, so
    /// that the queue board and the allocator cannot drift apart (spec 0032, M3-D2).
    ///
    /// The board promises "next serial N" before the fact rather than reporting a collision after
    /// it (ADR-0015 #1). That promise is only worth anything if the number offered is the number
    /// issued: the receptionist reads it aloud to the patient standing at the counter, and the
    /// issue-time SMS then sends whatever the allocator actually chose. When the label counted
    /// non-cancelled rows while the allocator maxed over all of them, one cancellation was enough
    /// to make the screen and the SMS name two different serials.
    ///
    /// <paramref name="doctorDay"/> must be <b>every</b> appointment for that doctor and date,
    /// cancelled and no-show included — a consumed serial is never handed out again.
    /// </summary>
    public static int NextSerialFor(IEnumerable<Appointment> doctorDay)
        => NextSerialAfter(doctorDay.Select(a => (int?)a.SerialNo).Max());

    /// <summary>As <see cref="NextSerialFor"/>, from a highest-issued serial the caller already
    /// has (a SQL <c>MAX</c>). Null means the doctor-day is empty.</summary>
    public static int NextSerialAfter(int? highestIssued) => (highestIssued ?? 0) + 1;

    /// <summary>
    /// Issues the next serial for the doctor/day. Constraint-backed (ADR-0015 #1): a concurrent
    /// taker causes a retry on the next number, never a duplicate and never a user-facing crash.
    ///
    /// <b>A serial, once issued, is consumed.</b> The allocation reads EVERY row for the
    /// doctor-day — cancelled and no-show included — because the unique index
    /// <c>(doctor_id, on_date, serial_no)</c> has no state filter either, and the two must agree.
    /// Excluding cancelled rows here (spec 0032, M3-D1) meant that cancelling the day's top
    /// serial made the allocator re-propose a number a cancelled row still held: it collided,
    /// retried the identical computation five times, and then blamed concurrency for something
    /// no retry could ever fix. Cancel is one click on the queue board, so a doctor's counter
    /// stopped until midnight.
    ///
    /// Reuse is not the alternative. The issue-time SMS has already told the first patient their
    /// number (`Appointments/Index.cshtml.cs`), so handing it to a second patient puts two people
    /// in the corridor holding "serial 5". Nothing reads a serial expecting reuse: it is
    /// displayed, ordered by, shown on the public board, and put in that SMS.
    /// </summary>
    public async Task<Appointment> IssueSerialAsync(
        ApptDbContext appt, long branchId, long patientId, long doctorId, DateOnly onDate,
        long actorId, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var next = NextSerialAfter(await appt.Appointments
                .Where(a => a.DoctorId == doctorId && a.OnDate == onDate)
                .Select(a => (int?)a.SerialNo).MaxAsync(ct));

            var row = new Appointment
            {
                BranchId = branchId, PatientId = patientId, DoctorId = doctorId,
                OnDate = onDate, SerialNo = next,
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

    /// <summary>
    /// The moves the queue board actually offers (spec 0039 WP1, AUD-VAL-11). Both <c>from</c>
    /// and <c>to</c> arrive as hidden form fields, so without this table the UPDATE below would
    /// write any string the browser sends — "deleted" included — into the state column.
    /// </summary>
    private static readonly Dictionary<string, string[]> LegalMoves = new()
    {
        [AppointmentState.Booked] =
            [AppointmentState.Arrived, AppointmentState.InChamber,
             AppointmentState.NoShow, AppointmentState.Cancelled],
        [AppointmentState.Arrived] =
            [AppointmentState.InChamber, AppointmentState.NoShow, AppointmentState.Cancelled],
        [AppointmentState.InChamber] = [AppointmentState.Done],
    };

    public async Task AdvanceAsync(ApptDbContext appt, long id, string from, string to, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to) ||
            !LegalMoves.TryGetValue(from, out var allowed) || !allowed.Contains(to))
            throw new AppointmentsException(
                "That is not a move this queue can make — refresh the board and use its buttons.");

        var affected = await appt.Database.ExecuteSqlAsync($"""
            UPDATE appt.appointment SET state = {to} WHERE id = {id} AND state = {from}
            """, ct);
        if (affected == 0) throw new AppointmentsException("Appointment already moved on — refresh (edge 28).");
    }
}
