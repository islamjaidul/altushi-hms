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

    // ---- spec 0057 (G49): the things that make this a document rather than a table of a run.
    public string Assignment { get; private set; } = "—";
    public long YtdGrossTaka { get; private set; }
    public long YtdDeductionTaka { get; private set; }
    public long YtdNetTaka { get; private set; }
    public long YtdTaxTaka { get; private set; }
    public IReadOnlyList<(string Type, decimal Available)> LeaveBalances { get; private set; } = [];
    public string PaymentMethodLabel { get; private set; } = "—";
    public string? BankAccount { get; private set; }

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

            // ---- year to date, over the same fiscal year the payslip's period falls in. Read from
            // locked runs only: a draft run's figures are not a year-to-date of anything.
            var yearStart = new DateOnly(
                Run.Period.Month >= 7 ? Run.Period.Year : Run.Period.Year - 1, 7, 1);

            var ytd = await (
                from l in s.Hr.PayrollLines.AsNoTracking()
                join r in s.Hr.PayrollRuns.AsNoTracking() on l.RunId equals r.Id
                where l.EmployeeId == Line.EmployeeId
                      && r.Period >= yearStart && r.Period <= Run.Period
                      && (r.State == PayrollRunState.Locked || r.State == PayrollRunState.Posted
                          || r.State == PayrollRunState.Disbursed)
                select new { l.Id, l.GrossEarningsTaka, l.TotalDeductionsTaka, l.NetPayTaka })
                .ToListAsync();

            YtdGrossTaka = ytd.Sum(x => x.GrossEarningsTaka);
            YtdDeductionTaka = ytd.Sum(x => x.TotalDeductionsTaka);
            YtdNetTaka = ytd.Sum(x => x.NetPayTaka);

            // Tax to date comes off the member ledger the run now writes (spec 0057), not off a
            // re-computation — the statement and the payslip must agree to the taka.
            YtdTaxTaka = await s.Hr.LedgerEntries.AsNoTracking()
                .Where(e => e.EmployeeId == Line.EmployeeId && e.Kind == LedgerKind.Tax
                            && e.OnDate >= yearStart)
                .SumAsync(e => e.EmployeeShareTaka);

            var leaveTypes = await s.Hr.LeaveTypes.AsNoTracking()
                .ToDictionaryAsync(t => t.Id, t => t.Name);
            LeaveBalances =
            [
                .. (await s.Hr.LeaveBalances.AsNoTracking()
                        .Where(b => b.EmployeeId == Line.EmployeeId)
                        .Select(b => new
                        {
                            b.LeaveTypeId,
                            Available = b.OpeningBp + b.AccruedBp + b.AdjustmentBp
                                        - b.AvailedBp - b.EncashedBp,
                        })
                        .ToListAsync())
                    .Select(b => (leaveTypes.GetValueOrDefault(b.LeaveTypeId, "—"), b.Available / 10000m))
                    .OrderBy(b => b.Item1),
            ];

            if (Person is not null)
            {
                PaymentMethodLabel = PaymentMethod.Label(Person.PaymentMethod);
                BankAccount = Person.PaymentMethod == PaymentMethod.Bank ? Person.BankAccountNo : null;

                var assignment = await s.Hr.Assignments.AsNoTracking()
                    .Where(a => a.EmployeeId == Person.Id && a.EffectiveFrom <= Run.Period
                                && (a.EffectiveTo == null || a.EffectiveTo >= Run.Period))
                    .OrderByDescending(a => a.EffectiveFrom)
                    .FirstOrDefaultAsync();

                if (assignment is not null)
                {
                    var unit = await s.Hr.OrgUnits.AsNoTracking()
                        .Where(u => u.Id == assignment.OrgUnitId).Select(u => u.Name).FirstOrDefaultAsync();
                    var designation = await s.Hr.Designations.AsNoTracking()
                        .Where(d => d.Id == assignment.DesignationId).Select(d => d.Name).FirstOrDefaultAsync();
                    Assignment = string.Join(" · ",
                        new[] { designation, unit }.Where(x => !string.IsNullOrWhiteSpace(x))!);
                }
            }
        });

        return Line is null ? NotFound() : Page();
    }
}
