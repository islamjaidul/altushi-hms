using System.Globalization;
using Hms.Kernel.Approvals;
using Hms.Kernel.Data;
using Hms.Lis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Lis;

public sealed record AmendableTest(
    long OrderTestId, long ResultId, string OrderNo, string PatientName, string Uhid,
    char Sex, short? AgeYears, long CatalogId, string TestName, int Version,
    DateTimeOffset VerifiedAt, string VerifiedBy);

public sealed record AmendValue(string Code, string Name, string Unit, string Range, string Current);

/// <summary>
/// Edge 22 / §5 M9 [M]: "amendment after verification requires supervisor + reason, both versions
/// retained". A released report is never edited — the amendment writes v2 recording what it
/// supersedes, and the approval that permitted it. v1 stays readable forever, because someone
/// acted on it.
/// </summary>
[Authorize(Policy = Perm.LisResultVerify)]
public class AmendModel(HmsTx tx, LisService lis, ApprovalEngine approvals) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? OrderTestId { get; set; }
    [BindProperty] public Dictionary<string, string> Values { get; set; } = [];
    [BindProperty, System.ComponentModel.DataAnnotations.StringLength(Bounds.Clinical)]
    public string? Narrative { get; set; }
    [BindProperty, System.ComponentModel.DataAnnotations.StringLength(Bounds.Note)]
    public string? Reason { get; set; }

    public IReadOnlyList<AmendableTest> Verified { get; private set; } = [];
    public AmendableTest? Selected { get; private set; }
    public IReadOnlyList<AmendValue> Current { get; private set; } = [];
    /// <summary>Prefills the amendment textarea, so unchanged findings carry to v2 visibly.</summary>
    public string? CurrentNarrative { get; private set; }
    public IReadOnlyList<ApprovalRequest> ApprovedAmendments { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            var cards = await LabBoard.LoadAsync(s, take: 200);
            var released = cards.Where(c => c.Stage is LabBoard.Verified or LabBoard.Delivered).ToList();
            var orderTestIds = released.SelectMany(c => c.Tests.Select(t => t.OrderTestId)).ToList();

            var results = await s.Lis.Results.AsNoTracking()
                .Where(r => orderTestIds.Contains(r.OrderTestId) && r.VerifiedAt != null)
                .ToListAsync();
            var verifierIds = results.Select(r => r.VerifiedBy ?? 0).Distinct().ToList();
            var users = await s.Auth.Users.AsNoTracking()
                .Where(u => verifierIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            var rows = new List<AmendableTest>();
            foreach (var card in released)
            {
                foreach (var t in card.Tests)
                {
                    var latest = results.Where(r => r.OrderTestId == t.OrderTestId)
                        .OrderByDescending(r => r.Version).FirstOrDefault();
                    if (latest?.VerifiedAt is null) continue;
                    rows.Add(new AmendableTest(t.OrderTestId, latest.Id, card.OrderNo,
                        card.PatientName, card.Uhid, card.Sex, card.AgeYears, t.CatalogId, t.Name,
                        latest.Version, latest.VerifiedAt.Value,
                        users.GetValueOrDefault(latest.VerifiedBy ?? 0, "—")));
                }
            }
            Verified = rows.OrderByDescending(r => r.VerifiedAt).Take(40).ToList();
            Selected = OrderTestId is { } id ? Verified.FirstOrDefault(r => r.OrderTestId == id) : null;

            if (Selected is not null)
            {
                var item = await s.Adm.TestCatalog.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == Selected.CatalogId);
                var template = ResultTemplates.Parse(item?.Template);
                var latest = results.Where(r => r.OrderTestId == Selected.OrderTestId)
                    .OrderByDescending(r => r.Version).First();
                var stored = ResultValues.Parse(latest.Values);

                Current = (template?.Parameters ?? [])
                    .Select(p =>
                    {
                        var band = p.BandFor(Selected.Sex, Selected.AgeYears);
                        return new AmendValue(p.Code, p.Name, p.Unit, band?.Text ?? "—",
                            stored.TryGetValue(p.Code, out var v) ? v.Value : "");
                    })
                    .ToList();
                CurrentNarrative = latest.Narrative;

                ApprovedAmendments = await s.Kernel.ApprovalRequests.AsNoTracking()
                    .Where(a => a.Type == "amend" && a.SourceTable == "lis.result"
                                && a.SourceId == Selected.OrderTestId && a.State == ApprovalState.Approved)
                    .ToListAsync();
            }
            return 0;
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (Selected is null) { Fail("Choose the report to correct."); return Page(); }
        if (string.IsNullOrWhiteSpace(Reason))
        { Fail("An amendment needs a reason — it prints on the corrected report."); return Page(); }

        var approved = ApprovedAmendments.FirstOrDefault();
        if (approved is null)
        {
            var raise = await tx.RunAsync(s => approvals.RaiseAsync(
                s.Kernel, BranchId, "amend", "lis.result", Selected.OrderTestId,
                ActorId, ActorRole, Reason!.Trim(), null));

            if (!raise.AutoApproved)
            {
                Toast("Amendment sent for approval — the released report is unchanged until it is decided",
                      "fact_check");
                return Redirect("/lis/amend");
            }
            approved = await tx.RunAsync(s => s.Kernel.ApprovalRequests
                .SingleAsync(a => a.Id == raise.ApprovalId!.Value));
        }

        try
        {
            await tx.RunAsync(async s =>
            {
                var item = await s.Adm.TestCatalog.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == Selected.CatalogId);
                var template = ResultTemplates.Parse(item?.Template);
                var values = new Dictionary<string, object>();

                foreach (var p in template?.Parameters ?? [])
                {
                    if (!Values.TryGetValue(p.Code, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
                    decimal? numeric = decimal.TryParse(raw, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var d) ? d : null;
                    var band = p.BandFor(Selected.Sex, Selected.AgeYears);
                    values[p.Code] = new
                    {
                        value = raw.Trim(),
                        unit = p.Unit,
                        flag = ResultTemplates.Flag(numeric, band),
                        ref_used = band is null ? "—" : $"{band.Text} ({band.Label})",
                    };
                }
                // WP4 (AUD-M10-01): a narrative-only report — every imaging exam — has no
                // template parameters, so "at least one corrected value" made it unamendable.
                // Findings text is a first-class amendment now; the service refuses only when
                // BOTH are empty.
                await lis.AmendAsync(s.Lis, Selected.OrderTestId, values, Narrative,
                    ActorId, approved!.Id);
                return 0;
            });
        }
        catch (LisException e) { Fail(e.Message); return Page(); }

        Toast($"{Selected.OrderNo} amended — version {Selected.Version + 1} issued, the original is retained",
              "sync_alt");
        return Redirect("/lis/amend");
    }
}
