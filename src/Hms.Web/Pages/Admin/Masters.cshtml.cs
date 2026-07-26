using Hms.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Admin;

public sealed record CatalogRow(
    string Kind, string Code, string Name, string Dept, string Extra,
    long? Price, DateOnly? From, DateOnly? To, bool Provisional);

/// <summary>
/// The F1 answer (§9A.1): what the hospital configures during construction. Prices are versions
/// with effective dates, so a historical invoice always reproduces its historical price (C6) —
/// this screen shows which version is in force today and what supersedes it.
/// </summary>
[Authorize(Policy = Perm.AdminMastersManage)]
public class MastersModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    public DateOnly Today { get; private set; }
    public IReadOnlyList<CatalogRow> Services { get; private set; } = [];
    public IReadOnlyList<CatalogRow> Tests { get; private set; } = [];
    public int UnpricedCount { get; private set; }

    public async Task OnGetAsync()
    {
        Today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var today = Today;

        await tx.RunAsync(async s =>
        {
            var versions = await s.Adm.RateVersions.AsNoTracking().ToListAsync();

            CatalogRow Row(string kind, long id, string code, string name, string dept,
                string extra, bool provisional)
            {
                var live = versions.FirstOrDefault(v =>
                    v.CatalogKind == kind && v.CatalogId == id && v.Scope == "standard"
                    && v.ValidFrom <= today && (v.ValidTo == null || today < v.ValidTo));
                return new CatalogRow(kind, code, name, dept, extra,
                    live?.Price, live?.ValidFrom, live?.ValidTo, provisional);
            }

            Services = (await s.Adm.Services.AsNoTracking().OrderBy(x => x.Dept).ThenBy(x => x.Name)
                    .ToListAsync())
                .Select(x => Row("service", x.Id, x.Code, x.Name, x.Dept, Ui.Humanize(x.Kind), x.Provisional))
                .ToList();

            Tests = (await s.Adm.TestCatalog.AsNoTracking().OrderBy(x => x.Dept).ThenBy(x => x.Name)
                    .ToListAsync())
                .Select(x => Row("test", x.Id, x.Code, x.Name, x.Dept,
                    (x.SampleTypes.Length > 0 ? string.Join("/", x.SampleTypes) : "no sample")
                    + " · " + (x.TatMinutes >= 60 ? $"{x.TatMinutes / 60} h" : $"{x.TatMinutes} min"),
                    x.Provisional))
                .ToList();

            UnpricedCount = Services.Count(r => r.Price is null) + Tests.Count(r => r.Price is null);
            return 0;
        });
    }
}
