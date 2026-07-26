using Hms.Admin.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Admin;

public sealed record CatalogRow(
    string Kind, long Id, string Code, string Name, string Dept, string Extra,
    long? Price, DateOnly? From, DateOnly? To, bool Provisional);

public sealed record PriceHistoryRow(DateOnly From, DateOnly? To, long Price, string Author);

/// <summary>
/// The F1 answer (§9A.1): what the hospital configures during construction. Prices are versions
/// with effective dates (US21.1) — changing one never rewrites history, it closes the current
/// version and opens the next, so a historical invoice always reproduces its historical price (C6).
/// </summary>
[Authorize(Policy = Perm.AdminMastersManage)]
public class MastersModel(HmsTx tx, TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? Show { get; set; }   // "test" | "service"

    // --- new catalog item ---
    [BindProperty] public string Kind { get; set; } = "test";
    [BindProperty] public string? Code { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Dept { get; set; }
    [BindProperty] public string? SampleType { get; set; }
    [BindProperty] public int TatMinutes { get; set; } = 240;
    [BindProperty] public long Price { get; set; }

    // --- price change ---
    [BindProperty] public long TargetId { get; set; }
    [BindProperty] public string TargetKind { get; set; } = "test";
    [BindProperty] public long NewPrice { get; set; }
    [BindProperty] public DateOnly EffectiveFrom { get; set; }

    public DateOnly Today { get; private set; }
    public IReadOnlyList<CatalogRow> Services { get; private set; } = [];
    public IReadOnlyList<CatalogRow> Tests { get; private set; } = [];
    public IReadOnlyList<PriceHistoryRow> History { get; private set; } = [];
    public string? HistoryFor { get; private set; }
    public int UnpricedCount { get; private set; }

    public async Task OnGetAsync(long? history, string? historyKind) => await LoadAsync(history, historyKind);

    private async Task LoadAsync(long? history = null, string? historyKind = null)
    {
        Today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        if (EffectiveFrom == default) EffectiveFrom = Today.AddDays(1);
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
                return new CatalogRow(kind, id, code, name, dept, extra,
                    live?.Price, live?.ValidFrom, live?.ValidTo, provisional);
            }

            Services = (await s.Adm.Services.AsNoTracking().OrderBy(x => x.Dept).ThenBy(x => x.Name).ToListAsync())
                .Select(x => Row("service", x.Id, x.Code, x.Name, x.Dept, Ui.Humanize(x.Kind), x.Provisional))
                .ToList();

            Tests = (await s.Adm.TestCatalog.AsNoTracking().OrderBy(x => x.Dept).ThenBy(x => x.Name).ToListAsync())
                .Select(x => Row("test", x.Id, x.Code, x.Name, x.Dept,
                    (x.SampleTypes.Length > 0 ? string.Join("/", x.SampleTypes) : "no sample")
                    + " · " + (x.TatMinutes >= 60 ? $"{x.TatMinutes / 60} h" : $"{x.TatMinutes} min"),
                    x.Provisional))
                .ToList();

            UnpricedCount = Services.Count(r => r.Price is null) + Tests.Count(r => r.Price is null);

            if (history is { } id2 && historyKind is { } k)
            {
                var authorIds = versions.Select(v => v.AuthorId).Distinct().ToList();
                var authors = await s.Auth.Users.AsNoTracking()
                    .Where(u => authorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
                History = versions
                    .Where(v => v.CatalogKind == k && v.CatalogId == id2 && v.Scope == "standard")
                    .OrderByDescending(v => v.ValidFrom)
                    .Select(v => new PriceHistoryRow(v.ValidFrom, v.ValidTo, v.Price,
                        authors.GetValueOrDefault(v.AuthorId, "—")))
                    .ToList();
                HistoryFor = (k == "test" ? Tests : Services).FirstOrDefault(r => r.Id == id2)?.Name;
            }
            return 0;
        });
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
        {
            await LoadAsync(); Fail("Code and name are both required."); return Page();
        }
        if (Price < 0) { await LoadAsync(); Fail("Price cannot be negative."); return Page(); }

        var code = Code.Trim().ToUpperInvariant();
        var from = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);

        try
        {
            await tx.RunAsync(async s =>
            {
                long id;
                if (Kind == "service")
                {
                    if (await s.Adm.Services.AnyAsync(x => x.Code == code))
                        throw new InvalidOperationException($"A service with code {code} already exists.");
                    var svc = new Service
                    {
                        Code = code, Name = Name!.Trim(), Dept = Dept?.Trim() ?? "OPD", Kind = "consult",
                        // edge 11: an item created before its price is agreed is provisional,
                        // and provisional items are visible on the go-live checklist.
                        Provisional = Price == 0,
                    };
                    s.Adm.Services.Add(svc);
                    await s.Adm.SaveChangesAsync();
                    id = svc.Id;
                }
                else
                {
                    if (await s.Adm.TestCatalog.AnyAsync(x => x.Code == code))
                        throw new InvalidOperationException($"A test with code {code} already exists.");
                    var t = new TestCatalogItem
                    {
                        Code = code, Name = Name!.Trim(), Dept = Dept?.Trim() ?? "Pathology",
                        SampleTypes = string.IsNullOrWhiteSpace(SampleType) ? [] : [SampleType.Trim()],
                        TatMinutes = TatMinutes,
                        Template = ResultTemplates.For(code),
                        Provisional = Price == 0,
                    };
                    s.Adm.TestCatalog.Add(t);
                    await s.Adm.SaveChangesAsync();
                    id = t.Id;
                }

                if (Price > 0)
                {
                    s.Adm.RateVersions.Add(new RateVersion
                    {
                        BranchId = BranchId, CatalogKind = Kind, CatalogId = id,
                        Price = Price, ValidFrom = from, AuthorId = ActorId,
                    });
                    await s.Adm.SaveChangesAsync();
                }

                s.Kernel.AuditEvents.Add(new Hms.Kernel.Data.AuditEvent
                {
                    BranchId = BranchId, At = clock.GetUtcNow(), ActorId = ActorId,
                    ActorNameSnapshot = ActorName, Action = "master.create",
                    Entity = Kind == "service" ? "adm.service" : "adm.test_catalog", EntityId = id,
                    After = System.Text.Json.JsonSerializer.Serialize(new { code, Name, Price }),
                    CorrelationId = Guid.NewGuid(), Tier = 2,
                });
                await s.Kernel.SaveChangesAsync();
                return 0;
            });
        }
        catch (InvalidOperationException e)
        {
            await LoadAsync(); Fail(e.Message); return Page();
        }

        Toast($"{code} added" + (Price == 0 ? " — provisional until a price is set" : ""), "inventory_2");
        return Redirect("/admin/masters");
    }

    /// <summary>
    /// US21.1: a price change is a new version from a future date. The current version is closed
    /// the day the new one opens — never edited — so every past invoice reproduces its own price.
    /// </summary>
    public async Task<IActionResult> OnPostRepriceAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        if (NewPrice < 0) { await LoadAsync(); Fail("Price cannot be negative."); return Page(); }
        if (EffectiveFrom < today)
        {
            await LoadAsync();
            Fail("A price cannot start in the past — that would change invoices already issued. " +
                 "Choose today or a future date.");
            return Page();
        }

        try
        {
            await tx.RunAsync(async s =>
            {
                var open = await s.Adm.RateVersions.SingleOrDefaultAsync(v =>
                    v.CatalogKind == TargetKind && v.CatalogId == TargetId &&
                    v.Scope == "standard" && v.BranchId == BranchId && v.ValidTo == null);

                if (open is not null)
                {
                    if (open.ValidFrom >= EffectiveFrom)
                        throw new InvalidOperationException(
                            $"The current price starts on {Ui.DateLong(open.ValidFrom)} — " +
                            "the new one must start after that.");
                    if (open.Price == NewPrice)
                        throw new InvalidOperationException("That is already the price — nothing to change.");
                    open.ValidTo = EffectiveFrom;
                }

                s.Adm.RateVersions.Add(new RateVersion
                {
                    BranchId = BranchId, CatalogKind = TargetKind, CatalogId = TargetId,
                    Price = NewPrice, ValidFrom = EffectiveFrom, AuthorId = ActorId,
                });

                // An item priced for the first time stops being provisional (edge 11).
                if (TargetKind == "test")
                {
                    var t = await s.Adm.TestCatalog.SingleAsync(x => x.Id == TargetId);
                    t.Provisional = false;
                }
                else
                {
                    var svc = await s.Adm.Services.SingleAsync(x => x.Id == TargetId);
                    svc.Provisional = false;
                }
                await s.Adm.SaveChangesAsync();

                s.Kernel.AuditEvents.Add(new Hms.Kernel.Data.AuditEvent
                {
                    BranchId = BranchId, At = clock.GetUtcNow(), ActorId = ActorId,
                    ActorNameSnapshot = ActorName, Action = "rate.change",
                    Entity = "adm.rate_version", EntityId = TargetId,
                    Before = open is null ? null
                        : System.Text.Json.JsonSerializer.Serialize(new { open.Price, open.ValidFrom }),
                    After = System.Text.Json.JsonSerializer.Serialize(new { NewPrice, EffectiveFrom }),
                    CorrelationId = Guid.NewGuid(), Tier = 2,
                });
                await s.Kernel.SaveChangesAsync();
                return 0;
            });
        }
        catch (InvalidOperationException e)
        {
            await LoadAsync(); Fail(e.Message); return Page();
        }

        Toast($"New price {Ui.Money(NewPrice)} effective {Ui.DateLong(EffectiveFrom)}", "payments");
        return Redirect($"/admin/masters?history={TargetId}&historyKind={TargetKind}");
    }
}
