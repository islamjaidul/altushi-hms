using Hms.Hr.Contracts;
using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record RunRow(
    long Id, string RunNo, DateOnly Period, string Kind, string State,
    int EmployeeCount, long TotalNet, int ExceptionCount, long? ReversalOf);

/// <summary>
/// Payroll runs, walking §11: Generated → Exceptions Reviewed → Approved⚿ → Locked → Posted.
/// <para>
/// Money totals are shown only to holders of <c>hr.salary.read</c>. The run's <em>state</em> is not
/// confidential — an HR officer must be able to see that March is locked — but its value is.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.PayrollRun)]
public class PayrollModel(IHrTx tx, PayrollService payroll, IPayrollPosting posting) : HmsPageModel
{
    [BindProperty] public string? Period { get; set; }

    public IReadOnlyList<RunRow> Runs { get; private set; } = [];
    public bool CanSeeMoney { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostGenerateAsync()
    {
        if (!FlexibleDate.TryParse(Period ?? "", out var period) || period == default)
        {
            await LoadAsync();
            Fail("Enter the payroll month, for example 01/03/2026.");
            return Page();
        }

        return await ActAsync(async s =>
        {
            var run = await payroll.GenerateAsync(
                s.Hr, s.Kernel, BranchId, period, ActorId, ActorName);
            return run.ExceptionCount > 0
                ? $"{run.RunNo} generated with {run.ExceptionCount} attendance exception(s) to review"
                : $"{run.RunNo} generated — nothing outstanding";
        });
    }

    public Task<IActionResult> OnPostReviewAsync(long id) => ActAsync(async s =>
    {
        await payroll.ReviewAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName);
        return "Exceptions reviewed — ready to send for approval";
    });

    public Task<IActionResult> OnPostRequestApprovalAsync(long id) => ActAsync(async s =>
    {
        var raise = await payroll.RequestApprovalAsync(
            s.Hr, s.Kernel, BranchId, id, ActorId, ActorName, ActorRole);
        return raise.AutoApproved
            ? "Approved under the standing threshold"
            : "Sent to Accounts / MD for approval";
    });

    /// <summary>§12 puts the lock approval with Accounts Manager / MD, never the requesting officer.</summary>
    public async Task<IActionResult> OnPostApproveAsync(long id)
    {
        if (!Can("hr.payroll.approve"))
            return Forbid();

        return await ActAsync(async s =>
        {
            await payroll.ApproveAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName);
            return "Payroll approved — it can now be locked";
        });
    }

    public Task<IActionResult> OnPostLockAsync(long id) => ActAsync(async s =>
    {
        var journal = await payroll.LockAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName);
        return $"Locked. The journal balances at {Ui.Money(journal.TotalDebit)} — it cannot be edited now, only reversed.";
    });

    public Task<IActionResult> OnPostPostAsync(long id) => ActAsync(async s =>
    {
        await payroll.PostAsync(s.Hr, s.Kernel, BranchId, id, posting, ActorId, ActorName);
        return "Posted. The journal is waiting for the ledger.";
    });

    public async Task<IActionResult> OnPostReverseAsync(long id, string reason)
    {
        if (!Can("hr.payroll.approve"))
            return Forbid();

        return await ActAsync(async s =>
        {
            var reversal = await payroll.ReverseAsync(
                s.Hr, s.Kernel, BranchId, id, reason, ActorId, ActorName);
            return $"Reversed by {reversal.RunNo}. The original stays on the record.";
        });
    }

    private async Task<IActionResult> ActAsync(Func<HrScope, Task<string>> body)
    {
        try
        {
            var message = await tx.RunAsync(body);
            Toast(message, "payments");
            return Redirect("/hr/payroll");
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
        CanSeeMoney = Can("hr.salary.read");

        await tx.RunAsync(async s =>
        {
            Runs = await s.Hr.PayrollRuns.AsNoTracking()
                .Where(r => r.BranchId == BranchId)
                .OrderByDescending(r => r.Period).ThenByDescending(r => r.Id)
                .Take(36)
                .Select(r => new RunRow(
                    r.Id, r.RunNo, r.Period, r.Kind, r.State,
                    r.EmployeeCount, r.TotalNetTaka, r.ExceptionCount, r.ReversalOfRunId))
                .ToListAsync();
        });
    }
}
