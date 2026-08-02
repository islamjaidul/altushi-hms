using Hms.Hr.Data;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record PayslipRow(
    long LineId, string EmployeeCode, string EmployeeName, string? PayslipNo,
    long Gross, long Deductions, long Net);

public sealed record RunPick(long Id, string RunNo, DateOnly Period, string State);

/// <summary>
/// Payslips for a locked or posted run (PRD §5 M16 [M], AUD-M16-09: <c>hr.payslip</c> had no
/// writer and no screen). Slip numbers are issued when the run posts; a locked run's lines are
/// already final — hard rule 4 — so the sheet can be read either way, and the header says which.
/// Behind <c>hr.salary.read</c>: a payslip is exactly the figure that permission protects.
/// </summary>
[Authorize(Policy = HrPerm.SalaryRead)]
public class PayslipsModel(IHrTx tx) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? RunId { get; set; }

    public IReadOnlyList<RunPick> Runs { get; private set; } = [];
    public RunPick? Selected { get; private set; }
    public IReadOnlyList<PayslipRow> Rows { get; private set; } = [];

    public async Task OnGetAsync() => await tx.RunAsync(async s =>
    {
        Runs = await s.Hr.PayrollRuns.AsNoTracking()
            .Where(r => r.BranchId == BranchId
                        && (r.State == PayrollRunState.Locked || r.State == PayrollRunState.Posted)
                        && r.Kind != PayrollRunKind.Reversal)
            .OrderByDescending(r => r.Period).ThenByDescending(r => r.Id)
            .Take(36)
            .Select(r => new RunPick(r.Id, r.RunNo, r.Period, r.State))
            .ToListAsync();

        Selected = Runs.FirstOrDefault(r => r.Id == RunId) ?? Runs.FirstOrDefault();
        if (Selected is null) return;

        var slips = await s.Hr.Payslips.AsNoTracking()
            .Where(p => p.BranchId == BranchId)
            .Join(s.Hr.PayrollLines.AsNoTracking().Where(l => l.RunId == Selected.Id),
                  p => p.PayrollLineId, l => l.Id, (p, l) => new { l.Id, p.PayslipNo })
            .ToDictionaryAsync(x => x.Id, x => x.PayslipNo);

        Rows = await s.Hr.PayrollLines.AsNoTracking()
            .Where(l => l.RunId == Selected.Id)
            .OrderBy(l => l.EmployeeCode)
            .Select(l => new PayslipRow(
                l.Id, l.EmployeeCode, l.EmployeeName, null,
                l.GrossEarningsTaka, l.TotalDeductionsTaka, l.NetPayTaka))
            .ToListAsync();
        Rows = [.. Rows.Select(r => r with { PayslipNo = slips.GetValueOrDefault(r.LineId) })];
    });
}
