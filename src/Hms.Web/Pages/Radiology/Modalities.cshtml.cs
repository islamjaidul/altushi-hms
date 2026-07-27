using Hms.Radiology.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Radiology;

public sealed record MappableTest(long Id, string Code, string Name, string Dept, string? Machine);

/// <summary>
/// §5 M10 [M]: the modality master and its test mapping. A customer's machines are not ours, so
/// this has to be editable — the seed maps the demo's three imaging tests and nothing more.
/// Without this screen the mapping would be code, and an unmapped test appears on no worklist.
/// </summary>
[Authorize(Policy = Perm.RadiologyStudyPerform)]
public class ModalitiesModel(HmsTx tx) : HmsPageModel
{
    [BindProperty] public string? Code { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public long ModalityId { get; set; }
    [BindProperty] public long TestId { get; set; }

    public IReadOnlyList<Modality> Modalities { get; private set; } = [];
    public IReadOnlyList<MappableTest> Tests { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        Modalities = await s.Radiology.Modalities.AsNoTracking()
            .OrderBy(m => m.Name).ToListAsync();
        var mapped = await s.Radiology.ModalityTests.AsNoTracking().ToListAsync();
        var byId = Modalities.ToDictionary(m => m.Id, m => m.Name);

        Tests = (await s.Adm.TestCatalog.AsNoTracking()
                .Where(t => t.Active && (t.Dept == "Imaging" || t.Dept == "Cardiology"))
                .OrderBy(t => t.Name).ToListAsync())
            .Select(t => new MappableTest(t.Id, t.Code, t.Name, t.Dept,
                mapped.FirstOrDefault(m => m.TestCatalogId == t.Id) is { } link
                    ? byId.GetValueOrDefault(link.ModalityId) : null))
            .ToList();
        return 0;
    });

    public async Task<IActionResult> OnPostAddAsync()
    {
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Name))
        { await LoadAsync(); Fail("A machine needs a short code and a name."); return Page(); }

        var code = Code.Trim().ToUpperInvariant();
        var added = await tx.RunAsync(async s =>
        {
            if (await s.Radiology.Modalities.AnyAsync(m => m.BranchId == BranchId && m.Code == code))
                return false;
            s.Radiology.Modalities.Add(new Modality
            {
                BranchId = BranchId, Code = code, Name = Name!.Trim(),
            });
            await s.Radiology.SaveChangesAsync();
            return true;
        });
        if (!added) { await LoadAsync(); Fail($"A machine with the code {code} already exists."); return Page(); }

        Toast("Machine added", "science");
        return Redirect("/radiology/modalities");
    }

    /// <summary>
    /// Moving a test to another machine replaces the mapping rather than adding one: the unique
    /// index means a test belongs to exactly one machine, and the operator's intent when they
    /// pick a different machine is plainly "this one now".
    /// </summary>
    public async Task<IActionResult> OnPostMapAsync()
    {
        await tx.RunAsync(async s =>
        {
            var existing = await s.Radiology.ModalityTests
                .FirstOrDefaultAsync(m => m.TestCatalogId == TestId);
            if (ModalityId == 0)
            {
                if (existing is not null) s.Radiology.ModalityTests.Remove(existing);
            }
            else if (existing is null)
            {
                s.Radiology.ModalityTests.Add(new ModalityTest
                {
                    ModalityId = ModalityId, TestCatalogId = TestId,
                });
            }
            else
            {
                existing.ModalityId = ModalityId;
            }
            await s.Radiology.SaveChangesAsync();
            return 0;
        });
        Toast("Mapping updated", "science");
        return Redirect("/radiology/modalities");
    }
}
