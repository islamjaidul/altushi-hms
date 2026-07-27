using System.Globalization;
using Hms.Lis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Lis;

public sealed record EntryParameter(
    string Code, string Name, string Unit, string Range,
    decimal? Low, decimal? High, string? Existing);
public sealed record EntryTest(
    long OrderTestId, string Name, bool HasResult, bool NarrativeOnly,
    IReadOnlyList<EntryParameter> Parameters, string? Narrative);

/// <summary>
/// 05 §5 screen 12. A keyboard grid: type a number, Enter moves to the next parameter. Flags are
/// computed server-side against the range that was in force, and stored with the value — saving
/// is not verifying (edge 34), so nothing here releases a report.
/// </summary>
[Authorize(Policy = Perm.LisResultEnter)]
public class ResultsModel(HmsTx tx, LisService lis) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? OrderId { get; set; }
    [BindProperty] public Dictionary<string, string> Values { get; set; } = [];
    [BindProperty] public Dictionary<string, string> Narratives { get; set; } = [];

    public IReadOnlyList<LabCard> Worklist { get; private set; } = [];
    public LabCard? Selected { get; private set; }
    public IReadOnlyList<EntryTest> Tests { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            var all = await LabBoard.LoadAsync(s);
            Worklist = all.Where(c => c.Stage is LabBoard.Received or LabBoard.Resulted).ToList();
            Selected = OrderId is { } id
                ? all.FirstOrDefault(c => c.OrderId == id)
                : Worklist.FirstOrDefault();
            if (Selected is null) return 0;

            var catalogIds = Selected.Tests.Select(t => t.CatalogId).ToList();
            var catalog = await s.Adm.TestCatalog.AsNoTracking()
                .Where(t => catalogIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

            var orderTestIds = Selected.Tests.Select(t => t.OrderTestId).ToList();
            var existing = await s.Lis.Results.AsNoTracking()
                .Where(r => orderTestIds.Contains(r.OrderTestId))
                .ToListAsync();

            var tests = new List<EntryTest>();
            foreach (var t in Selected.Tests)
            {
                var item = catalog.GetValueOrDefault(t.CatalogId);
                var template = ResultTemplates.Parse(item?.Template);
                var latest = existing.Where(r => r.OrderTestId == t.OrderTestId)
                    .OrderByDescending(r => r.Version).FirstOrDefault();
                var stored = ResultValues.Parse(latest?.Values);

                var parameters = (template?.Parameters ?? [])
                    .Select(p => new EntryParameter(p.Code, p.Name, p.Unit,
                        ResultTemplates.RangeText(p), p.Low, p.High,
                        stored.TryGetValue(p.Code, out var v) ? v.Value : null))
                    .ToList();

                tests.Add(new EntryTest(t.OrderTestId, t.Name, latest is not null,
                    parameters.Count == 0, parameters, latest?.Narrative));
            }
            Tests = tests;
            return 0;
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (Selected is null) { Fail("Nothing selected."); return Page(); }

        var saved = 0;
        var skipped = new List<string>();

        try
        {
            await tx.RunAsync(async s =>
            {
                var catalogIds = Selected!.Tests.Select(t => t.CatalogId).ToList();
                var catalog = await s.Adm.TestCatalog.AsNoTracking()
                    .Where(t => catalogIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

                foreach (var t in Selected.Tests)
                {
                    if (t.HasResult) { skipped.Add(t.Name); continue; }   // edge 22: amend, never overwrite

                    var template = ResultTemplates.Parse(catalog.GetValueOrDefault(t.CatalogId)?.Template);
                    var values = new Dictionary<string, object>();

                    foreach (var p in template?.Parameters ?? [])
                    {
                        var key = $"{t.OrderTestId}.{p.Code}";
                        if (!Values.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;

                        decimal? numeric = decimal.TryParse(raw, NumberStyles.Any,
                            CultureInfo.InvariantCulture, out var d) ? d : null;
                        values[p.Code] = new
                        {
                            value = raw.Trim(),
                            unit = p.Unit,
                            flag = ResultTemplates.Flag(numeric, p),
                            ref_used = ResultTemplates.RangeText(p),   // the range as it stood today
                        };
                    }

                    Narratives.TryGetValue(t.OrderTestId.ToString(), out var narrative);
                    if (values.Count == 0 && string.IsNullOrWhiteSpace(narrative)) continue;

                    await lis.EnterResultAsync(s.Lis, t.OrderTestId, values, narrative, ActorId);
                    saved++;
                }
                return 0;
            });
        }
        catch (LisException e)
        {
            Fail(e.Message);
            return Page();
        }

        if (saved == 0 && skipped.Count > 0)
        {
            Fail($"Already entered: {string.Join(", ", skipped)}. " +
                 "A recorded result is corrected by amendment, not by overwriting it.");
            return Page();
        }
        if (saved == 0) { Fail("Nothing to save — enter at least one value."); return Page(); }

        Toast($"{Selected.OrderNo} — {saved} result(s) saved, awaiting verification", "save");
        return Redirect("/lis/results");
    }
}
