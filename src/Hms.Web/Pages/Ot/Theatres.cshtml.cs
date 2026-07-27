using Hms.Ot.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Ot;

/// <summary>M7 [M] theatre master. Retiring a theatre never deletes it — old cases name it.</summary>
[Authorize(Policy = Perm.OtSchedule)]
public class TheatresModel(HmsTx tx) : HmsPageModel
{
    [BindProperty] public string? Name { get; set; }
    [BindProperty] public long Id { get; set; }

    public IReadOnlyList<Theatre> Rows { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        Rows = await s.Ot.Theatres.AsNoTracking().OrderBy(t => t.Name).ToListAsync();
        return 0;
    });

    public async Task<IActionResult> OnPostAddAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        { await LoadAsync(); Fail("Give the theatre a name."); return Page(); }

        var trimmed = Name.Trim();
        var added = await tx.RunAsync(async s =>
        {
            if (await s.Ot.Theatres.AnyAsync(t => t.BranchId == BranchId && t.Name == trimmed))
                return false;
            s.Ot.Theatres.Add(new Theatre { BranchId = BranchId, Name = trimmed });
            await s.Ot.SaveChangesAsync();
            return true;
        });
        if (!added) { await LoadAsync(); Fail("A theatre with that name already exists."); return Page(); }

        Toast("Theatre added", "science");
        return Redirect("/ot/theatres");
    }

    public async Task<IActionResult> OnPostToggleAsync()
    {
        await tx.RunAsync(async s =>
        {
            var theatre = await s.Ot.Theatres.FirstOrDefaultAsync(t => t.Id == Id);
            if (theatre is not null) theatre.Active = !theatre.Active;
            await s.Ot.SaveChangesAsync();
            return 0;
        });
        Toast("Theatre updated", "science");
        return Redirect("/ot/theatres");
    }
}
