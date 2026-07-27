using Hms.Radiology;
using Hms.Radiology.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Radiology;

/// <summary>
/// US10.1: today's paid imaging orders on the machine that performs them, so nobody re-types a
/// patient's name into a modality. Unpaid orders are absent by construction — the worklist reads
/// released orders only, exactly as the lab bench does.
/// </summary>
[Authorize(Policy = Perm.RadiologyWorklistRead)]
public class WorklistModel(HmsTx tx, RadiologyService radiology) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? ModalityId { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowReported { get; set; }

    [BindProperty] public long StudyId { get; set; }
    [BindProperty] public string? FilmSize { get; set; }
    [BindProperty] public int FilmCount { get; set; }
    [BindProperty] public string? Note { get; set; }

    public IReadOnlyList<Modality> Modalities { get; private set; } = [];
    public IReadOnlyList<RadiologyReporting.WorklistRow> Rows { get; private set; } = [];
    public int Unassigned { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        Modalities = await s.Radiology.Modalities.AsNoTracking()
            .Where(m => m.Active).OrderBy(m => m.Name).ToListAsync();
        Rows = await RadiologyReporting.WorklistAsync(s, radiology, BranchId, ModalityId, ShowReported);

        // A test nobody has mapped would silently vanish from every worklist. Count them so the
        // gap is visible on the screen instead of showing up as a missing report next week.
        var mapped = await s.Radiology.ModalityTests.AsNoTracking()
            .Select(m => m.TestCatalogId).ToListAsync();
        Unassigned = await s.Adm.TestCatalog.AsNoTracking()
            .CountAsync(t => t.Active && (t.Dept == "Imaging" || t.Dept == "Cardiology")
                             && !mapped.Contains(t.Id));
        return 0;
    });

    public async Task<IActionResult> OnPostDoneAsync()
    {
        if (!Can("radiology.study.perform"))
        {
            await LoadAsync();
            Fail("Only a radiology technician can mark a study performed.");
            return Page();
        }
        try
        {
            await tx.RunAsync(async s => await radiology.MarkDoneAsync(s.Radiology, s.Kernel,
                BranchId, StudyId, FilmSize, FilmCount, Note, ActorId, ActorName));
            Toast("Study marked done", "task_alt");
            return Redirect(ModalityId is null
                ? "/radiology/worklist"
                : $"/radiology/worklist?ModalityId={ModalityId}");
        }
        catch (RadiologyException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }
}
