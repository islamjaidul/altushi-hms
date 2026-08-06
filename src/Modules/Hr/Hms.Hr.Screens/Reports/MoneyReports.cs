using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Reports;

/// <summary>
/// The registers spec 0055 deliberately deferred and spec 0057 makes possible (§13's Statutory &amp;
/// ledgers and the rest of Payroll &amp; compensation).
/// <para>
/// Every one of these was excluded from the Phase-1 report centre for the same stated reason: the
/// table behind it had no writer, so the report would have rendered permanently empty — and a blank
/// report reads as "nothing happened" rather than "not built". Loans now have a lifecycle, and the
/// payroll run now posts a member ledger entry for every PF and tax deduction it computes, so these
/// have something true to show.
/// </para>
/// <para>
/// All of them are salary-bearing except the loan register's outstanding figure, which is money the
/// employer is owed rather than money it pays — and it is still money, so it is gated too.
/// </para>
/// </summary>
internal static class Money
{
    internal static ReportCell Taka(long amount) =>
        new(amount == 0 ? "—" : Ui.Money(amount), Number: amount);

    /// <summary>The ledger movements of one kind in a period, per member.</summary>
    internal static IQueryable<EmployeeLedgerEntry> Ledger(ReportContext ctx, string kind) =>
        ctx.Scope.Hr.LedgerEntries.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && e.Kind == kind
                        && e.OnDate >= ctx.Period.Range.From && e.OnDate <= ctx.Period.Range.To);
}

/// <summary>§13 — "What is lent, what is recovered, what is left" (G41).</summary>
public sealed class LoanRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "loan-register",
        Name = "Loan & advance register",
        Question = "What has been lent, what has been recovered, and what is still outstanding",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Month,
        Landscape = true,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var rows = await (
            from l in ctx.Scope.Hr.Loans.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on l.EmployeeId equals e.Id
            where l.BranchId == ctx.BranchId
                  && (ctx.Filters.EmployeeId == null || l.EmployeeId == ctx.Filters.EmployeeId)
            orderby l.State, l.StartPeriod descending
            select new
            {
                l.LoanNo, l.Kind, l.PrincipalTaka, l.RecoveredTaka, l.InstallmentTaka,
                l.InstallmentCount, l.StartPeriod, l.State, l.Purpose,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = rows.Count;
        if (matched > ctx.MaxRows) rows = [.. rows.Take(ctx.MaxRows)];

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                "Nothing has been lent. A loan is raised on HR → Loans & advances; it recovers "
                + "itself through payroll once it is approved and disbursed.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Number"), new("Code"), new("Employee"), new("Kind"),
                new("Principal", ReportAlign.Right, Money: true),
                new("Recovered", ReportAlign.Right, Money: true),
                new("Outstanding", ReportAlign.Right, Money: true),
                new("Installment", ReportAlign.Right, Money: true),
                new("Starts"), new("State"),
            ],
            Rows =
            [
                .. rows.Select(r => new ReportRow(
                [
                    new ReportCell(r.LoanNo),
                    new ReportCell(r.EmployeeCode, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.FullName, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.Kind),
                    Money.Taka(r.PrincipalTaka),
                    Money.Taka(r.RecoveredTaka),
                    Money.Taka(r.PrincipalTaka - r.RecoveredTaka),
                    new ReportCell($"{Ui.Money(r.InstallmentTaka)} × {r.InstallmentCount}",
                        Number: r.InstallmentTaka),
                    new ReportCell(r.StartPeriod.ToString("MMM yyyy")),
                    new ReportCell(LoanState.Label(r.State),
                        PillCss: r.State switch
                        {
                            LoanState.Disbursed => "warn",
                            LoanState.Closed or LoanState.Foreclosed => "good",
                            LoanState.WrittenOff => "bad",
                            _ => "",
                        }),
                ])),
            ],
            MatchedRows = matched,
            Totals =
            [
                new ReportCell(""), new ReportCell(""),
                new ReportCell($"{rows.Count} loan{(rows.Count == 1 ? "" : "s")}"),
                new ReportCell(""),
                Money.Taka(rows.Sum(r => r.PrincipalTaka)),
                Money.Taka(rows.Sum(r => r.RecoveredTaka)),
                Money.Taka(rows.Sum(r => r.PrincipalTaka - r.RecoveredTaka)),
                new ReportCell(""), new ReportCell(""), new ReportCell(""),
            ],
        };
    }
}

