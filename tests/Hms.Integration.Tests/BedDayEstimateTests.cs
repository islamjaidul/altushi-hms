using Hms.Admin;
using Hms.Admin.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0032 M2-D1. `/frontdesk` totals a patient's unposted bed days by resolving each one, and
/// the loop used to be `catch (RateResolutionException) { }` — an unpriced day contributed zero
/// and the enquiry desk quoted a family a number that was too low, with nothing on screen to
/// say so. For a screen whose only job is to quote, a silently-low quote is the worst failure
/// mode available.
///
/// The QA sweep could not reach it with seeded data and reported it as a code-read risk rather
/// than a reproduced defect. It is reachable, and these tests are the reproduction: a rate plan
/// only has to start *after* a sitting patient was admitted, which is exactly what edge 11's
/// "provisional items need prices before go-live" describes, or to carry a gap between two
/// versions, which effective-dating permits by construction (`valid_to` is nullable, not
/// contiguous).
///
/// The fix is `RateResolver.TotalOverDaysAsync`, which still refuses to throw at a read-only
/// screen but returns the days it could not price so the caller can present a floor honestly.
/// </summary>
[Collection("postgres")]
public class BedDayEstimateTests : IAsyncLifetime
{
    private const long CabinBedDay = 9101;      // a bed-day tariff service, priced below
    private const long WardBedDay = 9102;       // one with a gap in its rate plan
    private const long UnpricedBedDay = 9103;   // provisional at go-live — never priced

    private readonly PostgresFixture _pg;
    private readonly RateResolver _resolver = new();

    public BedDayEstimateTests(PostgresFixture pg) => _pg = pg;

    private AdmDbContext CreateAdm() => new(
        new DbContextOptionsBuilder<AdmDbContext>().UseNpgsql(_pg.ConnectionString,
            o => o.MigrationsHistoryTable("__ef_migrations", "adm_data"))
        .UseSnakeCaseNamingConvention().Options);

