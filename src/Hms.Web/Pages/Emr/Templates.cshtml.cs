using Hms.Emr;
using Hms.Emr.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Emr;

/// <summary>
/// §5 M5 [S]: a doctor's own templates and favourite drugs. Both belong to the doctor, not to
/// the hospital — one consultant's "URTI adult" is not another's, and sharing them would make
/// the feature useless to both.
/// </summary>
[Authorize(Policy = Perm.EmrNoteWrite)]
public class TemplatesModel(HmsTx tx, EmrService emr) : HmsPageModel
{
    [BindProperty] public long Id { get; set; }
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public string? Complaint { get; set; }
    [BindProperty] public string? OnExamination { get; set; }
    [BindProperty] public string? Diagnosis { get; set; }
    [BindProperty] public string? Advice { get; set; }
    [BindProperty] public List<string?> DrugName { get; set; } = [];
    [BindProperty] public List<string?> DrugDose { get; set; } = [];
    [BindProperty] public List<string?> DrugFrequency { get; set; } = [];
    [BindProperty] public List<string?> DrugDuration { get; set; } = [];

    [BindProperty] public long FavouriteProductId { get; set; }
    [BindProperty] public string? FavouriteName { get; set; }
    [BindProperty] public string? FavouriteDose { get; set; }
    [BindProperty] public string? FavouriteFrequency { get; set; }
    [BindProperty] public string? FavouriteDuration { get; set; }

    public IReadOnlyList<NoteTemplate> Rows { get; private set; } = [];
    public IReadOnlyList<Favourite> Favourites { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        Rows = await s.Emr.Templates.AsNoTracking()
            .Where(t => t.DoctorId == ActorId && t.Active).OrderBy(t => t.Name).ToListAsync();
        Favourites = await s.Emr.Favourites.AsNoTracking()
            .Where(f => f.DoctorId == ActorId).OrderBy(f => f.DrugName).ToListAsync();
        return 0;
    });

    public static IReadOnlyList<DrugLine> Lines(NoteTemplate t) => EmrService.TemplateDrugs(t);

    public async Task<IActionResult> OnPostSaveAsync()
    {
        try
        {
            await tx.RunAsync(async s => await emr.SaveTemplateAsync(s.Emr, ActorId, Name ?? "",
                new NoteBody(Complaint, OnExamination, Diagnosis, Advice, null), Drugs()));
            Toast("Template saved", "edit_note");
            return Redirect("/emr/templates");
        }
        catch (EmrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    /// <summary>Retiring a template hides it; it is never deleted, so old notes keep their lineage.</summary>
    public async Task<IActionResult> OnPostRetireAsync()
    {
        await tx.RunAsync(async s =>
        {
            var template = await s.Emr.Templates
                .FirstOrDefaultAsync(t => t.Id == Id && t.DoctorId == ActorId);
            if (template is not null) template.Active = false;
            await s.Emr.SaveChangesAsync();
            return 0;
        });
        Toast("Template retired", "edit_note");
        return Redirect("/emr/templates");
    }

    public async Task<IActionResult> OnPostFavouriteAsync()
    {
        if (FavouriteProductId == 0 || string.IsNullOrWhiteSpace(FavouriteName))
        {
            await LoadAsync();
            Fail("Pick the medicine from the list so the favourite points at a real product.");
            return Page();
        }
        await tx.RunAsync(async s => await emr.AddFavouriteAsync(s.Emr, ActorId, FavouriteProductId,
            FavouriteName!.Trim(), FavouriteDose, FavouriteFrequency, FavouriteDuration));
        Toast("Added to your favourites", "task_alt");
        return Redirect("/emr/templates");
    }

    private List<DrugLine> Drugs()
    {
        var lines = new List<DrugLine>();
        for (var i = 0; i < DrugName.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(DrugName[i])) continue;
            lines.Add(new DrugLine(null, DrugName[i]!, At(DrugDose, i), At(DrugFrequency, i),
                At(DrugDuration, i), null));
        }
        return lines;

        static string? At(List<string?> list, int i) => i < list.Count ? list[i] : null;
    }
}
