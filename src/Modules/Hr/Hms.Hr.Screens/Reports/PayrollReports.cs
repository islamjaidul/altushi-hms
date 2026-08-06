using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Reports;

/// <summary>
/// §13 Payroll &amp; compensation — every one of these is salary-bearing, so every one requires
/// <c>hr.reports.salary</c>. The page refuses before <c>BuildAsync</c> is ever called, which is why
/// these classes contain no permission code: a reader without the grant causes no query to run
/// against <c>hr.payroll_line</c> at all (D6, N-HR8).
/// </summary>
internal static class Pay
{
    /// <summary>The runs whose period falls in the selection. A cancelled run is not payroll.</summary>
    internal static IQueryable<PayrollRun> Runs(ReportContext ctx)
    {
        var (from, to) = (ctx.Period.Range.From, ctx.Period.Range.To);
        return ctx.Scope.Hr.PayrollRuns.AsNoTracking()
            .Where(r => r.BranchId == ctx.BranchId && r.Period >= from && r.Period <= to
                        && r.State != PayrollRunState.Cancelled);
    }

    internal static string StateWord(string state) => state switch
    {
        PayrollRunState.Generated => "Generated",
        PayrollRunState.ExceptionsReviewed => "Exceptions reviewed",
        PayrollRunState.Approved => "Approved",
        PayrollRunState.Locked => "Locked",
        PayrollRunState.Posted => "Posted",
        PayrollRunState.Cancelled => "Cancelled",
        _ => state,
    };

    internal static string StatePill(string state) => state switch
    {
        PayrollRunState.Posted or PayrollRunState.Locked => "good",
        PayrollRunState.Cancelled => "bad",
        _ => "warn",
    };

    internal static string KindWord(string kind) => kind switch
    {
        PayrollRunKind.Regular => "Regular",
        PayrollRunKind.Reversal => "Reversal",
        PayrollRunKind.Supplementary => "Supplementary",
        _ => kind,
    };

    /// <summary>The empty state every payroll report shares — one place, one sentence.</summary>
    internal static ReportTable NoRuns(ReportContext ctx) => ReportTable.Empty(
        $"No payroll run covers {PeriodWords.Range(ctx.Period.Range)}. "
        + "A run is created for a month on the Payroll runs screen; until then there are no figures "
        + "to report. Step back a month with the arrow to see the last one.");
}