    public async Task InitializeAsync()
    {
        await using var adm = CreateAdm();
        await adm.Database.MigrateAsync();

        // xUnit builds a fresh instance per test while the Postgres fixture is shared, so the
        // seed has to be idempotent — `ex_rate_version_no_overlap` refuses a second insert.
        if (await adm.RateVersions.AnyAsync(r => r.CatalogId == CabinBedDay)) return;

        // The cabin is priced from 10 July onward and not before — a ward brought onto the rate
        // plan while a patient was already lying in it.
        adm.RateVersions.Add(new RateVersion
        {
            BranchId = 1, CatalogKind = "service", CatalogId = CabinBedDay, Price = 1200,
            ValidFrom = new DateOnly(2026, 7, 10), ValidTo = null, AuthorId = 1,
        });

        // The general ward is priced 1-5 July and again from 8 July: 5, 6 and 7 July fall in a
        // gap that effective-dating permits and nothing forbids.
        adm.RateVersions.Add(new RateVersion
        {
            BranchId = 1, CatalogKind = "service", CatalogId = WardBedDay, Price = 800,
            ValidFrom = new DateOnly(2026, 7, 1), ValidTo = new DateOnly(2026, 7, 5), AuthorId = 1,
        });
        adm.RateVersions.Add(new RateVersion
        {
            BranchId = 1, CatalogKind = "service", CatalogId = WardBedDay, Price = 900,
            ValidFrom = new DateOnly(2026, 7, 8), ValidTo = null, AuthorId = 1,
        });

        await adm.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static DateOnly[] Days(int from, int to) =>
        Enumerable.Range(from, to - from + 1).Select(d => new DateOnly(2026, 7, d)).ToArray();

    // ---- the defect ------------------------------------------------------------

    /// <summary>
    /// The reproduction. A patient admitted on 8 July into a cabin priced only from 10 July has
    /// four unposted days; two of them cannot be priced. The old loop returned 2400 and called it
    /// the answer. The estimate is 2400 either way — what changed is that it now knows it is short.
    /// </summary>
    [Fact]
    public async Task An_unpriced_bed_day_is_named_not_silently_dropped()
    {
        await using var adm = CreateAdm();

        var total = await _resolver.TotalOverDaysAsync(adm, "service", CabinBedDay, Days(8, 11));

        Assert.Equal(2400, total.Total);                 // 10 + 11 July at 1200
        Assert.False(total.IsComplete);                  // ...and it says so
        Assert.Equal([new DateOnly(2026, 7, 8), new DateOnly(2026, 7, 9)], total.Unpriced);
    }

    /// <summary>A gap between two rate versions is unpriced too — the plan need not be
    /// contiguous, and the days in the hole are real days the patient occupied a bed.</summary>
    [Fact]
    public async Task A_gap_between_two_rate_versions_leaves_named_unpriced_days()
    {
        await using var adm = CreateAdm();

        var total = await _resolver.TotalOverDaysAsync(adm, "service", WardBedDay, Days(3, 9));

        // 3, 4 July at 800; 5-7 July unpriced; 8, 9 July at 900.
        Assert.Equal(1600 + 1800, total.Total);
        Assert.Equal(Days(5, 7), total.Unpriced);
    }

    /// <summary>A tariff nobody ever priced — the go-live case edge 11 names — prices nothing
    /// and reports every day, rather than quoting a confident zero.</summary>
    [Fact]
    public async Task A_never_priced_tariff_reports_every_day_and_totals_zero()
    {
        await using var adm = CreateAdm();

        var total = await _resolver.TotalOverDaysAsync(adm, "service", UnpricedBedDay, Days(1, 3));

        Assert.Equal(0, total.Total);
        Assert.Equal(3, total.Unpriced.Count);
        Assert.False(total.IsComplete);
    }

    /// <summary>The total is a lower bound: it can never exceed the fully-priced figure, and the
    /// gap is exactly the unpriced days. This is what lets the screen say "at least".</summary>
    [Fact]
    public async Task The_total_is_a_floor_when_days_are_missing()
    {
        await using var adm = CreateAdm();

        var partial = await _resolver.TotalOverDaysAsync(adm, "service", CabinBedDay, Days(8, 11));
        var whole = await _resolver.TotalOverDaysAsync(adm, "service", CabinBedDay, Days(10, 11));

        Assert.True(partial.Total <= whole.Total + partial.Unpriced.Count * 1200);
        Assert.True(whole.IsComplete);
        Assert.Equal(partial.Total, whole.Total);        // the missing days added nothing at all
    }

    // ---- the happy path must stay silent ---------------------------------------

    /// <summary>A fully priced stay reports complete, so the warning cannot cry wolf on the
    /// ordinary case — which is every case in the seeded database.</summary>
    [Fact]
    public async Task A_fully_priced_stay_is_complete_and_sums_every_day()
    {
        await using var adm = CreateAdm();

        var total = await _resolver.TotalOverDaysAsync(adm, "service", CabinBedDay, Days(10, 14));

        Assert.Equal(5 * 1200, total.Total);
        Assert.True(total.IsComplete);
        Assert.Empty(total.Unpriced);
    }

    [Fact]
    public async Task No_days_at_all_is_complete_and_zero()
    {
        await using var adm = CreateAdm();

        var total = await _resolver.TotalOverDaysAsync(adm, "service", CabinBedDay, []);

        Assert.Equal(0, total.Total);
        Assert.True(total.IsComplete);
    }

    /// <summary>The per-day resolution still honours effective dating (C6): the gap's two sides
    /// keep their own historical prices rather than both taking the latest.</summary>
    [Fact]
    public async Task Each_day_is_priced_at_the_rate_effective_on_that_day()
    {
        await using var adm = CreateAdm();

        var early = await _resolver.TotalOverDaysAsync(adm, "service", WardBedDay, Days(1, 2));
        var late = await _resolver.TotalOverDaysAsync(adm, "service", WardBedDay, Days(8, 9));

        Assert.Equal(1600, early.Total);      // 2 x 800, the price then
        Assert.Equal(1800, late.Total);       // 2 x 900, the price now
    }
}
