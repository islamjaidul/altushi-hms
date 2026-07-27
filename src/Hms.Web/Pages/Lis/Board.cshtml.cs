using Hms.Lis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Web.Pages.Lis;

/// <summary>
/// 05 §4.3 / screens 10–11 — the pipeline board. A tube scan advances the card it belongs to;
/// clicking is the fallback, not the design centre (§7 U6). Every advance is a state-guarded
/// UPDATE, so two benches scanning the same tube cannot both "win" (ADR-0015 #4).
/// </summary>
[Authorize(Policy = Perm.LisWorklistRead)]
public class BoardModel(HmsTx tx, LisService lis, TimeProvider clock) : HmsPageModel
{
    /// <summary>§5 M9 [M]: "worklists by department / status".</summary>
    [BindProperty(SupportsGet = true)] public string? Dept { get; set; }
    [BindProperty(SupportsGet = true)] public bool LateOnly { get; set; }

    public IReadOnlyList<LabCard> Cards { get; private set; } = [];
    public IReadOnlyList<string> Departments { get; private set; } = [];
    public DateTimeOffset Now { get; private set; }
    public int BreachedCount => Cards.Count(c => c.Breached(Now));

    public IEnumerable<LabCard> InStage(string stage) => Cards.Where(c => c.Stage == stage);

    public async Task OnGetAsync()
    {
        Now = clock.GetUtcNow();
        var all = await tx.RunAsync(s => LabBoard.LoadAsync(s));
        Departments = all.SelectMany(c => c.Departments).Distinct().OrderBy(d => d).ToList();

        Cards = all
            .Where(c => string.IsNullOrEmpty(Dept) || c.Departments.Contains(Dept))
            .Where(c => !LateOnly || c.Breached(Now))
            .ToList();
    }

    /// <summary>
    /// §12 splits the bench from the signatory: collecting and receiving tubes is the
    /// technologist's grant, not the pathologist's. The board is readable by both, so the
    /// handlers carry the finer check the page policy cannot express.
    /// </summary>
    private bool CanHandleSamples => Can("lis.sample.collect");

    public async Task<IActionResult> OnPostCollectAsync(long sampleId)
    {
        if (!CanHandleSamples) return Forbid();
        try
        {
            await tx.RunAsync(s => lis.CollectAsync(s.Lis, sampleId, ActorId));
            Toast("Sample collected", "arrow_forward");
        }
        catch (LisException e) { Toast(e.Message, "error"); }
        return Redirect("/lis/board");
    }

    public async Task<IActionResult> OnPostReceiveAsync(long sampleId)
    {
        if (!CanHandleSamples) return Forbid();
        try
        {
            await tx.RunAsync(s => lis.ReceiveAsync(s.Lis, sampleId, ActorId));
            Toast("Sample received at the lab", "arrow_forward");
        }
        catch (LisException e) { Toast(e.Message, "error"); }
        return Redirect("/lis/board");
    }

    /// <summary>Rejection is never a dead end: it spawns a recollection child on the same tests.</summary>
    public async Task<IActionResult> OnPostRejectAsync(long sampleId, string reason)
    {
        if (!CanHandleSamples) return Forbid();
        if (string.IsNullOrWhiteSpace(reason))
        {
            Toast("A rejection needs a reason — the recollection label carries it.", "error");
            return Redirect("/lis/board");
        }
        try
        {
            var child = await tx.RunAsync(async s =>
            {
                var sample = await lis.RejectAndSpawnRecollectionAsync(s.Lis, sampleId, reason.Trim());
                await lis.PrintLabelAsync(s.Lis, sample.Id, ActorId);
                return sample;
            });
            Toast($"Rejected — recollection label {child.Barcode} printed", "sync_alt");
        }
        catch (LisException e) { Toast(e.Message, "error"); }
        return Redirect("/lis/board");
    }
}