/// <summary>§13 Payroll — "The run, in full, per employee per component".</summary>
public sealed class SalarySheetReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "salary-sheet",
        Name = "Salary sheet",
        Question = "The run in full — every employee, gross, deductions and net",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Month,
        Landscape = true,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        var runs = await Pay.Runs(ctx)
            .Select(r => new { r.Id, r.RunNo, r.Period, r.State })
            .ToListAsync(ct);
        if (runs.Count == 0) return Pay.NoRuns(ctx);

        var runIds = runs.Select(r => r.Id).ToList();
        var query = ctx.Scope.Hr.PayrollLines.AsNoTracking().Where(l => runIds.Contains(l.RunId));
        if (ctx.Filters.EmployeeId is { } id) query = query.Where(l => l.EmployeeId == id);
        if (ctx.Filters.OrgUnitIds.Count > 0)
            query = query.Where(l => l.OrgUnitId != null && ctx.Filters.OrgUnitIds.Contains(l.OrgUnitId.Value));
        if (ctx.Filters.GradeId is { } g) query = query.Where(l => l.GradeId == g);
        if (ctx.Filters.DesignationId is { } d) query = query.Where(l => l.DesignationId == d);

        var lines = await query.OrderBy(l => l.EmployeeCode).Take(ctx.MaxRows + 1).ToListAsync(ct);

        if (lines.Count == 0)
        {
            return ReportTable.Empty(
                $"The run for {PeriodWords.Range(ctx.Period.Range)} has no lines matching these "
                + "filters. Remove a filter, or check the run was generated with employees on it.");
        }

        var matched = lines.Count;
        if (matched > ctx.MaxRows) lines = [.. lines.Take(ctx.MaxRows)];

        var runOf = runs.ToDictionary(r => r.Id);
        var rows = lines.Select(l => new ReportRow(
        [
            new ReportCell(l.EmployeeCode, Href: ctx.Employee(l.EmployeeId)),
            // The line snapshots the name, so a payslip reprinted next year still says who it paid.
            new ReportCell(l.EmployeeName, Href: ctx.Employee(l.EmployeeId)),
            new ReportCell(lookups.Unit(l.OrgUnitId)),
            new ReportCell(lookups.Designation(l.DesignationId)),
            new ReportCell(LeaveText.Days(l.PayableDaysBp), l.PayableDaysBp),
            new ReportCell(Ui.Group(l.GrossEarningsTaka), l.GrossEarningsTaka),
            new ReportCell(Ui.Group(l.TotalDeductionsTaka), l.TotalDeductionsTaka),
            new ReportCell(Ui.Group(l.NetPayTaka), l.NetPayTaka),
            new ReportCell(l.CarriedShortfallTaka == 0 ? "—" : Ui.Group(l.CarriedShortfallTaka),
                l.CarriedShortfallTaka == 0 ? null : l.CarriedShortfallTaka,
                PillCss: l.CarriedShortfallTaka > 0 ? "warn" : null),
            new ReportCell(runOf.TryGetValue(l.RunId, out var r) ? r.RunNo : "—"),
        ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Unit"), new("Designation"),
                new("Payable days", ReportAlign.Right),
                new("Gross", ReportAlign.Right, Money: true),
                new("Deductions", ReportAlign.Right, Money: true),
                new("Net pay", ReportAlign.Right, Money: true),
                new("Carried shortfall", ReportAlign.Right, Money: true),
                new("Run"),
            ],
            Rows = rows,
            MatchedRows = matched,
            Totals =
            [
                new ReportCell("Total"), new ReportCell(""), new ReportCell(""), new ReportCell(""),
                new ReportCell(""),
                new ReportCell(Ui.Group(lines.Sum(l => l.GrossEarningsTaka)), lines.Sum(l => l.GrossEarningsTaka)),
                new ReportCell(Ui.Group(lines.Sum(l => l.TotalDeductionsTaka)), lines.Sum(l => l.TotalDeductionsTaka)),
                new ReportCell(Ui.Group(lines.Sum(l => l.NetPayTaka)), lines.Sum(l => l.NetPayTaka)),
                new ReportCell(Ui.Group(lines.Sum(l => l.CarriedShortfallTaka))),
                new ReportCell(""),
            ],
        };
    }
}

/// <summary>§13 Payroll — "Where the cost sits" (US16.14, the board's question).</summary>
public sealed class SalarySummaryByUnitReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "salary-summary-by-unit",
        Name = "Salary summary by unit",
        Question = "Where the salary cost sits, by department",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        var runIds = await Pay.Runs(ctx).Select(r => r.Id).ToListAsync(ct);
        if (runIds.Count == 0) return Pay.NoRuns(ctx);

        var byUnit = await ctx.Scope.Hr.PayrollLines.AsNoTracking()
            .Where(l => runIds.Contains(l.RunId))
            .GroupBy(l => l.OrgUnitId)
            .Select(g => new
            {
                OrgUnitId = g.Key,
                People = g.Count(),
                Gross = g.Sum(x => x.GrossEarningsTaka),
                Deductions = g.Sum(x => x.TotalDeductionsTaka),
                Net = g.Sum(x => x.NetPayTaka),
                EmployerCost = g.Sum(x => x.EmployerCostTaka),
            })
            .ToListAsync(ct);

        if (byUnit.Count == 0)
        {
            return ReportTable.Empty(
                $"The run for {PeriodWords.Range(ctx.Period.Range)} has no lines. "
                + "Generate it on the Payroll runs screen.");
        }

        var rows = byUnit.OrderByDescending(x => x.Gross).Select(x => new ReportRow(
        [
            new ReportCell(lookups.Unit(x.OrgUnitId)),
            new ReportCell(x.People.ToString("N0"), x.People,
                x.OrgUnitId is { } u ? ctx.Drill("salary-sheet", ("unit", u.ToString())) : null),
            new ReportCell(Ui.Group(x.Gross), x.Gross,
                x.OrgUnitId is { } u2 ? ctx.Drill("salary-sheet", ("unit", u2.ToString())) : null),
            new ReportCell(Ui.Group(x.Deductions), x.Deductions),
            new ReportCell(Ui.Group(x.Net), x.Net),
            // The employer's true cost — gross plus employer contributions (§13 "Employer cost").
            new ReportCell(Ui.Group(x.EmployerCost), x.EmployerCost),
            new ReportCell(x.People == 0 ? "—" : Ui.Group(x.Gross / x.People), x.People == 0 ? null : x.Gross / x.People),
        ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Unit"), new("Employees", ReportAlign.Right),
                new("Gross", ReportAlign.Right, Money: true),
                new("Deductions", ReportAlign.Right, Money: true),
                new("Net pay", ReportAlign.Right, Money: true),
                new("Employer cost", ReportAlign.Right, Money: true),
                new("Average gross", ReportAlign.Right, Money: true),
            ],
            Rows = rows,
            MatchedRows = byUnit.Count,
            Totals =
            [
                new ReportCell("Total"),
                new ReportCell(byUnit.Sum(x => x.People).ToString("N0")),
                new ReportCell(Ui.Group(byUnit.Sum(x => x.Gross)), byUnit.Sum(x => x.Gross)),
                new ReportCell(Ui.Group(byUnit.Sum(x => x.Deductions)), byUnit.Sum(x => x.Deductions)),
                new ReportCell(Ui.Group(byUnit.Sum(x => x.Net)), byUnit.Sum(x => x.Net)),
                new ReportCell(Ui.Group(byUnit.Sum(x => x.EmployerCost)), byUnit.Sum(x => x.EmployerCost)),
                new ReportCell(""),
            ],
        };
    }
}