/// <summary>
/// §13 — the installments payroll <b>could not</b> take, and why. An employer who never sees this
/// finds out at settlement that three years of recovery quietly stopped in month two.
/// </summary>
public sealed class DeferredRecoveryReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "deferred-recovery",
        Name = "Deferred recoveries",
        Question = "Which loan installments payroll could not take, and in which month",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        var rows = await (
            from i in ctx.Scope.Hr.LoanInstallments.AsNoTracking()
            join l in ctx.Scope.Hr.Loans.AsNoTracking() on i.LoanId equals l.Id
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on l.EmployeeId equals e.Id
            where i.Deferred && i.Period >= range.From && i.Period <= range.To
            orderby i.Period descending, e.EmployeeCode
            select new
            {
                i.Period, l.LoanNo, l.InstallmentTaka, l.PrincipalTaka, l.RecoveredTaka,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = rows.Count;
        if (matched > ctx.MaxRows) rows = [.. rows.Take(ctx.MaxRows)];

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                $"No installment was deferred in {PeriodWords.Describe(ctx.Period)}. An installment "
                + "is deferred when taking it would push net pay below the employer's minimum — it "
                + "is never simply skipped.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Month"), new("Loan"), new("Code"), new("Employee"),
                new("Installment", ReportAlign.Right, Money: true),
                new("Still outstanding", ReportAlign.Right, Money: true),
            ],
            Rows =
            [
                .. rows.Select(r => new ReportRow(
                [
                    new ReportCell(r.Period.ToString("MMM yyyy")),
                    new ReportCell(r.LoanNo),
                    new ReportCell(r.EmployeeCode, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.FullName, Href: ctx.Employee(r.EmployeeId)),
                    Money.Taka(r.InstallmentTaka),
                    Money.Taka(r.PrincipalTaka - r.RecoveredTaka),
                ])),
            ],
            MatchedRows = matched,
        };
    }
}

/// <summary>
/// §13 Statutory &amp; ledgers — one member statement per ledger kind (G42, G43, G44). The run now
/// posts an entry for every provident-fund and tax deduction it computes; before spec 0057 the money
/// was taken every month and the balance it was taken <em>into</em> did not exist.
/// </summary>
public abstract class LedgerStatementReport(string kind, string key, string name, string question)
    : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = key,
        Name = name,
        Question = question,
        Category = ReportCategory.Statutory,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.FinancialYear,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        // Opening balance is everything before the period; the statement then moves from it.
        var opening = await ctx.Scope.Hr.LedgerEntries.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && e.Kind == kind && e.OnDate < range.From)
            .GroupBy(e => e.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Employee = g.Sum(x => x.EmployeeShareTaka),
                Employer = g.Sum(x => x.EmployerShareTaka),
            })
            .ToDictionaryAsync(x => x.EmployeeId, x => (x.Employee, x.Employer), ct);

        var movement = await Money.Ledger(ctx, kind)
            .GroupBy(e => e.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Employee = g.Sum(x => x.EmployeeShareTaka),
                Employer = g.Sum(x => x.EmployerShareTaka),
            })
            .ToListAsync(ct);

        var ids = movement.Select(m => m.EmployeeId).Concat(opening.Keys).Distinct().ToList();
        if (ids.Count == 0)
        {
            return ReportTable.Empty(
                $"No {name.ToLowerInvariant()} movement exists for {PeriodWords.Describe(ctx.Period)}. "
                + "Entries are written by the payroll run itself, and only where the employer has "
                + "configured the policy that computes them.");
        }

        var people = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id)
                        && (ctx.Filters.EmployeeId == null || e.Id == ctx.Filters.EmployeeId))
            .OrderBy(e => e.EmployeeCode)
            .Select(e => new { e.Id, e.EmployeeCode, e.FullName })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var byEmployee = movement.ToDictionary(m => m.EmployeeId, m => (m.Employee, m.Employer));

        var matched = people.Count;
        if (matched > ctx.MaxRows) people = [.. people.Take(ctx.MaxRows)];

        if (people.Count == 0)
        {
            return ReportTable.Empty(
                "No member matches this selection. Remove the employee filter, or widen the period.");
        }

        var rows = people.Select(p =>
        {
            var (openEmployee, openEmployer) = opening.GetValueOrDefault(p.Id);
            var (moveEmployee, moveEmployer) = byEmployee.GetValueOrDefault(p.Id);

            return new ReportRow(
            [
                new ReportCell(p.EmployeeCode, Href: ctx.Employee(p.Id)),
                new ReportCell(p.FullName, Href: ctx.Employee(p.Id)),
                Money.Taka(openEmployee + openEmployer),
                Money.Taka(moveEmployee),
                Money.Taka(moveEmployer),
                Money.Taka(openEmployee + openEmployer + moveEmployee + moveEmployer),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Member"),
                new("Opening", ReportAlign.Right, Money: true),
                new("Employee share", ReportAlign.Right, Money: true),
                new("Employer share", ReportAlign.Right, Money: true),
                new("Closing", ReportAlign.Right, Money: true),
            ],
            Rows = rows,
            MatchedRows = matched,
            Totals =
            [
                new ReportCell(""),
                new ReportCell($"{rows.Count} member{(rows.Count == 1 ? "" : "s")}"),
                Money.Taka(rows.Sum(r => r.Cells[2].Number ?? 0)),
                Money.Taka(rows.Sum(r => r.Cells[3].Number ?? 0)),
                Money.Taka(rows.Sum(r => r.Cells[4].Number ?? 0)),
                Money.Taka(rows.Sum(r => r.Cells[5].Number ?? 0)),
            ],
        };
    }
}

