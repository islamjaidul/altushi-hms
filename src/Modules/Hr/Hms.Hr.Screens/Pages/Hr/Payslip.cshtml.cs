using Hms.Hr.Data;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

/// <summary>
/// One employee's payslip for one run — the printable document (PRD §5 M16 [M], AUD-M16-09).
/// Browser-print HTML like every other document in the product; the component codes and names
/// were snapshotted onto the line when the run generated, so this sheet reproduces even after
/// components are renamed (hard rule 5).
/// </summary>
[Authorize(Policy = HrPerm.SalaryRead)]
public class PayslipModel(IHrTx tx) : HmsPageModel
{
    public PayrollLine? Line { get; private set; }
    public PayrollRun? Run { get; private set; }
    public Payslip? Slip { get; private set; }
    public Employee? Person { get; private set; }
    public IReadOnlyList<PayrollComponentLine> Earnings { get; private set; } = [];
    public IReadOnlyList<PayrollComponentLine> Deductions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id)
    {
        await tx.RunAsync(async s =>
        {
            Line = await s.Hr.PayrollLines.AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id && l.BranchId == BranchId);
            if (Line is null) return;

            Run = await s.Hr.PayrollRuns.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == Line.RunId);

            // A payslip is a document of a final figure. Before lock the numbers can still
            // change, so the sheet refuses to exist yet (the list screen says the same).
            if (Run is null || Run.State is not (PayrollRunState.Locked or PayrollRunState.Posted))
            {
                Line = null;
                return;
            }

            Slip = await s.Hr.Payslips.AsNoTracking()
                .FirstOrDefaultAsync(p => p.PayrollLineId == Line.Id);
            Person = await s.Hr.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == Line.EmployeeId);

            var components = await s.Hr.PayrollComponentLines.AsNoTracking()
                .Where(c => c.PayrollLineId == Line.Id)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();
            Earnings = [.. components.Where(c => c.Kind == PayComponentKind.Earning)];
            Deductions = [.. components.Where(c => c.Kind != PayComponentKind.Earning)];
        });

        return Line is null ? NotFound() : Page();
    }
}