/// <summary>§13 Payroll — "One component across everybody".</summary>
public sealed class ComponentRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "component-register",
        Name = "Component-wise register",
        Question = "Every earning and deduction component, across everybody",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var runIds = await Pay.Runs(ctx).Select(r => r.Id).ToListAsync(ct);
        if (runIds.Count == 0) return Pay.NoRuns(ctx);

        var byComponent = await ctx.Scope.Hr.PayrollComponentLines.AsNoTracking()
            .Where(c => runIds.Contains(c.RunId))
            .GroupBy(c => new { c.ComponentCode, c.ComponentName, c.Kind })
            .Select(g => new
            {
                g.Key.ComponentCode,
                g.Key.ComponentName,
                g.Key.Kind,
                Lines = g.Count(),
                Amount = g.Sum(x => x.AmountTaka),
            })
            .ToListAsync(ct);

        if (byComponent.Count == 0)
        {
            return ReportTable.Empty(
                $"The run for {PeriodWords.Range(ctx.Period.Range)} produced no component lines. "
                + "Check that employees have a pay structure with components on it.");
        }

        var rows = byComponent.OrderBy(x => x.Kind).ThenByDescending(x => x.Amount)
            .Select(x => new ReportRow(
            [
                // The code and name are snapshots: renaming a component next year must not rewrite
                // last year's register.
                new ReportCell(x.ComponentCode),
                new ReportCell(x.ComponentName),
                new ReportCell(x.Kind),
                new ReportCell(x.Lines.ToString("N0"), x.Lines),
                new ReportCell(Ui.Group(x.Amount), x.Amount),
            ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Component"), new("Kind"),
                new("Employees", ReportAlign.Right), new("Amount", ReportAlign.Right, Money: true),
            ],
            Rows = rows,
            MatchedRows = byComponent.Count,
        };
    }
}