public sealed class ProvidentFundStatementReport() : LedgerStatementReport(
    LedgerKind.ProvidentFund, "pf-statement", "Provident fund statement",
    "Each member's provident-fund balance and what moved it");

public sealed class WelfareStatementReport() : LedgerStatementReport(
    LedgerKind.Welfare, "welfare-statement", "Welfare fund statement",
    "Each member's welfare-fund balance and what moved it");

public sealed class AdvanceLedgerReport() : LedgerStatementReport(
    LedgerKind.Advance, "advance-ledger", "Recoverable advances",
    "What the employer is owed beyond loans — including carried payroll shortfalls");

/// <summary>
/// §13 — the monthly TDS register (G44). Read from the ledger the run writes rather than recomputed,
/// so the register, the payslip and the annual statement cannot disagree.
/// </summary>
public sealed class TaxDeductedReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "tds-register",
        Name = "Tax deducted at source",
        Question = "What was withheld from whom, and in which month",
        Category = ReportCategory.Statutory,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var rows = await (
            from l in Money.Ledger(ctx, LedgerKind.Tax)
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on l.EmployeeId equals e.Id
            where ctx.Filters.EmployeeId == null || l.EmployeeId == ctx.Filters.EmployeeId
            orderby l.OnDate descending, e.EmployeeCode
            select new
            {
                l.OnDate, l.Narration, l.EmployeeShareTaka,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName, e.Tin,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = rows.Count;
        if (matched > ctx.MaxRows) rows = [.. rows.Take(ctx.MaxRows)];

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                $"No tax was withheld in {PeriodWords.Describe(ctx.Period)}. Income tax computes "
                + "only where the employer has configured a slab table — this product ships none, "
                + "because it can cite no authoritative source for Bangladeshi rates.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Month"), new("Code"), new("Employee"), new("TIN"),
                new("Withheld", ReportAlign.Right, Money: true),
            ],
            Rows =
            [
                .. rows.Select(r => new ReportRow(
                [
                    new ReportCell(r.OnDate.ToString("MMM yyyy")),
                    new ReportCell(r.EmployeeCode, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.FullName, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.Tin ?? "—"),
                    Money.Taka(r.EmployeeShareTaka),
                ])),
            ],
            MatchedRows = matched,
            Totals =
            [
                new ReportCell(""), new ReportCell(""),
                new ReportCell($"{rows.Count} deduction{(rows.Count == 1 ? "" : "s")}"),
                new ReportCell(""),
                Money.Taka(rows.Sum(r => r.EmployeeShareTaka)),
            ],
        };
    }
}

