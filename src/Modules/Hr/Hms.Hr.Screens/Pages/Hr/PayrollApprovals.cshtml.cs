using Hms.Hr.Data;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record ApprovalRow(
    long Id, string RunNo, DateOnly Period, string State, int EmployeeCount,
    long TotalNet, int ExceptionCount, DateTimeOffset GeneratedAt);

/// <summary>
/// The approver's own door into payroll (AUD-AUZ-01). §12 makes the Accounts Manager / MD the
/// payroll approver and deliberately does NOT give them <c>hr.payroll.run</c> — but
/// <c>/hr/payroll</c>'s page policy is exactly that, so the only account that could approve a run
/// was the one that generated it and maker-checker was unusable. This page carries
/// <c>hr.payroll.approve</c> as its policy: an approver reaches the decision without gaining the
/// ability to generate, and the generator's screen is unchanged.
/// </summary>
[Authorize(Policy = HrPerm.PayrollApprove)]
public class PayrollApprovalsModel(IHrTx tx, PayrollService payroll) : HmsPageModel
{
    public IReadOnlyList<ApprovalRow> Waiting { get; private set; } = [];
    public IReadOnlyList<ApprovalRow> Recent { get; private set; } = [];
    public bool CanSeeMoney { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public Task<IActionResult> OnPostApproveAsync(long id) => ActAsync(async s =>
    {
        await payroll.ApproveAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName);
        return "Payroll approved — the HR officer can now lock it";
    });

    public async Task<IActionResult> OnPostReverseAsync(long id, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            await LoadAsync();
            Fail("A reversal needs a reason — it is a permanent part of the record.");
            return Page();
        }

        return await ActAsync(async s =>
        {
            var reversal = await payroll.ReverseAsync(
                s.Hr, s.Kernel, BranchId, id, reason!, ActorId, ActorName);
            return $"Reversed by {reversal.RunNo}. The original stays on the record.";
        });
    }

    private async Task<IActionResult> ActAsync(Func<HrScope, Task<string>> body)
    {
        try
        {
            var message = await tx.RunAsync(body);
            Toast(message, "task_alt");
            return Redirect("/hr/payroll/approvals");
        }
        catch (HrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    private async Task LoadAsync()
    {
        CanSeeMoney = Can(HrPerm.Claim.SalaryRead);

        await tx.RunAsync(async s =>
        {
            var runs = await s.Hr.PayrollRuns.AsNoTracking()
                .Where(r => r.BranchId == BranchId)
                .OrderByDescending(r => r.Period).ThenByDescending(r => r.Id)
                .Take(36)
                .Select(r => new ApprovalRow(
                    r.Id, r.RunNo, r.Period, r.State, r.EmployeeCount,
                    r.TotalNetTaka, r.ExceptionCount, r.GeneratedAt))
                .ToListAsync();

            Waiting = [.. runs.Where(r => r.State == PayrollRunState.ExceptionsReviewed)];
            Recent = [.. runs.Where(r => r.State != PayrollRunState.ExceptionsReviewed
                                         && r.State != PayrollRunState.Generated)];
        });
    }
}
