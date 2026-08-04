using Hms.Lis;
using Hms.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Lis;

public sealed record VerifyValue(string Name, string Value, string Unit, string Flag, string Range);
public sealed record VerifyTest(
    long OrderTestId, long ResultId, string Name, bool Verified,
    IReadOnlyList<VerifyValue> Values, string? Narrative, string? EnteredBy, DateTimeOffset EnteredAt);

/// <summary>
/// 05 §5 screen 13. Verification is a signature, not a save: the e-sign hash covers the values,
/// so a later edit cannot hide behind the same signature (edge 34). Release is what triggers the
/// report-ready SMS (§9A.2 module 8).
/// </summary>
[Authorize(Policy = Perm.LisResultVerify)]
public class VerifyModel(
    HmsTx tx, LisService lis, SmsQueue sms, OrgIdentity hospital) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? OrderId { get; set; }
    /// <summary>5A-R1 [Must]: which reporting consultant's block prints on the release.</summary>
    [BindProperty] public long? ConsultantId { get; set; }
    /// <summary>
    /// US9.3 AC (spec 0044): the pathologist's explicit acknowledgement that they have seen the
    /// critical values on this order. Deliberately a separate control from the verify button —
    /// pressing the same button twice is not an acknowledgement of anything.
    /// </summary>
    [BindProperty] public bool CriticalAcknowledged { get; set; }

    public IReadOnlyList<LabCard> Worklist { get; private set; } = [];
    public LabCard? Selected { get; private set; }
    public IReadOnlyList<VerifyTest> Tests { get; private set; } = [];
    public IReadOnlyList<ConsultantPick> Consultants { get; private set; } = [];

    public sealed record ConsultantPick(long Id, string Label);

    /// <summary>A critical value awaiting the pathologist's eye, with the test it came from.</summary>
    public sealed record CriticalFinding(string TestName, VerifyValue Value)
    {
        public string Sentence =>
            $"{Value.Name} {Value.Value}{(string.IsNullOrWhiteSpace(Value.Unit) ? "" : " " + Value.Unit)}"
          + $" — {ResultFlags.Label(Value.Flag)} (ref {Value.Range})";
    }

    /// <summary>
    /// Every critical value on the tests **still to be verified**. Already-verified tests are
    /// excluded: their acknowledgement was given when they were released, and re-demanding it
    /// would block the rest of a part-verified order forever.
    /// </summary>
    public IReadOnlyList<CriticalFinding> Criticals =>
        Tests.Where(t => !t.Verified)
             .SelectMany(t => t.Values.Where(v => ResultFlags.IsCritical(v.Flag))
                               .Select(v => new CriticalFinding(t.Name, v)))
             .ToList();

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            var all = await LabBoard.LoadAsync(s);
            Worklist = all.Where(c => c.Stage == LabBoard.Resulted).ToList();
            Selected = OrderId is { } id
                ? all.FirstOrDefault(c => c.OrderId == id)
                : Worklist.FirstOrDefault();
            if (Selected is null) return 0;

            var catalogIds = Selected.Tests.Select(t => t.CatalogId).ToList();
            var catalog = await s.Adm.TestCatalog.AsNoTracking()
                .Where(t => catalogIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);

            var orderTestIds = Selected.Tests.Select(t => t.OrderTestId).ToList();
            var results = await s.Lis.Results.AsNoTracking()
                .Where(r => orderTestIds.Contains(r.OrderTestId)).ToListAsync();
            var enteredByIds = results.Select(r => r.EnteredBy).Distinct().ToList();
            var users = await s.Auth.Users.AsNoTracking()
                .Where(u => enteredByIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            var tests = new List<VerifyTest>();
            foreach (var t in Selected.Tests)
            {
                var latest = results.Where(r => r.OrderTestId == t.OrderTestId)
                    .OrderByDescending(r => r.Version).FirstOrDefault();
                if (latest is null) continue;

                var template = ResultTemplates.Parse(catalog.GetValueOrDefault(t.CatalogId)?.Template);
                var stored = ResultValues.Parse(latest.Values);
                var values = (template?.Parameters ?? [])
                    .Where(p => stored.ContainsKey(p.Code))
                    .Select(p => new VerifyValue(p.Name, stored[p.Code].Value, stored[p.Code].Unit,
                        stored[p.Code].Flag, stored[p.Code].RefUsed))
                    .ToList();

                tests.Add(new VerifyTest(t.OrderTestId, latest.Id, t.Name,
                    latest.VerifiedAt is not null, values, latest.Narrative,
                    users.GetValueOrDefault(latest.EnteredBy), latest.EnteredAt));
            }
            Tests = tests;

            // Only consultants entitled to sign for these departments are offered (§7 U7).
            var depts = await s.Adm.TestCatalog.AsNoTracking()
                .Where(x => catalogIds.Contains(x.Id)).Select(x => x.Dept).Distinct().ToListAsync();
            Consultants = (await s.Adm.ReportingConsultants.AsNoTracking()
                    .Where(c => c.Active).OrderBy(c => c.Name).ToListAsync())
                .Where(c => c.Departments.Length == 0 || c.Departments.Any(depts.Contains))
                .Select(c => new ConsultantPick(c.Id, $"{c.Name} — {c.Degrees}"))
                .ToList();
            ConsultantId ??= Consultants.FirstOrDefault()?.Id;
            return 0;
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (Selected is null)
        { Fail("That order is not awaiting verification — pick one from the worklist."); return Page(); }

        // AUD-VAL-26a: the signature block resolves against the reporting-consultant master —
        // an id nothing points at would sign the printed report with nobody. Only a consultant
        // this order's departments offer may sign it.
        if (ConsultantId is { } cid && Consultants.All(c => c.Id != cid))
        {
            Fail("That reporting consultant is not on the master list — pick one from the list.");
            return Page();
        }

        var pending = Tests.Where(t => !t.Verified).ToList();
        if (pending.Count == 0) { Fail("Everything on this order is already verified."); return Page(); }

        // US9.3 AC (spec 0044): a panic value must not be releasable with the same single click
        // as a normal one. The pathologist is the last person between this result and a patient
        // who goes home, so the refusal names the value rather than saying "tick the box".
        var criticals = Criticals;
        if (criticals.Count > 0 && !CriticalAcknowledged)
        {
            Fail(criticals.Count == 1
                ? $"This report carries a critical value — {criticals[0].Sentence}. "
                + "Confirm you have seen it before releasing the report."
                : $"This report carries {criticals.Count} critical values — "
                + string.Join("; ", criticals.Select(c => c.Sentence))
                + ". Confirm you have seen them before releasing the report.");
            return Page();
        }
        var ackNote = criticals.Count == 0 ? null
            : string.Join(" | ", criticals.Select(c => $"{c.TestName}: {c.Sentence}"));

        try
        {
            await tx.RunAsync(async s =>
            {
                foreach (var t in pending)
                {
                    await lis.VerifyAsync(s.Lis, t.ResultId, ActorId, "reporting_consultant");
                    if (ConsultantId is { } cid)
                    {
                        // The signature block that prints is a master reference, not free text —
                        // the report can always reproduce who signed it and their credentials.
                        var row = await s.Lis.Results.SingleAsync(r => r.Id == t.ResultId);
                        row.SignatureImageRef = $"consultant:{cid}";
                    }
                    if (ackNote is not null)
                    {
                        // Recorded on every version released under this acknowledgement, with the
                        // values it covered — what an audit asks is what was on screen, not that
                        // a box was ticked.
                        var row = await s.Lis.Results.SingleAsync(r => r.Id == t.ResultId);
                        row.CriticalAckBy = ActorId;
                        row.CriticalAckAt = DateTimeOffset.UtcNow;
                        row.CriticalAckNote = ackNote;
                    }
                }
                await s.Lis.SaveChangesAsync();

                await SmsSender.SendAsync(s, sms, BranchId, Hms.Notifications.Data.SmsEvent.ReportReady,
                    Selected.Phone, new Dictionary<string, string?>
                    { ["hospital"] = hospital.Name, ["patient"] = Selected!.PatientName, ["order"] = Selected.OrderNo });
                await s.Notif.SaveChangesAsync();
                return 0;
            });
        }
        catch (LisException e)
        {
            Fail(e.Message);
            return Page();
        }

        Toast($"{Selected.OrderNo} verified and e-signed by {ActorName} — patient notified", "verified");
        return Redirect($"/lis/report/{Selected.OrderId}");
    }
}