/// <summary>§13 — the per-employee annual tax computation (G44).</summary>
public sealed class AnnualTaxStatementReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "annual-tax-statement",
        Name = "Annual tax computation",
        Question = "Each employee's taxable earnings for the year and the tax withheld against them",
        Category = ReportCategory.Statutory,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.FinancialYear,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        var earnings = await (
            from l in ctx.Scope.Hr.PayrollLines.AsNoTracking()
            join r in ctx.Scope.Hr.PayrollRuns.AsNoTracking() on l.RunId equals r.Id
            where r.BranchId == ctx.BranchId && r.Period >= range.From && r.Period <= range.To
                  && (r.State == PayrollRunState.Locked || r.State == PayrollRunState.Posted
                      || r.State == PayrollRunState.Disbursed)
            group l by l.EmployeeId into g
            select new
            {
                EmployeeId = g.Key,
                Gross = g.Sum(x => x.GrossEarningsTaka),
                Net = g.Sum(x => x.NetPayTaka),
                Months = g.Count(),
            })
            .ToListAsync(ct);

        if (earnings.Count == 0)
        {
            return ReportTable.Empty(
                $"No locked payroll run falls in {PeriodWords.Describe(ctx.Period)}. The annual "
                + "statement is built from runs that have actually been locked, never from drafts.");
        }

        var withheld = await ctx.Scope.Hr.LedgerEntries.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && e.Kind == LedgerKind.Tax
                        && e.OnDate >= range.From && e.OnDate <= range.To)
            .GroupBy(e => e.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Tax = g.Sum(x => x.EmployeeShareTaka) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Tax, ct);

        var ids = earnings.Select(e => e.EmployeeId).ToList();
        var people = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id)
                        && (ctx.Filters.EmployeeId == null || e.Id == ctx.Filters.EmployeeId))
            .OrderBy(e => e.EmployeeCode)
            .Select(e => new { e.Id, e.EmployeeCode, e.FullName, e.Tin })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var byEmployee = earnings.ToDictionary(e => e.EmployeeId);
        var matched = people.Count;
        if (matched > ctx.MaxRows) people = [.. people.Take(ctx.MaxRows)];

        if (people.Count == 0)
            return ReportTable.Empty("No employee matches this selection.");

        var rows = people.Select(p =>
        {
            var e = byEmployee[p.Id];
            var tax = withheld.GetValueOrDefault(p.Id);
            return new ReportRow(
            [
                new ReportCell(p.EmployeeCode, Href: ctx.Employee(p.Id)),
                new ReportCell(p.FullName, Href: ctx.Employee(p.Id)),
                new ReportCell(p.Tin ?? "—"),
                new ReportCell(e.Months.ToString(), Number: e.Months),
                Money.Taka(e.Gross),
                Money.Taka(tax),
                Money.Taka(e.Net),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Employee"), new("TIN"),
                new("Months", ReportAlign.Right),
                new("Gross earnings", ReportAlign.Right, Money: true),
                new("Tax withheld", ReportAlign.Right, Money: true),
                new("Net paid", ReportAlign.Right, Money: true),
            ],
            Rows = rows,
            MatchedRows = matched,
            Totals =
            [
                new ReportCell(""),
                new ReportCell($"{rows.Count} employee{(rows.Count == 1 ? "" : "s")}"),
                new ReportCell(""), new ReportCell(""),
                Money.Taka(rows.Sum(r => r.Cells[4].Number ?? 0)),
                Money.Taka(rows.Sum(r => r.Cells[5].Number ?? 0)),
                Money.Taka(rows.Sum(r => r.Cells[6].Number ?? 0)),
            ],
        };
    }
}

