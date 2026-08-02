using Hms.Appointments;
using Hms.Appointments.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// M3's serial allocator (spec 0032, defect M3-D1). The rule under test is one sentence:
/// <b>a serial, once issued, is consumed</b> — ending a serial (done, cancelled, no-show) never
/// puts its number back in the pool.
///
/// It has to be a Postgres test: the whole allocator is a race between an in-memory
/// <c>max(serial_no) + 1</c> and the unique index <c>(doctor_id, on_date, serial_no)</c>, and an
/// in-memory provider would pass a version of this that a real database refuses.
/// </summary>
[Collection("postgres")]
public class AppointmentQueueTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly AppointmentsService _svc = new(TimeProvider.System);

    public AppointmentQueueTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var appt = CreateAppt(null);
        await appt.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ApptDbContext CreateAppt(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<ApptDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "appt"))
            : new DbContextOptionsBuilder<ApptDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "appt")))
        .UseSnakeCaseNamingConvention().Options);

    /// <summary>A doctor and a day nobody else in the suite is queueing against. The doctor is
    /// a real appt.doctor row — spec 0039's FK ended invented doctor ids.</summary>
    private async Task<(long DoctorId, DateOnly OnDate)> OwnQueueAsync()
    {
        await using var appt = CreateAppt(null);
        var doctor = new Doctor { Name = $"Dr Test {Random.Shared.Next(1_000_000, 9_999_999)}" };
        appt.Doctors.Add(doctor);
        await appt.SaveChangesAsync();
        return (doctor.Id, new DateOnly(2026, 8, 1));
    }

    // -----------------------------------------------------------------------
    // LC-QUE-05 — the serial after an ended one
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Cancelling_the_days_top_serial_does_not_block_the_next_one()
    {
        // The two-click repro from the QA sweep: issue 1, cancel 1, issue again. Before the fix
        // the allocator re-proposed 1 (its max query skipped the cancelled row, the unique index
        // did not), collided five times and told the operator the queue was "moving fast".
        var (doctorId, onDate) = await OwnQueueAsync();
        await using var appt = CreateAppt(null);

        var first = await _svc.IssueSerialAsync(appt, 1, patientId: 11, doctorId, onDate, actorId: 7);
        Assert.Equal(1, first.SerialNo);

        await _svc.AdvanceAsync(appt, first.Id, AppointmentState.Booked, AppointmentState.Cancelled);

        var second = await _svc.IssueSerialAsync(appt, 1, patientId: 12, doctorId, onDate, actorId: 7);
        Assert.Equal(2, second.SerialNo);                  // 2, not "try again" and not a re-used 1
    }

    [Fact]
    public async Task A_no_show_serial_is_never_handed_to_a_second_patient()
    {
        // The other half of LC-QUE-05, and the reason reuse cannot be the fix: the issue-time SMS
        // has already told patient 21 that they are serial 1.
        var (doctorId, onDate) = await OwnQueueAsync();
        await using var appt = CreateAppt(null);

        var first = await _svc.IssueSerialAsync(appt, 1, patientId: 21, doctorId, onDate, actorId: 7);
        await _svc.AdvanceAsync(appt, first.Id, AppointmentState.Booked, AppointmentState.NoShow);

        var second = await _svc.IssueSerialAsync(appt, 1, patientId: 22, doctorId, onDate, actorId: 7);
        Assert.NotEqual(first.SerialNo, second.SerialNo);
        Assert.Equal(2, second.SerialNo);
    }

    [Fact]
    public async Task Every_serial_ever_issued_for_a_doctor_day_stays_distinct()
    {
        // Twelve patients through a queue that is cancelled every other time — the shape of a
        // real morning. What must hold is that no number is ever handed out twice, whatever
        // happened to the appointment that first took it.
        var (doctorId, onDate) = await OwnQueueAsync();
        await using var appt = CreateAppt(null);

        for (var i = 0; i < 12; i++)
        {
            var row = await _svc.IssueSerialAsync(appt, 1, 100 + i, doctorId, onDate, 7);
            if (i % 2 == 0)
                await _svc.AdvanceAsync(appt, row.Id, AppointmentState.Booked, AppointmentState.Cancelled);
        }

        await using var fresh = CreateAppt(null);
        var serials = await fresh.Appointments
            .Where(a => a.DoctorId == doctorId && a.OnDate == onDate)
            .Select(a => a.SerialNo).ToListAsync();
        Assert.Equal(12, serials.Count);
        Assert.Equal(12, serials.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 12), serials.Order());
    }

    // -----------------------------------------------------------------------
    // M3-D2 — the number the board offers is the number the patient receives
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_next_serial_the_board_offers_is_the_one_the_next_patient_gets()
    {
        // The queue board promises "next serial N" BEFORE the fact (ADR-0015 #1), and the
        // receptionist reads that number aloud while the patient is standing there. The label was
        // computed from a count of non-cancelled rows while the allocator maxed over all of them,
        // so one cancellation made the screen and the issue-time SMS name two different serials.
        // Both now come from `AppointmentsService.NextSerialFor`.
        var (doctorId, onDate) = await OwnQueueAsync();
        await using var appt = CreateAppt(null);

        // An empty doctor-day offers 1.
        Assert.Equal(1, AppointmentsService.NextSerialFor(
            await DoctorDayAsync(doctorId, onDate)));

        // 1, 2, 3 with the middle one cancelled — the shape that broke the label.
        var issued = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var offered = AppointmentsService.NextSerialFor(await DoctorDayAsync(doctorId, onDate));
            var row = await _svc.IssueSerialAsync(appt, 1, 50 + i, doctorId, onDate, 7);
            Assert.Equal(offered, row.SerialNo);              // offered == issued, every time
            issued.Add(row.SerialNo);
            if (i == 1)
                await _svc.AdvanceAsync(appt, row.Id, AppointmentState.Booked, AppointmentState.Cancelled);
        }
        Assert.Equal([1, 2, 3], issued);

        // …and after the cancellation the board offers 4, not the "3" a non-cancelled count gives.
        var next = AppointmentsService.NextSerialFor(await DoctorDayAsync(doctorId, onDate));
        Assert.Equal(4, next);
        var last = await _svc.IssueSerialAsync(appt, 1, 60, doctorId, onDate, 7);
        Assert.Equal(next, last.SerialNo);
    }

    /// <summary>Every row for a doctor-day, the way the queue board loads them.</summary>
    private async Task<List<Appointment>> DoctorDayAsync(long doctorId, DateOnly onDate)
    {
        await using var fresh = CreateAppt(null);
        return await fresh.Appointments.AsNoTracking()
            .Where(a => a.DoctorId == doctorId && a.OnDate == onDate).ToListAsync();
    }

    // -----------------------------------------------------------------------
    // LC-QUE-07 — the retry loop must still do its real job
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Two_counters_issuing_at_once_get_two_different_serials()
    {
        // The allocator's retry exists for this, not for cancelled rows. Both callers read the
        // same max, both propose the same number, the unique index refuses one — and the loser
        // must recompute and land on the next free number rather than fail the operator.
        var (doctorId, onDate) = await OwnQueueAsync();

        await using var connA = new NpgsqlConnection(_pg.ConnectionString);
        await using var connB = new NpgsqlConnection(_pg.ConnectionString);
        await connA.OpenAsync();
        await connB.OpenAsync();
        await using var apptA = CreateAppt(connA);
        await using var apptB = CreateAppt(connB);

        var issued = await Task.WhenAll(
            _svc.IssueSerialAsync(apptA, 1, 31, doctorId, onDate, 7),
            _svc.IssueSerialAsync(apptB, 1, 32, doctorId, onDate, 7));

        Assert.Equal([1, 2], issued.Select(a => a.SerialNo).Order());
    }

    [Fact]
    public async Task A_serial_still_advances_only_from_the_state_it_was_in()
    {
        // M3-G1: `AdvanceAsync` is a state-guarded UPDATE, so the second click on a stale queue
        // board loses in words instead of overwriting the first (edge 28).
        var (doctorId, onDate) = await OwnQueueAsync();
        await using var appt = CreateAppt(null);

        var row = await _svc.IssueSerialAsync(appt, 1, 41, doctorId, onDate, 7);
        await _svc.AdvanceAsync(appt, row.Id, AppointmentState.Booked, AppointmentState.InChamber);

        var stale = await Assert.ThrowsAsync<AppointmentsException>(
            () => _svc.AdvanceAsync(appt, row.Id, AppointmentState.Booked, AppointmentState.InChamber));
        Assert.Contains("already moved on", stale.Message);

        await using var fresh = CreateAppt(null);
        Assert.Equal(AppointmentState.InChamber,
            (await fresh.Appointments.SingleAsync(a => a.Id == row.Id)).State);
    }
}
