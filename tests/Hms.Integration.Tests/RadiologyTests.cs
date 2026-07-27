using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Radiology;
using Hms.Radiology.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0026 (§5 M10). Small on purpose: a radiology report is a result, and M9's tests already
/// own results. What is new here is the study — that it is created exactly once per order test,
/// that "done" is single-shot, and that a test maps to at most one machine.
/// </summary>
[Collection("postgres")]
public class RadiologyTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RadiologyService _radiology =
        new(new AuditWriter(TimeProvider.System), TimeProvider.System);

    public RadiologyTests(PostgresFixture pg) => _pg = pg;

    private RadiologyDbContext CreateRadiology() => new(
        new DbContextOptionsBuilder<RadiologyDbContext>().UseNpgsql(_pg.ConnectionString,
            o => o.MigrationsHistoryTable("__ef_migrations", "radiology"))
        .UseSnakeCaseNamingConvention().Options);

    private long _modality;

    public async Task InitializeAsync()
    {
        await using var radiology = CreateRadiology();
        await radiology.Database.MigrateAsync();
        var modality = new Modality
        {
            BranchId = 1, Code = $"M{Guid.NewGuid().ToString("N")[..6]}", Name = "Test machine",
        };
        radiology.Modalities.Add(modality);
        await radiology.SaveChangesAsync();
        _modality = modality.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static long _orderTest = 500_000;
    private static long NextOrderTest() => Interlocked.Increment(ref _orderTest);

    [Fact]
    public async Task A_study_is_created_once_per_order_test()
    {
        await using var radiology = CreateRadiology();
        var orderTestId = NextOrderTest();

        var first = await _radiology.EnsureStudyAsync(radiology, 1, orderTestId, 42, _modality);
        var second = await _radiology.EnsureStudyAsync(radiology, 1, orderTestId, 42, _modality);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(RadiologyService.AccessionFor(orderTestId), first.AccessionNo);
        Assert.Equal(StudyState.Awaiting, first.State);
    }

    [Fact]
    public async Task Marking_a_study_done_is_single_shot_and_attributable()
    {
        await using var radiology = CreateRadiology();
        await using var kernel = _pg.CreateKernelContext();
        var study = await _radiology.EnsureStudyAsync(radiology, 1, NextOrderTest(), 42, _modality);

        await _radiology.MarkDoneAsync(radiology, kernel, 1, study.Id, "14x17", 1, "AP view",
            11, "Technician");

        var stored = await radiology.Studies.AsNoTracking().SingleAsync(x => x.Id == study.Id);
        Assert.Equal(StudyState.Done, stored.State);
        Assert.Equal(11, stored.PerformedBy);
        Assert.NotNull(stored.PerformedAt);
        Assert.Equal(1, stored.FilmCount);

        var again = await Assert.ThrowsAsync<RadiologyException>(() => _radiology.MarkDoneAsync(
            radiology, kernel, 1, study.Id, "14x17", 1, null, 11, "Technician"));
        Assert.Contains("not waiting", again.Message);
    }

    [Fact]
    public async Task A_negative_film_count_is_refused()
    {
        await using var radiology = CreateRadiology();
        await using var kernel = _pg.CreateKernelContext();
        var study = await _radiology.EnsureStudyAsync(radiology, 1, NextOrderTest(), 42, _modality);

        await Assert.ThrowsAsync<RadiologyException>(() => _radiology.MarkDoneAsync(
            radiology, kernel, 1, study.Id, null, -1, null, 11, "Technician"));
    }

    [Fact]
    public async Task Reporting_moves_the_study_on_from_either_earlier_state()
    {
        await using var radiology = CreateRadiology();
        await using var kernel = _pg.CreateKernelContext();

        // Straight from awaiting — a report can exist before anyone remembered to mark the film.
        var skipped = await _radiology.EnsureStudyAsync(radiology, 1, NextOrderTest(), 42, _modality);
        await _radiology.MarkReportedAsync(radiology, skipped.Id);
        Assert.Equal(StudyState.Reported,
            (await radiology.Studies.AsNoTracking().SingleAsync(x => x.Id == skipped.Id)).State);

        var normal = await _radiology.EnsureStudyAsync(radiology, 1, NextOrderTest(), 42, _modality);
        await _radiology.MarkDoneAsync(radiology, kernel, 1, normal.Id, null, 1, null, 11, "T");
        await _radiology.MarkReportedAsync(radiology, normal.Id);
        Assert.Equal(StudyState.Reported,
            (await radiology.Studies.AsNoTracking().SingleAsync(x => x.Id == normal.Id)).State);
    }

    [Fact]
    public async Task A_test_belongs_to_at_most_one_machine()
    {
        await using var radiology = CreateRadiology();
        var testId = NextOrderTest();
        radiology.ModalityTests.Add(new ModalityTest { ModalityId = _modality, TestCatalogId = testId });
        await radiology.SaveChangesAsync();

        radiology.ModalityTests.Add(new ModalityTest { ModalityId = _modality + 1, TestCatalogId = testId });
        // Two machines claiming one test would put the same patient on two worklists — the
        // mismatch US10.1 exists to prevent. The database refuses it.
        await Assert.ThrowsAsync<DbUpdateException>(() => radiology.SaveChangesAsync());
    }

    [Fact]
    public async Task An_unmapped_test_has_no_machine()
    {
        await using var radiology = CreateRadiology();
        Assert.Null(await RadiologyService.ModalityForTestAsync(radiology, NextOrderTest()));
    }
}
