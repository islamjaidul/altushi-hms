using Hms.Hr.Data;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record VarianceRow(
    long Id, long EmployeeId, string Code, string Name,
    long PreviousTaka, long CurrentTaka, int DeltaBp, string? Reason, string? ReviewedByName)
{
    public bool IsNew => DeltaBp == int.MaxValue;

    /// <summary>The movement in the operator's words. "New" rather than an infinite percentage.</summary>
    public string Words => IsNew
        ? "new this month"
        : DeltaBp == 0 ? "unchanged"
        : $"{(DeltaBp > 0 ? "+" : "")}{DeltaBp / 100m:0.##}%";

    public string Tone => IsNew ? "warn" : Math.Abs(DeltaBp) >= 5_000 ? "bad"
        : Math.Abs(DeltaBp) >= 1_000 ? "warn" : "good";
}

/// <summary>
/// §9's Variance Reviewed, the step between exceptions and approval (spec 0057, G47).
/// <para>
/// The module went straight from one to the other, so a run where somebody's pay tripled was
/// approved exactly as fast as a run where nothing moved. This is the screen that makes an approver
/// look — and it will not let the run past until every change outside the employer's tolerance has
/// been given a reason.
/// </para>
/// <para>
/// Entirely salary-bearing, so it is behind <c>hr.salary.read</c> on top of <c>hr.payroll.run</c>.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.PayrollRun)]
public class VarianceModel(IHrTx tx, PayrollService payroll, DisbursementService money)
    : HmsPageModel
{
    public PayrollRun? Run { get; private set; }
    public IReadOnlyList<VarianceRow> Rows { get; private set; } = [];
    public int ToleranceBp { get; private set; } = 2_000;
    public bool CanSeeMoney => Can("hr.salary.read");

    public int OutstandingCount => Rows.Count(r =>
        r.Reason is null && DisbursementService.NeedsReason(r.DeltaBp, ToleranceBp));

    public bool NeedsReason(VarianceRow row) =>
        DisbursementService.NeedsReason(row.DeltaBp, ToleranceBp);

    [BindProperty] public string? Reason { get; set; }

    public async Task<IActionResult> OnGetAsync(long id)
    {
        await LoadAsync(id);
        if (Run is null) return NotFound();
        return CanSeeMoney ? Page() : Forbid();
    }

    public async Task<IActionResult> OnPostExplainAsync(long id, long noteId)
    {
        if (!CanSeeMoney) return Forbid();

        try
        {
            await tx.RunAsync(s => money.ExplainAsync(
                s.Hr, s.Kernel, BranchId, noteId, Reason ?? "", ActorId, ActorName));
        }
        catch (HrException e)
        {
            await LoadAsync(id);
            Fail(e.Message);
            return Page();
        }

        Toast("Explained", "edit_note");
        return Redirect($"/hr/payroll/{id}/variance");
    }

    public async Task<IActionResult> OnPostAcceptAsync(long id)
    {
        if (!CanSeeMoney) return Forbid();

        try
        {
            await tx.RunAsync(s => payroll.ReviewVarianceAsync(
                s.Hr, s.Kernel, BranchId, id, ActorId, ActorName));
        }
        catch (HrException e)
        {
            await LoadAsync(id);
            Fail(e.Message);
            return Page();
        }

        Toast("Variance reviewed — the run can now be sent for approval", "fact_check");
        return Redirect("/hr/payroll");
    }

    private async Task LoadAsync(long id)
    {
        await tx.RunAsync(async s =>
        {
            Run = await s.Hr.PayrollRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id && r.BranchId == BranchId);
            if (Run is null) return;

            var periodEnd = Run.Period.AddMonths(1).AddDays(-1);
            var policy = await s.Hr.PayrollPolicies.AsNoTracking()
                .Where(p => p.BranchId == BranchId && p.EffectiveFrom <= periodEnd
                            && (p.EffectiveTo == null || p.EffectiveTo >= periodEnd))
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync();
            ToleranceBp = policy?.VarianceTolerationBp ?? 2_000;

            if (!CanSeeMoney) return;

            Rows =
            [
                .. await (
                    from v in s.Hr.VarianceNotes.AsNoTracking()
                    join e in s.Hr.Employees.AsNoTracking() on v.EmployeeId equals e.Id
                    where v.RunId == id
                    orderby v.DeltaBp descending
                    select new VarianceRow(
                        v.Id, e.Id, e.EmployeeCode, e.FullName,
                        v.PreviousNetTaka, v.CurrentNetTaka, v.DeltaBp, v.Reason, v.ReviewedByName))
                    .ToListAsync(),
            ];

            // Biggest movements first, in both directions: a 60% fall is as interesting as a rise.
            Rows = [.. Rows.OrderByDescending(r => r.IsNew ? int.MaxValue : Math.Abs(r.DeltaBp))
                .ThenBy(r => r.Name)];
        });
    }
}
