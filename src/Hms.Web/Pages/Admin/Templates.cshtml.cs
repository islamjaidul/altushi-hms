using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Admin;

public sealed record TemplateTest(long Id, string Code, string Name, string Dept, int Parameters);

public sealed record EditableParameter(
    string Code, string Name, string Unit,
    decimal? Low, decimal? High,
    decimal? MaleLow, decimal? MaleHigh, decimal? FemaleLow, decimal? FemaleHigh);

/// <summary>
/// 5A-10 [Must]: "per-modality report template engine — each a configurable format". The
/// parameter grid a test reports on, and the reference bands each parameter is judged by, are
/// data the hospital edits — not code. Editing a template never touches results already stored:
/// each result keeps the band it was judged by in its own row (edge 22).
/// </summary>
[Authorize(Policy = Perm.AdminMastersManage)]
public class TemplatesModel(HmsTx tx) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? TestId { get; set; }
    [BindProperty] public List<string> Code { get; set; } = [];
    [BindProperty] public List<string> Name { get; set; } = [];
    [BindProperty] public List<string> Unit { get; set; } = [];
    [BindProperty] public List<string> Low { get; set; } = [];
    [BindProperty] public List<string> High { get; set; } = [];
    [BindProperty] public List<string> MaleLow { get; set; } = [];
    [BindProperty] public List<string> MaleHigh { get; set; } = [];
    [BindProperty] public List<string> FemaleLow { get; set; } = [];
    [BindProperty] public List<string> FemaleHigh { get; set; } = [];

    public IReadOnlyList<TemplateTest> Tests { get; private set; } = [];
    public TemplateTest? Selected { get; private set; }
    public IReadOnlyList<EditableParameter> Parameters { get; private set; } = [];
    public int UsageCount { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            var items = await s.Adm.TestCatalog.AsNoTracking()
                .Where(t => t.Active).OrderBy(t => t.Dept).ThenBy(t => t.Name).ToListAsync();

            Tests = items.Select(t => new TemplateTest(t.Id, t.Code, t.Name, t.Dept,
                ResultTemplates.Parse(t.Template)?.Parameters.Count ?? 0)).ToList();

            var item = TestId is { } id ? items.FirstOrDefault(t => t.Id == id) : null;
            if (item is null) return 0;

            Selected = Tests.First(t => t.Id == item.Id);
            var template = ResultTemplates.Parse(item.Template);

            Parameters = (template?.Parameters ?? []).Select(p =>
            {
                ReferenceBand? Band(char? sex) => p.SafeBands.FirstOrDefault(b =>
                    b.Sex == sex && b.AgeFrom is null && b.AgeTo is null);
                var general = Band(null);
                var male = Band('M');
                var female = Band('F');
                return new EditableParameter(p.Code, p.Name, p.Unit,
                    general?.Low, general?.High, male?.Low, male?.High, female?.Low, female?.High);
            }).ToList();

            // How many results already used this template — context for the edit.
            // diag.* and lis.* are separate contexts (ADR-0003): two queries, joined in memory.
            var orderTestIds = await s.Diag.OrderTests.AsNoTracking()
                .Where(ot => ot.TestCatalogId == item.Id).Select(ot => ot.Id).ToListAsync();
            UsageCount = orderTestIds.Count == 0 ? 0
                : await s.Lis.Results.AsNoTracking().CountAsync(r => orderTestIds.Contains(r.OrderTestId));
            return 0;
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (Selected is null) { Fail("Choose a test to configure."); return Page(); }

        static decimal? Num(List<string> list, int i) =>
            i < list.Count && decimal.TryParse(list[i], out var d) ? d : null;

        var parameters = new List<ResultParameter>();
        for (var i = 0; i < Code.Count; i++)
        {
            var code = Code[i]?.Trim().ToUpperInvariant();
            var name = i < Name.Count ? Name[i]?.Trim() : null;
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) continue;

            var bands = new List<ReferenceBand>();
            var (lo, hi) = (Num(Low, i), Num(High, i));
            if (lo is not null || hi is not null) bands.Add(new ReferenceBand(null, null, null, lo, hi));
            var (ml, mh) = (Num(MaleLow, i), Num(MaleHigh, i));
            if (ml is not null && mh is not null) bands.Add(new ReferenceBand('M', null, null, ml, mh));
            var (fl, fh) = (Num(FemaleLow, i), Num(FemaleHigh, i));
            if (fl is not null && fh is not null) bands.Add(new ReferenceBand('F', null, null, fl, fh));
            if (bands.Count == 0) bands.Add(new ReferenceBand(null, null, null, null, null));

            parameters.Add(new ResultParameter(code, name!, i < Unit.Count ? Unit[i]?.Trim() ?? "" : "", bands));
        }

        if (parameters.Count == 0)
        { Fail("A template needs at least one parameter — code and name are both required."); return Page(); }

        var duplicate = parameters.GroupBy(p => p.Code).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        { Fail($"Parameter code {duplicate.Key} appears more than once."); return Page(); }

        var json = JsonSerializer.Serialize(new ResultTemplate(parameters));
        await tx.RunAsync(async s =>
        {
            var item = await s.Adm.TestCatalog.SingleAsync(t => t.Id == Selected!.Id);
            item.Template = json;
            await s.Adm.SaveChangesAsync();

            s.Kernel.AuditEvents.Add(new Hms.Kernel.Data.AuditEvent
            {
                BranchId = BranchId, At = DateTimeOffset.UtcNow, ActorId = ActorId,
                ActorNameSnapshot = ActorName, Action = "template.change",
                Entity = "adm.test_catalog", EntityId = item.Id,
                After = JsonSerializer.Serialize(new { item.Code, parameters = parameters.Count }),
                CorrelationId = Guid.NewGuid(), Tier = 2,
            });
            await s.Kernel.SaveChangesAsync();
            return 0;
        });

        Toast($"{Selected.Code} template saved — {parameters.Count} parameter(s)", "science");
        return Redirect($"/admin/templates?testId={Selected.Id}");
    }
}