/// <summary>§13 — the bonus register (G38).</summary>
public sealed class BonusRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "bonus-register",
        Name = "Bonus register",
        Question = "What bonus was paid, to how many people, and who was excluded",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Year,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        var sheets = await ctx.Scope.Hr.BonusSheets.AsNoTracking()
            .Where(b => b.BranchId == ctx.BranchId && b.Period >= range.From && b.Period <= range.To)
            .OrderByDescending(b => b.Period)
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = sheets.Count;
        if (matched > ctx.MaxRows) sheets = [.. sheets.Take(ctx.MaxRows)];

        if (sheets.Count == 0)
        {
            return ReportTable.Empty(
                $"No bonus sheet falls in {PeriodWords.Describe(ctx.Period)}. A sheet is drafted on "
                + "HR → Bonus & increments and lists everybody, including the people who did not "
                + "qualify and why.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Number"), new("Bonus"), new("Kind"), new("Period"),
                new("Eligible", ReportAlign.Right), new("Excluded", ReportAlign.Right),
                new("Total", ReportAlign.Right, Money: true), new("State"),
            ],
            Rows =
            [
                .. sheets.Select(b => new ReportRow(
                [
                    new ReportCell(b.SheetNo),
                    new ReportCell(b.Title),
                    new ReportCell(BonusKind.Label(b.Kind)),
                    new ReportCell(b.Period.ToString("MMM yyyy")),
                    new ReportCell(b.EligibleCount.ToString(), Number: b.EligibleCount),
                    new ReportCell(b.ExcludedCount.ToString(), Number: b.ExcludedCount),
                    Money.Taka(b.TotalTaka),
                    new ReportCell(BonusState.Label(b.State),
                        PillCss: b.State == BonusState.Paid ? "good" : "warn"),
                ])),
            ],
            MatchedRows = matched,
            Totals =
            [
                new ReportCell(""), new ReportCell(""), new ReportCell(""),
                new ReportCell($"{sheets.Count} sheet{(sheets.Count == 1 ? "" : "s")}"),
                new ReportCell(sheets.Sum(b => b.EligibleCount).ToString(),
                    Number: sheets.Sum(b => b.EligibleCount)),
                new ReportCell(sheets.Sum(b => b.ExcludedCount).ToString(),
                    Number: sheets.Sum(b => b.ExcludedCount)),
                Money.Taka(sheets.Sum(b => b.TotalTaka)),
                new ReportCell(""),
            ],
        };
    }
}

/// <summary>§13 — the increment and promotion register (G39, G40).</summary>
public sealed class IncrementRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "increment-register",
        Name = "Increment & promotion register",
        Question = "Whose pay was raised, by how much, and under which run",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Year,
        Landscape = true,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        var rows = await (
            from l in ctx.Scope.Hr.CompensationLines.AsNoTracking()
            join r in ctx.Scope.Hr.CompensationRuns.AsNoTracking() on l.RunId equals r.Id
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on l.EmployeeId equals e.Id
            where r.BranchId == ctx.BranchId && l.Applied
                  && r.EffectiveFrom >= range.From && r.EffectiveFrom <= range.To
                  && (ctx.Filters.EmployeeId == null || l.EmployeeId == ctx.Filters.EmployeeId)
            orderby r.EffectiveFrom descending, e.EmployeeCode
            select new
            {
                r.RunNo, r.Kind, r.Title, r.EffectiveFrom,
                l.OldGrossTaka, l.NewGrossTaka, l.Basis,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = rows.Count;
        if (matched > ctx.MaxRows) rows = [.. rows.Take(ctx.MaxRows)];

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                $"No increment or promotion was applied in {PeriodWords.Describe(ctx.Period)}. "
                + "A run appears here once it has been approved and applied, not while it is a "
                + "proposal.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Run"), new("Kind"), new("Effective"), new("Code"), new("Employee"),
                new("Was", ReportAlign.Right, Money: true),
                new("Now", ReportAlign.Right, Money: true),
                new("Rise", ReportAlign.Right, Money: true),
                new("Basis"),
            ],
            Rows =
            [
                .. rows.Select(r => new ReportRow(
                [
                    new ReportCell(r.RunNo),
                    new ReportCell(CompensationRunKind.Label(r.Kind)),
                    new ReportCell(Ui.DateLong(r.EffectiveFrom)),
                    new ReportCell(r.EmployeeCode, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.FullName, Href: ctx.Employee(r.EmployeeId)),
                    Money.Taka(r.OldGrossTaka),
                    Money.Taka(r.NewGrossTaka),
                    Money.Taka(r.NewGrossTaka - r.OldGrossTaka),
                    new ReportCell(r.Basis ?? "—"),
                ])),
            ],
            MatchedRows = matched,
            Totals =
            [
                new ReportCell(""), new ReportCell(""), new ReportCell(""), new ReportCell(""),
                new ReportCell($"{rows.Count} change{(rows.Count == 1 ? "" : "s")}"),
                Money.Taka(rows.Sum(r => r.OldGrossTaka)),
                Money.Taka(rows.Sum(r => r.NewGrossTaka)),
                Money.Taka(rows.Sum(r => r.NewGrossTaka - r.OldGrossTaka)),
                new ReportCell(""),
            ],
        };
    }
}

