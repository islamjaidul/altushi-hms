using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record LoanRow(
    long Id, string LoanNo, string Kind, long EmployeeId, string Code, string Name,
    long PrincipalTaka, long RecoveredTaka, long InstallmentTaka, int InstallmentCount,
    DateOnly StartPeriod, string State, string? Purpose, long? ApprovalRequestId)
{
    public long OutstandingTaka => PrincipalTaka - RecoveredTaka;

    public int PercentRecovered => PrincipalTaka <= 0 ? 0
        : (int)(RecoveredTaka * 100 / PrincipalTaka);
}

/// <summary>
/// Loans and advances (module PRD §7.9, G41).
/// <para>
/// <c>hr.loan</c> and <c>hr.loan_installment</c> shipped with the module and had no screen, no
/// service and no recovery — two tables that nothing had ever written a row into. This is the door.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.SalaryRead)]
public class LoansModel(IHrTx tx, LoanService loans) : HmsPageModel
{
    public IReadOnlyList<LoanRow> Rows { get; private set; } = [];
    public List<SelectListItem> People { get; private set; } = [];
    public long TotalOutstanding { get; private set; }
    public int DeferredThisYear { get; private set; }

    /// <summary>§11 names <c>hr.loan.manage</c> for the loan lifecycle.</summary>
    public bool CanManage => Can("hr.loan.manage");

    [BindProperty] public long EmployeeId { get; set; }
    [BindProperty] public string Kind { get; set; } = "loan";
    [BindProperty] public long PrincipalTaka { get; set; }
    [BindProperty] public int InstallmentCount { get; set; } = 12;
    [BindProperty] public string? StartPeriod { get; set; }
    [BindProperty] public string? Purpose { get; set; }
    [BindProperty] public string? Reason { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostRequestAsync()
    {
        if (!CanManage) return Forbid();

        var start = FlexibleDate.TryParse(StartPeriod ?? "", out var d) && d != default
            ? d : Dhaka.Today(TimeProvider.System).AddMonths(1);

        return await ActAsync(async s =>
        {
            var loan = await loans.RequestAsync(
                s.Hr, s.Kernel, BranchId, EmployeeId, Kind, PrincipalTaka, InstallmentCount,
                start, Purpose, ActorId, ActorName);
            return $"{loan.LoanNo} requested — it recovers nothing until it is approved and disbursed";
        });
    }

    public async Task<IActionResult> OnPostSendForApprovalAsync(long id)
    {
        if (!CanManage) return Forbid();

        return await ActAsync(async s =>
        {
            var raise = await loans.RaiseApprovalAsync(
                s.Hr, s.Kernel, BranchId, id, ActorId, ActorRole);
            return raise.AutoApproved
                ? "Approved under the standing threshold"
                : "Sent for approval";
        });
    }

    public async Task<IActionResult> OnPostApplyApprovalAsync(long id)
    {
        if (!CanManage) return Forbid();
        return await ActAsync(async s =>
        {
            await loans.ApplyApprovalAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName);
            return "Approval applied — the money can be disbursed";
        });
    }

    public async Task<IActionResult> OnPostDisburseAsync(long id)
    {
        if (!CanManage) return Forbid();
        return await ActAsync(async s =>
        {
            await loans.DisburseAsync(s.Hr, s.Kernel, BranchId, id, ActorId, ActorName);
            return "Disbursed — payroll starts recovering from the start period";
        });
    }

    public async Task<IActionResult> OnPostForecloseAsync(long id)
    {
        if (!CanManage) return Forbid();
        return await ActAsync(async s =>
        {
            await loans.ForecloseAsync(
                s.Hr, s.Kernel, BranchId, id, Dhaka.Today(TimeProvider.System), ActorId, ActorName);
            return "Settled in full";
        });
    }

    public async Task<IActionResult> OnPostWriteOffAsync(long id)
    {
        if (!CanManage) return Forbid();
        return await ActAsync(async s =>
        {
            await loans.WriteOffAsync(s.Hr, s.Kernel, BranchId, id, Reason ?? "", ActorId, ActorName);
            return "Written off — the record keeps the principal, the recovery and the reason";
        });
    }

    private async Task<IActionResult> ActAsync(Func<HrScope, Task<string>> body)
    {
        try
        {
            var message = await tx.RunAsync(body);
            Toast(message, "savings");
            return Redirect("/hr/loans");
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
        await tx.RunAsync(async s =>
        {
            Rows =
            [
                .. await (
                    from l in s.Hr.Loans.AsNoTracking()
                    join e in s.Hr.Employees.AsNoTracking() on l.EmployeeId equals e.Id
                    orderby l.State, l.StartPeriod descending
                    select new LoanRow(
                        l.Id, l.LoanNo, l.Kind, e.Id, e.EmployeeCode, e.FullName,
                        l.PrincipalTaka, l.RecoveredTaka, l.InstallmentTaka, l.InstallmentCount,
                        l.StartPeriod, l.State, l.Purpose, l.ApprovalRequestId))
                    .Take(300)
                    .ToListAsync(),
            ];

            TotalOutstanding = Rows.Where(r => r.State == LoanState.Disbursed)
                .Sum(r => r.OutstandingTaka);

            var yearStart = new DateOnly(Dhaka.Today(TimeProvider.System).Year, 1, 1);
            DeferredThisYear = await s.Hr.LoanInstallments.AsNoTracking()
                .CountAsync(i => i.Deferred && i.Period >= yearStart);

            People = await s.Hr.Employees.AsNoTracking()
                .Where(e => e.BranchId == BranchId && e.SeparatedOn == null)
                .OrderBy(e => e.EmployeeCode)
                .Select(e => new SelectListItem(e.EmployeeCode + " — " + e.FullName, e.Id.ToString()))
                .ToListAsync();
        });
    }
}
