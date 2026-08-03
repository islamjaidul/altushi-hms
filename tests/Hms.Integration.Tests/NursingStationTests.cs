using Hms.Emr;
using Hms.Emr.Data;
using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0041 (R5 Nursing Station). What the database and the state machines guarantee for the
/// three new nursing writes: the Medicine Chart generated from a prescription, the care task,
/// and the ward duty assignment.
///
/// The clock is <b>fixed</b> for every generation test. Expansion depends on the Dhaka wall
/// clock — "how many doses does 1+0+1 for 3 days make" has a different answer at 07:00 and at
/// 21:00 — so a real clock would make this suite green only during part of the day, which is
/// the spec 0027 lesson.
/// </summary>
[Collection("postgres")]
public class NursingStationTests : IAsyncLifetime
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>00:30 Asia/Dhaka on 2026-08-03 — before every slot, so day one is never truncated.</summary>
    private static readonly DateTimeOffset EarlyMorning =
        new(2026, 8, 2, 18, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly Day1 = new(2026, 8, 3);
    private static DateTimeOffset DhakaUtc(DateOnly day, int hour)
        => new DateTimeOffset(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(6))
            .ToUniversalTime();

    private readonly PostgresFixture _pg;
    private readonly FixedClock _clock = new(EarlyMorning);

    private EmrService Emr() => new(new AuditWriter(_clock), _clock);
    private IpdService Ipd() => new(
        new NumberSeriesService(), new AuditWriter(_clock), new FiscalCalendar(7), _clock);

    public NursingStationTests(PostgresFixture pg) => _pg = pg;

    private EmrDbContext CreateEmr() => new(
        new DbContextOptionsBuilder<EmrDbContext>().UseNpgsql(_pg.ConnectionString,
            o => o.MigrationsHistoryTable("__ef_migrations", "emr"))
        .UseSnakeCaseNamingConvention().Options);

    private IpdDbContext CreateIpd() => new(
        new DbContextOptionsBuilder<IpdDbContext>().UseNpgsql(_pg.ConnectionString,
            o => o.MigrationsHistoryTable("__ef_migrations", "ipd"))
        .UseSnakeCaseNamingConvention().Options);

    public async Task InitializeAsync()
    {
        await using var emr = CreateEmr();
        await emr.Database.MigrateAsync();
        await using var ipd = CreateIpd();
        await ipd.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static long _admission = 40_000;
    private static long NextAdmission() => Interlocked.Increment(ref _admission);

    /// <summary>A signed indoor prescription with the given drug lines.</summary>
    private async Task<Note> SignedIndoorNoteAsync(
        EmrDbContext emr, long admissionId, params DrugLine[] drugs)
    {
        await using var kernel = _pg.CreateKernelContext();
        var note = await Emr().OpenDraftAsync(emr, 1, admissionId, null, admissionId, 7);
        await Emr().SaveDraftAsync(emr, note.Id,
            new NoteBody("Fever", null, "Viral fever", null, null), drugs);
        await Emr().FinaliseAsync(emr, kernel, note.Id, 1, 7, "Dr. Test");
        return note;
    }

    // ---- generation ------------------------------------------------------------------------

    [Fact]
    public async Task Generation_expands_the_prescription_into_dated_doses()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var admissionId = NextAdmission();
        var note = await SignedIndoorNoteAsync(emr, admissionId,
            new DrugLine(null, "Napa 500", "1 tab", "1+0+1", "3 days", "after food"));

        var result = await Emr().GenerateScheduleAsync(
            emr, kernel, 1, admissionId, note.Id, 9, "Nasrin");

        Assert.Equal(6, result.Inserted);
        Assert.Empty(result.Unreadable);

        var doses = await emr.MarDoses.AsNoTracking()
            .Where(d => d.AdmissionId == admissionId).OrderBy(d => d.ScheduledAt).ToListAsync();
        Assert.Equal(6, doses.Count);
        Assert.All(doses, d => Assert.Equal("Napa 500", d.DrugName));
        Assert.All(doses, d => Assert.NotNull(d.NoteDrugId));
        Assert.All(doses, d => Assert.Equal(DoseState.Scheduled, d.State));
        // 08:00 and 20:00 Dhaka on the first day, stored as UTC instants.
        Assert.Equal(DhakaUtc(Day1, 8), doses[0].ScheduledAt);
        Assert.Equal(DhakaUtc(Day1, 20), doses[1].ScheduledAt);
        Assert.Equal(DhakaUtc(Day1.AddDays(2), 20), doses[^1].ScheduledAt);
    }

    [Fact]
    public async Task Generating_twice_adds_nothing_the_second_time()
    {
        // The Generate button is on a screen a nurse can reload, and two nurses can press it at
        // once. Regeneration must be a no-op, not a doubled medicine chart.
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var admissionId = NextAdmission();
        var note = await SignedIndoorNoteAsync(emr, admissionId,
            new DrugLine(null, "Napa 500", "1 tab", "1+1+1", "2 days", null));

        var first = await Emr().GenerateScheduleAsync(emr, kernel, 1, admissionId, note.Id, 9, "Nasrin");
        var second = await Emr().GenerateScheduleAsync(emr, kernel, 1, admissionId, note.Id, 9, "Nasrin");

        Assert.Equal(6, first.Inserted);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(6, await emr.MarDoses.CountAsync(d => d.AdmissionId == admissionId));
    }

    [Fact]
    public async Task An_unreadable_frequency_is_reported_and_never_guessed()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var admissionId = NextAdmission();
        var note = await SignedIndoorNoteAsync(emr, admissionId,
            new DrugLine(null, "Napa 500", "1 tab", "1+0+1", "2 days", null),
            new DrugLine(null, "Omidon", "1 tab", "PRN", null, "if vomiting"));

        var result = await Emr().GenerateScheduleAsync(emr, kernel, 1, admissionId, note.Id, 9, "Nasrin");

        Assert.Equal(4, result.Inserted);
        Assert.Equal(["Omidon"], result.Unreadable);
        Assert.DoesNotContain(
            await emr.MarDoses.AsNoTracking().Where(d => d.AdmissionId == admissionId).ToListAsync(),
            d => d.DrugName == "Omidon");
    }

    [Fact]
    public async Task Two_drugs_sharing_a_slot_both_get_their_dose()
    {
        // The idempotency index is (note_drug_id, scheduled_at) — per prescription line, not per
        // instant. Two drugs at 08:00 is the normal case and must not collide.
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var admissionId = NextAdmission();
        var note = await SignedIndoorNoteAsync(emr, admissionId,
            new DrugLine(null, "Napa 500", "1 tab", "1+0+0", "1 day", null),
            new DrugLine(null, "Losectil 20", "1 cap", "1+0+0", "1 day", "before food"));

        var result = await Emr().GenerateScheduleAsync(emr, kernel, 1, admissionId, note.Id, 9, "Nasrin");

        Assert.Equal(2, result.Inserted);
        var doses = await emr.MarDoses.AsNoTracking()
            .Where(d => d.AdmissionId == admissionId).ToListAsync();
        Assert.Equal(2, doses.Count);
        Assert.Single(doses.Select(d => d.ScheduledAt).Distinct());
    }

    [Fact]
    public async Task An_unsigned_prescription_cannot_be_generated_from()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var admissionId = NextAdmission();
        var draft = await Emr().OpenDraftAsync(emr, 1, admissionId, null, admissionId, 7);
        await Emr().SaveDraftAsync(emr, draft.Id, new NoteBody("Fever", null, "Viral", null, null),
            [new DrugLine(null, "Napa 500", "1 tab", "1+0+1", "3 days", null)]);

        var refused = await Assert.ThrowsAsync<EmrException>(() =>
            Emr().GenerateScheduleAsync(emr, kernel, 1, admissionId, draft.Id, 9, "Nasrin"));
        Assert.Contains("sign", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_prescription_from_another_admission_is_refused()
    {
        // The note id arrives from the request. It identifies a row; it does not prove the row
        // belongs to the patient whose chart is open (security-guardrails §2).
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var mine = NextAdmission();
        var theirs = NextAdmission();
        var note = await SignedIndoorNoteAsync(emr, theirs,
            new DrugLine(null, "Napa 500", "1 tab", "1+0+1", "3 days", null));

        await Assert.ThrowsAsync<EmrException>(() =>
            Emr().GenerateScheduleAsync(emr, kernel, 1, mine, note.Id, 9, "Nasrin"));
        Assert.Empty(await emr.MarDoses.AsNoTracking().Where(d => d.AdmissionId == mine).ToListAsync());
    }

    [Fact]
    public async Task Generation_is_audited()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var admissionId = NextAdmission();
        var note = await SignedIndoorNoteAsync(emr, admissionId,
            new DrugLine(null, "Napa 500", "1 tab", "1+0+0", "2 days", null));

        await Emr().GenerateScheduleAsync(emr, kernel, 1, admissionId, note.Id, 9, "Nasrin");

        Assert.True(await kernel.AuditEvents.AsNoTracking()
            .AnyAsync(e => e.Action == "emr.mar.generated" && e.EntityId == note.Id));
    }

    [Fact]
    public async Task Doses_already_past_today_are_not_generated_overdue()
    {
        // Generating at 14:30 Dhaka must not create this morning's 08:00 dose — a dose born
        // overdue is a false accusation on the ward board.
        _clock.Now = new DateTimeOffset(2026, 8, 3, 8, 30, 0, TimeSpan.Zero);   // 14:30 Dhaka
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var admissionId = NextAdmission();
        var note = await SignedIndoorNoteAsync(emr, admissionId,
            new DrugLine(null, "Napa 500", "1 tab", "1+0+1", "2 days", null));

        var result = await Emr().GenerateScheduleAsync(emr, kernel, 1, admissionId, note.Id, 9, "Nasrin");

        Assert.Equal(3, result.Inserted);                     // 20:00 today, then both tomorrow
        var doses = await emr.MarDoses.AsNoTracking()
            .Where(d => d.AdmissionId == admissionId).OrderBy(d => d.ScheduledAt).ToListAsync();
        Assert.Equal(DhakaUtc(Day1, 20), doses[0].ScheduledAt);
        Assert.All(doses, d => Assert.True(d.ScheduledAt > _clock.Now));
        _clock.Now = EarlyMorning;
    }

    [Fact]
    public async Task A_generated_dose_is_administered_by_the_existing_single_shot_path()
    {
        // Generation must not create a second kind of dose: the row it writes is an ordinary
        // MarDose and goes through AdministerAsync's guard like a hand-scheduled one.
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var admissionId = NextAdmission();
        var note = await SignedIndoorNoteAsync(emr, admissionId,
            new DrugLine(null, "Napa 500", "1 tab", "1+0+0", "1 day", null));
        await Emr().GenerateScheduleAsync(emr, kernel, 1, admissionId, note.Id, 9, "Nasrin");
        var dose = await emr.MarDoses.AsNoTracking().FirstAsync(d => d.AdmissionId == admissionId);

        await Emr().AdministerAsync(emr, kernel, 1, dose.Id, DoseState.Missed, "Patient at OT", 9, "Nasrin");

        var twice = await Assert.ThrowsAsync<EmrException>(() => Emr().AdministerAsync(
            emr, kernel, 1, dose.Id, DoseState.Given, null, 9, "Nasrin"));
        Assert.Contains("already recorded", twice.Message);

        var stored = await emr.MarDoses.AsNoTracking().SingleAsync(d => d.Id == dose.Id);
        Assert.Equal(DoseState.Missed, stored.State);
        Assert.Equal("Patient at OT", stored.StateReason);   // LC-NUR-06: the reason is on the row
    }

    [Fact]
    public async Task A_dose_cannot_be_recorded_from_another_branch()
    {
        // Spec 0042 F9: the administer guard carried no branch predicate, so a branch-B operator
        // posting a branch-A DoseId recorded that dose — a cross-tenant clinical write. Bulk
        // generation (0041) made dose ids sequential and guessable, which is what promoted this
        // from latent to likely.
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var dose = await Emr().ScheduleDoseAsync(emr, 1, NextAdmission(), "Napa", "1 tab", null,
            _clock.Now.AddHours(1), 9);

        var refused = await Assert.ThrowsAsync<EmrException>(() => Emr().AdministerAsync(
            emr, kernel, branchId: 2, dose.Id, DoseState.Given, null, 9, "Foreign Nurse"));
        Assert.Contains("already recorded", refused.Message);

        var stored = await emr.MarDoses.AsNoTracking().SingleAsync(d => d.Id == dose.Id);
        Assert.Equal(DoseState.Scheduled, stored.State);      // the row did not move
    }

    // ---- care tasks ------------------------------------------------------------------------

    [Fact]
    public async Task A_task_needs_a_title()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();

        await Assert.ThrowsAsync<EmrException>(() => Emr().CreateTaskAsync(
            emr, kernel, 1, NextAdmission(), "   ", null, null, null, 9, "Nasrin"));
    }

    [Fact]
    public async Task A_task_is_completed_once_and_the_completion_is_attributable()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var task = await Emr().CreateTaskAsync(emr, kernel, 1, NextAdmission(),
            "Turn the patient", "Left side", "positioning", _clock.Now.AddHours(2), 9, "Nasrin");

        await Emr().CompleteTaskAsync(emr, kernel, 1, task.Id, 9, "Nasrin");

        var twice = await Assert.ThrowsAsync<EmrException>(() =>
            Emr().CompleteTaskAsync(emr, kernel, 1, task.Id, 9, "Nasrin"));
        Assert.Contains("already", twice.Message, StringComparison.OrdinalIgnoreCase);

        var stored = await emr.CareTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskState.Done, stored.State);
        Assert.Equal((long?)9, stored.CompletedBy);
        Assert.NotNull(stored.CompletedAt);
    }

    [Fact]
    public async Task Cancelling_a_task_requires_a_reason()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var task = await Emr().CreateTaskAsync(emr, kernel, 1, NextAdmission(),
            "Remove cannula", null, null, null, 9, "Nasrin");

        await Assert.ThrowsAsync<EmrException>(() =>
            Emr().CancelTaskAsync(emr, kernel, 1, task.Id, "  ", 9, "Nasrin"));

        await Emr().CancelTaskAsync(emr, kernel, 1, task.Id, "Doctor deferred it", 9, "Nasrin");
        var stored = await emr.CareTasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal(TaskState.Cancelled, stored.State);
        Assert.Equal("Doctor deferred it", stored.StateReason);
    }

    [Fact]
    public async Task A_completed_task_cannot_then_be_cancelled()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var task = await Emr().CreateTaskAsync(emr, kernel, 1, NextAdmission(),
            "Pre-op shave", null, null, null, 9, "Nasrin");
        await Emr().CompleteTaskAsync(emr, kernel, 1, task.Id, 9, "Nasrin");

        await Assert.ThrowsAsync<EmrException>(() =>
            Emr().CancelTaskAsync(emr, kernel, 1, task.Id, "changed my mind", 9, "Nasrin"));
    }

    [Fact]
    public async Task The_database_refuses_a_closed_task_with_no_closing_stamp()
    {
        // The service is not the only writer that will ever exist; the CHECK is the floor.
        await using var emr = CreateEmr();
        emr.CareTasks.Add(new CareTask
        {
            BranchId = 1, AdmissionId = NextAdmission(), Title = "Bad row",
            State = TaskState.Done, CreatedAt = _clock.Now, CreatedBy = 9,
        });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => emr.SaveChangesAsync());
    }

    // ---- ward duty -------------------------------------------------------------------------

    [Fact]
    public async Task A_duty_assignment_is_recorded_and_a_duplicate_is_refused()
    {
        await using var ipd = CreateIpd();
        await using var kernel = _pg.CreateKernelContext();
        var bed = await IpdSeed.BedAsync(ipd);

        var duty = await Ipd().AssignDutyAsync(ipd, kernel, 1, bed.WardId, Day1,
            DutyShift.Morning, DutyRole.Nurse, null, "Salma Khatun", 9, "Nasrin");

        Assert.True(duty.Active);
        var again = await Assert.ThrowsAsync<IpdException>(() => Ipd().AssignDutyAsync(
            ipd, kernel, 1, bed.WardId, Day1, DutyShift.Morning, DutyRole.Nurse, null,
            "Salma Khatun", 9, "Nasrin"));
        Assert.Contains("already", again.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_same_person_can_be_reassigned_after_the_assignment_is_ended()
    {
        // The unique index is partial on `active` precisely so a corrected roster is possible
        // without deleting the history of who was on the ward (hard rule 4).
        await using var ipd = CreateIpd();
        await using var kernel = _pg.CreateKernelContext();
        var bed = await IpdSeed.BedAsync(ipd);
        var first = await Ipd().AssignDutyAsync(ipd, kernel, 1, bed.WardId, Day1,
            DutyShift.Night, DutyRole.Aya, null, "Rina Begum", 9, "Nasrin");

        await Ipd().EndDutyAsync(ipd, kernel, 1, first.Id, "Swapped with the evening aya", 9, "Nasrin");
        var second = await Ipd().AssignDutyAsync(ipd, kernel, 1, bed.WardId, Day1,
            DutyShift.Night, DutyRole.Aya, null, "Rina Begum", 9, "Nasrin");

        Assert.NotEqual(first.Id, second.Id);
        var rows = await ipd.DutyAssignments.AsNoTracking()
            .Where(d => d.WardId == bed.WardId).ToListAsync();
        Assert.Equal(2, rows.Count);                                  // history kept, not overwritten
        Assert.Single(rows, d => d.Active);
        Assert.Equal("Swapped with the evening aya", rows.Single(d => !d.Active).EndedReason);
    }

    [Fact]
    public async Task Ending_a_duty_needs_a_reason_and_happens_once()
    {
        await using var ipd = CreateIpd();
        await using var kernel = _pg.CreateKernelContext();
        var bed = await IpdSeed.BedAsync(ipd);
        var duty = await Ipd().AssignDutyAsync(ipd, kernel, 1, bed.WardId, Day1,
            DutyShift.Evening, DutyRole.WardBoy, null, "Jashim Uddin", 9, "Nasrin");

        await Assert.ThrowsAsync<IpdException>(() =>
            Ipd().EndDutyAsync(ipd, kernel, 1, duty.Id, null, 9, "Nasrin"));

        await Ipd().EndDutyAsync(ipd, kernel, 1, duty.Id, "Went home sick", 9, "Nasrin");
        var twice = await Assert.ThrowsAsync<IpdException>(() =>
            Ipd().EndDutyAsync(ipd, kernel, 1, duty.Id, "Went home sick", 9, "Nasrin"));
        Assert.Contains("already", twice.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("afternoon", DutyRole.Nurse)]
    [InlineData(DutyShift.Morning, "matron")]
    public async Task Only_the_ward_vocabulary_is_accepted(string shift, string role)
    {
        await using var ipd = CreateIpd();
        await using var kernel = _pg.CreateKernelContext();
        var bed = await IpdSeed.BedAsync(ipd);

        await Assert.ThrowsAsync<IpdException>(() => Ipd().AssignDutyAsync(
            ipd, kernel, 1, bed.WardId, Day1, shift, role, null, "Someone", 9, "Nasrin"));
    }

    [Fact]
    public async Task A_duty_assignment_needs_a_real_ward_and_a_name()
    {
        await using var ipd = CreateIpd();
        await using var kernel = _pg.CreateKernelContext();
        var bed = await IpdSeed.BedAsync(ipd);

        await Assert.ThrowsAsync<IpdException>(() => Ipd().AssignDutyAsync(
            ipd, kernel, 1, 999_999, Day1, DutyShift.Morning, DutyRole.Nurse, null, "Ghost", 9, "Nasrin"));
        await Assert.ThrowsAsync<IpdException>(() => Ipd().AssignDutyAsync(
            ipd, kernel, 1, bed.WardId, Day1, DutyShift.Morning, DutyRole.Nurse, null, "   ", 9, "Nasrin"));
    }

    [Fact]
    public async Task Duty_writes_are_audited()
    {
        await using var ipd = CreateIpd();
        await using var kernel = _pg.CreateKernelContext();
        var bed = await IpdSeed.BedAsync(ipd);
        var duty = await Ipd().AssignDutyAsync(ipd, kernel, 1, bed.WardId, Day1,
            DutyShift.Morning, DutyRole.Nurse, null, "Audited Nurse", 9, "Nasrin");
        await Ipd().EndDutyAsync(ipd, kernel, 1, duty.Id, "Shift changed", 9, "Nasrin");

        var actions = await kernel.AuditEvents.AsNoTracking()
            .Where(e => e.EntityId == duty.Id && e.Entity == "ipd.duty_assignment")
            .Select(e => e.Action).ToListAsync();
        Assert.Contains("ipd.duty.assigned", actions);
        Assert.Contains("ipd.duty.ended", actions);
    }
}