/// <summary>§13 — the disbursement register (G45): who has actually been paid, and how.</summary>
public sealed class DisbursementRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "disbursement-register",
        Name = "Disbursement register",
        Question = "Who has actually been paid, by what method, and who is still outstanding",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Month,
        Landscape = true,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        var rows = await (
            from l in ctx.Scope.Hr.DisbursementLines.AsNoTracking()
            join d in ctx.Scope.Hr.Disbursements.AsNoTracking() on l.DisbursementId equals d.Id
            where d.BranchId == ctx.BranchId
                  && d.ValueDate >= range.From && d.ValueDate <= range.To
                  && (ctx.Filters.EmployeeId == null || l.EmployeeId == ctx.Filters.EmployeeId)
            orderby d.ValueDate descending, l.EmployeeCode
            select new
            {
                d.BatchNo, d.ValueDate, l.EmployeeId, l.EmployeeCode, l.EmployeeName,
                l.Method, l.AmountTaka, l.Reference, l.PaidAt, l.PaidByName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = rows.Count;
        if (matched > ctx.MaxRows) rows = [.. rows.Take(ctx.MaxRows)];

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                $"Nothing was disbursed in {PeriodWords.Describe(ctx.Period)}. A batch is prepared "
                + "from a locked run on the run's own disbursement screen.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Batch"), new("Value date"), new("Code"), new("Employee"), new("Method"),
                new("Amount", ReportAlign.Right, Money: true),
                new("Reference"), new("Paid"),
            ],
            Rows =
            [
                .. rows.Select(r => new ReportRow(
                [
                    new ReportCell(r.BatchNo),
                    new ReportCell(Ui.DateLong(r.ValueDate)),
                    new ReportCell(r.EmployeeCode, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.EmployeeName, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(PaymentMethod.Label(r.Method)),
                    Money.Taka(r.AmountTaka),
                    new ReportCell(r.Reference ?? "—"),
                    new ReportCell(r.PaidAt is null ? "outstanding" : (r.PaidByName ?? "paid"),
                        PillCss: r.PaidAt is null ? "bad" : "good"),
                ])),
            ],
            MatchedRows = matched,
            Totals =
            [
                new ReportCell(""), new ReportCell(""), new ReportCell(""),
                new ReportCell($"{rows.Count} payment{(rows.Count == 1 ? "" : "s")}"),
                new ReportCell(""),
                Money.Taka(rows.Sum(r => r.AmountTaka)),
                new ReportCell(""),
                new ReportCell($"{rows.Count(r => r.PaidAt is null)} outstanding"),
            ],
        };
    }
}

/// <summary>§13 — who is being held, and why (G46).</summary>
public sealed class SalaryHoldReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "salary-holds",
        Name = "Salary holds",
        Question = "Whose salary is being withheld, since when, and why",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var rows = await (
            from h in ctx.Scope.Hr.SalaryHolds.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on h.EmployeeId equals e.Id
            where h.BranchId == ctx.BranchId
                  && (ctx.Filters.EmployeeId == null || h.EmployeeId == ctx.Filters.EmployeeId)
            orderby h.ReleasedAt == null descending, h.HeldAt descending
            select new
            {
                h.Reason, h.HeldAt, h.HeldByName, h.ReleasedAt, h.ReleaseReason,
                EmployeeId = e.Id, e.EmployeeCode, e.FullName,
            })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        var matched = rows.Count;
        if (matched > ctx.MaxRows) rows = [.. rows.Take(ctx.MaxRows)];

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                "Nobody's salary is held, and none has been. A hold is placed from the payroll "
                + "screen and always carries a reason — a held person still appears on the salary "
                + "sheet, marked, rather than vanishing from it.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Employee"), new("Reason"), new("Held from"),
                new("By"), new("Released"),
            ],
            Rows =
            [
                .. rows.Select(r => new ReportRow(
                [
                    new ReportCell(r.EmployeeCode, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.FullName, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.Reason),
                    new ReportCell(Ui.DateLong(DateOnly.FromDateTime(Ui.Local(r.HeldAt).DateTime))),
                    new ReportCell(r.HeldByName),
                    new ReportCell(
                        r.ReleasedAt is null ? "still held"
                            : Ui.DateLong(DateOnly.FromDateTime(Ui.Local(r.ReleasedAt.Value).DateTime)),
                        PillCss: r.ReleasedAt is null ? "bad" : "good"),
                ])),
            ],
            MatchedRows = matched,
        };
    }
}
