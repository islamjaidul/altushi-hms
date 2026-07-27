using Hms.Emr;
using Hms.Emr.Data;
using Hms.Kernel.Audit;
using Microsoft.EntityFrameworkCore;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0024 (§5 M5, §5A-7). What is tested here is what the database and the state machine
/// guarantee — immutability after signing, supersession as the only correction, the MAR's
/// single-shot administration, and the XOR that keeps a note attached to exactly one visit.
/// </summary>
[Collection("postgres")]
public class EmrTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly EmrService _emr = new(new AuditWriter(TimeProvider.System), TimeProvider.System);

    public EmrTests(PostgresFixture pg) => _pg = pg;

    private EmrDbContext CreateEmr() => new(
        new DbContextOptionsBuilder<EmrDbContext>().UseNpgsql(_pg.ConnectionString,
            o => o.MigrationsHistoryTable("__ef_migrations", "emr"))
        .UseSnakeCaseNamingConvention().Options);

    public async Task InitializeAsync()
    {
        await using var emr = CreateEmr();
        await emr.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static long _encounter = 9_000;
    private static long NextEncounter() => Interlocked.Increment(ref _encounter);

    [Fact]
    public async Task Draft_is_resumed_not_duplicated()
    {
        await using var emr = CreateEmr();
        var encounterId = NextEncounter();

        var first = await _emr.OpenDraftAsync(emr, 1, 1, encounterId, null, 7);
        var second = await _emr.OpenDraftAsync(emr, 1, 1, encounterId, null, 7);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task A_note_belongs_to_exactly_one_parent()
    {
        await using var emr = CreateEmr();

        await Assert.ThrowsAsync<EmrException>(() =>
            _emr.OpenDraftAsync(emr, 1, 1, NextEncounter(), 5, 7));
        await Assert.ThrowsAsync<EmrException>(() =>
            _emr.OpenDraftAsync(emr, 1, 1, null, null, 7));
    }

    [Fact]
    public async Task Signing_makes_the_note_immutable()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var note = await _emr.OpenDraftAsync(emr, 1, 1, NextEncounter(), null, 7);

        await _emr.SaveDraftAsync(emr, note.Id,
            new NoteBody("Fever", null, "Viral fever", "Rest", null),
            [new DrugLine(null, "Napa 500", "1 tab", "1+1+1", "3 days", null)]);
        await _emr.FinaliseAsync(emr, kernel, note.Id, 1, 7, "Dr. Test");

        var refused = await Assert.ThrowsAsync<EmrException>(() =>
            _emr.SaveDraftAsync(emr, note.Id, new NoteBody("Changed", null, null, null, null), []));
        Assert.Contains("already signed", refused.Message);
    }

    [Fact]
    public async Task Signing_twice_is_refused_rather_than_silently_repeated()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var note = await _emr.OpenDraftAsync(emr, 1, 1, NextEncounter(), null, 7);
        await _emr.SaveDraftAsync(emr, note.Id,
            new NoteBody("Cough", null, "URTI", null, null), []);

        await _emr.FinaliseAsync(emr, kernel, note.Id, 1, 7, "Dr. Test");
        var again = await Assert.ThrowsAsync<EmrException>(() =>
            _emr.FinaliseAsync(emr, kernel, note.Id, 1, 7, "Dr. Test"));
        Assert.Contains("already signed", again.Message);
    }

    [Fact]
    public async Task An_empty_note_cannot_be_signed()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var note = await _emr.OpenDraftAsync(emr, 1, 1, NextEncounter(), null, 7);

        await Assert.ThrowsAsync<EmrException>(() =>
            _emr.FinaliseAsync(emr, kernel, note.Id, 1, 7, "Dr. Test"));
    }

    [Fact]
    public async Task Correction_supersedes_and_carries_the_drugs_forward()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var note = await _emr.OpenDraftAsync(emr, 1, 1, NextEncounter(), null, 7);
        await _emr.SaveDraftAsync(emr, note.Id,
            new NoteBody("Fever", null, "Viral fever", "Rest", null),
            [new DrugLine(null, "Napa 500", "1 tab", "1+1+1", "3 days", "after food")]);
        await _emr.FinaliseAsync(emr, kernel, note.Id, 1, 7, "Dr. Test");

        var replacement = await _emr.SupersedeAsync(emr, kernel, note.Id, 1, 7, "Dr. Test");

        var original = await emr.Notes.AsNoTracking().SingleAsync(n => n.Id == note.Id);
        Assert.Equal(NoteState.Superseded, original.State);
        Assert.Equal("Viral fever", original.Diagnosis);          // the record is untouched
        Assert.Equal(note.Id, replacement.SupersedesId);
        Assert.Equal(NoteState.Draft, replacement.State);

        var carried = await emr.NoteDrugs.AsNoTracking()
            .Where(d => d.NoteId == replacement.Id).ToListAsync();
        Assert.Single(carried);
        Assert.Equal("Napa 500", carried[0].DrugName);
    }

    [Fact]
    public async Task Only_a_signed_note_can_be_corrected()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var note = await _emr.OpenDraftAsync(emr, 1, 1, NextEncounter(), null, 7);

        await Assert.ThrowsAsync<EmrException>(() =>
            _emr.SupersedeAsync(emr, kernel, note.Id, 1, 7, "Dr. Test"));
    }

    [Fact]
    public async Task The_same_note_cannot_be_corrected_twice()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var note = await _emr.OpenDraftAsync(emr, 1, 1, NextEncounter(), null, 7);
        await _emr.SaveDraftAsync(emr, note.Id, new NoteBody("Pain", null, "Sprain", null, null), []);
        await _emr.FinaliseAsync(emr, kernel, note.Id, 1, 7, "Dr. Test");
        await _emr.SupersedeAsync(emr, kernel, note.Id, 1, 7, "Dr. Test");

        // The second attempt loses on the unique index, and says so comprehensibly.
        await Assert.ThrowsAnyAsync<Exception>(() =>
            _emr.SupersedeAsync(emr, kernel, note.Id, 1, 7, "Dr. Test"));
    }

    [Fact]
    public async Task Vitals_need_at_least_one_reading_and_a_sane_blood_pressure()
    {
        await using var emr = CreateEmr();
        var encounterId = NextEncounter();

        await Assert.ThrowsAsync<EmrException>(() => _emr.RecordVitalsAsync(
            emr, 1, 1, encounterId, null, null, null, null, null, null, null, 3));

        var backwards = await Assert.ThrowsAsync<EmrException>(() => _emr.RecordVitalsAsync(
            emr, 1, 1, encounterId, null, 80, 120, null, null, null, null, 3));
        Assert.Contains("higher than diastolic", backwards.Message);

        var ok = await _emr.RecordVitalsAsync(
            emr, 1, 1, encounterId, null, 138, 86, 82, 374, 715, 97, 3);
        Assert.Equal((short?)374, ok.TemperatureTenthsC);       // 37.4 °C round-trips exactly
    }

    [Fact]
    public async Task A_dose_is_recorded_once_and_a_missed_dose_needs_a_reason()
    {
        await using var emr = CreateEmr();
        await using var kernel = _pg.CreateKernelContext();
        var dose = await _emr.ScheduleDoseAsync(emr, 1, 42, "Ceftriaxone", "1 g", "IV",
            DateTimeOffset.UtcNow, 9);

        await Assert.ThrowsAsync<EmrException>(() => _emr.AdministerAsync(
            emr, kernel, 1, dose.Id, DoseState.Missed, null, 9, "Nurse"));

        await _emr.AdministerAsync(emr, kernel, 1, dose.Id, DoseState.Given, null, 9, "Nurse");

        var twice = await Assert.ThrowsAsync<EmrException>(() => _emr.AdministerAsync(
            emr, kernel, 1, dose.Id, DoseState.Given, null, 9, "Nurse"));
        Assert.Contains("already recorded", twice.Message);

        var stored = await emr.MarDoses.AsNoTracking().SingleAsync(d => d.Id == dose.Id);
        Assert.Equal(DoseState.Given, stored.State);
        Assert.Equal((long?)9, stored.AdministeredBy);
        Assert.NotNull(stored.AdministeredAt);
    }

    [Fact]
    public async Task A_handover_is_one_record_per_admission()
    {
        await using var emr = CreateEmr();
        var first = await _emr.RecordHandoverAsync(emr, 1, 77, "OPD", "Stable", "One bag", 9);
        var second = await _emr.RecordHandoverAsync(emr, 1, 77, "Emergency", "Drowsy", null, 9);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("Emergency", second.ReceivedFrom);
        Assert.Single(await emr.ReceiveNotes.AsNoTracking().Where(r => r.AdmissionId == 77).ToListAsync());
    }

    [Fact]
    public async Task A_template_applies_its_drug_lines()
    {
        await using var emr = CreateEmr();
        var saved = await _emr.SaveTemplateAsync(emr, 7, "URTI adult",
            new NoteBody("Sore throat", "Throat congested", "Acute pharyngitis", "Gargle", null),
            [new DrugLine(null, "Napa 500", "1 tab", "1+1+1", "5 days", null)]);

        var lines = EmrService.TemplateDrugs(saved);
        Assert.Single(lines);
        Assert.Equal("Napa 500", lines[0].DrugName);

        // Saving the same name again replaces it rather than growing a second copy.
        await _emr.SaveTemplateAsync(emr, 7, "URTI adult",
            new NoteBody("Sore throat", null, "Pharyngitis", null, null), []);
        Assert.Single(await emr.Templates.AsNoTracking()
            .Where(t => t.DoctorId == 7 && t.Name == "URTI adult").ToListAsync());
    }
}
