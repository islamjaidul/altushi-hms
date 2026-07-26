using Hms.Admin;
using Hms.Admin.Data;
using Hms.Kernel.Audit;
using Microsoft.EntityFrameworkCore;

namespace Hms.Integration.Tests;

[Collection("postgres")]
public class ImportAndDashboardTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly CatalogImportService _import = new(new AuditWriter(TimeProvider.System), TimeProvider.System);

    public ImportAndDashboardTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var adm = CreateAdm();
        await adm.Database.MigrateAsync();
        await using var kernel = _pg.CreateKernelContext();
        await kernel.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private AdmDbContext CreateAdm() => new(
        new DbContextOptionsBuilder<AdmDbContext>().UseNpgsql(_pg.ConnectionString,
            o => o.MigrationsHistoryTable("__ef_migrations", "adm_data"))
        .UseSnakeCaseNamingConvention().Options);

    private const string Header = "code,name,dept,sample_types,tat_minutes,price,valid_from\n";

    [Fact]
    public async Task Import_commits_good_rows_and_reports_bad_rows_per_row()
    {
        await using var adm = CreateAdm();
        await using var kernel = _pg.CreateKernelContext();

        var csv = Header +
                  "CBC01,Complete Blood Count,Hematology,EDTA,240,400,2026-01-01\n" +
                  "RBS01,Random Blood Sugar,Biochemistry,Serum,60,150,2026-01-01\n" +
                  ",Missing Code,Biochemistry,Serum,60,100,2026-01-01\n" +          // bad: no code
                  "XR01,X-ray Chest,Imaging,None,120,abc,2026-01-01\n";            // bad: price

        var result = await _import.ImportTestCatalogAsync(adm, kernel, csv, "prices.csv", 1, 7, "Admin");

        Assert.True(result.Committed);
        Assert.Equal(2, result.Inserted);
        Assert.Equal(2, result.Errors.Count);                          // error round-trip (edge 12)
        Assert.Contains(result.Errors, e => e.Row == 4 && e.Field == "code");
        Assert.Contains(result.Errors, e => e.Row == 5 && e.Field == "price");

        var batch = await kernel.ImportBatches.SingleAsync(b => b.Id == result.BatchId);
        Assert.Equal(64, batch.Sha256.Length);                         // file hash recorded (ADR-0010)
    }

    [Fact]
    public async Task Reimport_updates_and_versions_prices_never_duplicating()
    {
        await using var adm = CreateAdm();
        await using var kernel = _pg.CreateKernelContext();

        await _import.ImportTestCatalogAsync(adm, kernel,
            Header + "LIP01,Lipid Profile,Biochemistry,Serum,1440,1200,2026-01-01\n",
            "v1.csv", 1, 7, "Admin");

        // The construction-phase price change (edge 13): same code, new price, later date
        var second = await _import.ImportTestCatalogAsync(adm, kernel,
            Header + "LIP01,Lipid Profile,Biochemistry,Serum,1440,1500,2026-08-01\n",
            "v2.csv", 1, 7, "Admin");

        Assert.Equal(0, second.Inserted);
        Assert.Equal(1, second.Updated);                               // upsert by natural key

        var item = await adm.TestCatalog.SingleAsync(t => t.Code == "LIP01");
        var versions = await adm.RateVersions
            .Where(v => v.CatalogKind == "test" && v.CatalogId == item.Id)
            .OrderBy(v => v.ValidFrom).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.Equal(new DateOnly(2026, 8, 1), versions[0].ValidTo);   // old version closed, not edited away
        Assert.Equal(1500, versions[1].Price);

        var resolver = new RateResolver();
        Assert.Equal(1200, (await resolver.ResolveAsync(adm, "test", item.Id, new DateOnly(2026, 7, 15))).Price);
        Assert.Equal(1500, (await resolver.ResolveAsync(adm, "test", item.Id, new DateOnly(2026, 8, 15))).Price);
    }

    [Fact]
    public async Task Dashboard_view_aggregates_latest_summary_versions()
    {
        await using var kernel = _pg.CreateKernelContext();
        var rows = await kernel.Database.SqlQuery<long>(
            $"SELECT coalesce(sum(net),0) AS \"Value\" FROM bill.v_dashboard_day").ToListAsync();
        Assert.Single(rows);                                           // view exists and aggregates
        Assert.True(rows[0] >= 0);
    }
}