/// <summary>§13 Payroll — "Gross + employer contributions — the true cost".</summary>
public sealed class EmployerCostReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "employer-cost",
        Name = "Employer cost statement",
        Question = "What the payroll actually costs the employer, run by run",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Year,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var runs = await Pay.Runs(ctx).OrderBy(r => r.Period).ThenBy(r => r.Sequence)
            .Take(ctx.MaxRows).ToListAsync(ct);
        if (runs.Count == 0) return Pay.NoRuns(ctx);

        var rows = runs.Select(r => new ReportRow(
        [
            new ReportCell(Ui.DateLong(r.Period)),
            new ReportCell(r.RunNo),
            new ReportCell(Pay.KindWord(r.Kind)),
            new ReportCell(Pay.StateWord(r.State), PillCss: Pay.StatePill(r.State)),
            new ReportCell(r.EmployeeCount.ToString("N0"), r.EmployeeCount,
                ctx.Drill("salary-sheet")),
            new ReportCell(Ui.Group(r.TotalGrossTaka), r.TotalGrossTaka),
            new ReportCell(Ui.Group(r.TotalDeductionTaka), r.TotalDeductionTaka),
            new ReportCell(Ui.Group(r.TotalNetTaka), r.TotalNetTaka),
            new ReportCell(Ui.Group(r.TotalEmployerCostTaka), r.TotalEmployerCostTaka),
        ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Period"), new("Run"), new("Kind"), new("State"),
                new("Employees", ReportAlign.Right),
                new("Gross", ReportAlign.Right, Money: true),
                new("Deductions", ReportAlign.Right, Money: true),
                new("Net paid", ReportAlign.Right, Money: true),
                new("Employer cost", ReportAlign.Right, Money: true),
            ],
            Rows = rows,
            MatchedRows = runs.Count,
            Totals =
            [
                new ReportCell("Total"), new ReportCell(""), new ReportCell(""), new ReportCell(""),
                new ReportCell(""),
                new ReportCell(Ui.Group(runs.Sum(r => r.TotalGrossTaka)), runs.Sum(r => r.TotalGrossTaka)),
                new ReportCell(Ui.Group(runs.Sum(r => r.TotalDeductionTaka)), runs.Sum(r => r.TotalDeductionTaka)),
                new ReportCell(Ui.Group(runs.Sum(r => r.TotalNetTaka)), runs.Sum(r => r.TotalNetTaka)),
                new ReportCell(Ui.Group(runs.Sum(r => r.TotalEmployerCostTaka)), runs.Sum(r => r.TotalEmployerCostTaka)),
            ],
        };
    }
}

/// <summary>§13 Payroll — "Every state change on every run, with actor and time".</summary>
public sealed class PayrollRunAuditReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "payroll-run-audit",
        Name = "Payroll run audit",
        Question = "Every state change on every run, with who did it and when",
        Category = ReportCategory.Payroll,
        SalaryBearing = true,
        DefaultGrain = PeriodGrain.Year,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var runs = await Pay.Runs(ctx).OrderBy(r => r.Period).Take(ctx.MaxRows).ToListAsync(ct);
        if (runs.Count == 0) return Pay.NoRuns(ctx);

        // The run row carries five who/when pairs, so its whole state history is already on it —
        // no separate event table is needed to answer "who locked March".
        var actorIds = runs.SelectMany(r => new[]
            { r.GeneratedBy, r.ReviewedBy ?? 0, r.ApprovedBy ?? 0, r.LockedBy ?? 0, r.PostedBy ?? 0 })
            .Where(id => id != 0).Distinct().ToList();

        // A separate query, not a join: Auth and Hr are different DbContexts and EF cannot join
        // across two instances (CrossContextQueryTests enforces this).
        var actors = await ctx.Scope.Auth.Users
            .Where(u => actorIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        var rows = runs.Select(r => new ReportRow(
        [
            new ReportCell(Ui.DateLong(r.Period)),
            new ReportCell(r.RunNo),
            new ReportCell(Pay.StateWord(r.State), PillCss: Pay.StatePill(r.State)),
            Stamp(r.GeneratedAt, r.GeneratedBy),
            Stamp(r.ReviewedAt, r.ReviewedBy),
            Stamp(r.ApprovedAt, r.ApprovedBy),
            Stamp(r.LockedAt, r.LockedBy),
            Stamp(r.PostedAt, r.PostedBy),
        ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Period"), new("Run"), new("State"),
                new("Generated", Sortable: false), new("Reviewed", Sortable: false),
                new("Approved", Sortable: false), new("Locked", Sortable: false),
                new("Posted", Sortable: false),
            ],
            Rows = rows,
            MatchedRows = runs.Count,
        };

        ReportCell Stamp(DateTimeOffset? at, long? by)
        {
            if (at is null) return new ReportCell("—");
            var who = by is { } id && actors.TryGetValue(id, out var name) ? name : $"user {by}";
            return new ReportCell($"{Ui.DateTimeShort(at.Value)} · {who}");
        }
    }
}
