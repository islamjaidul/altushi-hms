using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Reports;

internal static class LeaveText
{
    /// <summary>Days are stored in basis points (10000 = one day) — integers only, never a decimal.</summary>
    internal static string Days(int bp) => bp % 10000 == 0
        ? (bp / 10000).ToString("N0")
        : (bp / 10000m).ToString("0.##");

    internal static string StateWord(string state) => state switch
    {
        LeaveApplicationState.Applied => "Applied",
        LeaveApplicationState.Recommended => "Recommended",
        LeaveApplicationState.Approved => "Approved",
        LeaveApplicationState.Rejected => "Rejected",
        LeaveApplicationState.Availed => "Availed",
        LeaveApplicationState.Cancelled => "Cancelled",
        _ => state,
    };

    internal static string StatePill(string state) => state switch
    {
        LeaveApplicationState.Approved or LeaveApplicationState.Availed => "good",
        LeaveApplicationState.Rejected or LeaveApplicationState.Cancelled => "bad",
        _ => "warn",
    };
}

/// <summary>§13 Leave — "Every application in the period with its outcome".</summary>
public sealed class LeaveRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "leave-register",
        Name = "Leave register",
        Question = "Every application in the period, and what was decided",
        Category = ReportCategory.Leave,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var (from, to) = (ctx.Period.Range.From, ctx.Period.Range.To);
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        // Overlap, not containment: a leave running 28 Jul – 3 Aug belongs on both months' registers,
        // because it is absence in both. Containment would lose it from each.
        var query = ctx.Scope.Hr.LeaveApplications.AsNoTracking()
            .Where(a => a.BranchId == ctx.BranchId && a.FromDate <= to && a.ToDate >= from);

        if (ctx.Filters.EmployeeId is { } id) query = query.Where(a => a.EmployeeId == id);

        var applications = await query
            .OrderBy(a => a.FromDate)
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        if (applications.Count == 0)
        {
            return ReportTable.Empty(
                $"No leave overlapped {PeriodWords.Range(ctx.Period.Range)}. "
                + "An application appears here if any of its days fall in the period.");
        }

        var matched = applications.Count;
        if (matched > ctx.MaxRows) applications = [.. applications.Take(ctx.MaxRows)];

        var ids = applications.Select(a => a.EmployeeId).Distinct().ToList();
        var names = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeCode, e.FullName }, ct);

        var rows = applications.Select(a =>
        {
            names.TryGetValue(a.EmployeeId, out var who);
            return new ReportRow(
            [
                new ReportCell(a.ApplicationNo),
                new ReportCell(who?.EmployeeCode ?? "—", Href: ctx.Employee(a.EmployeeId)),
                new ReportCell(who?.FullName ?? "—", Href: ctx.Employee(a.EmployeeId)),
                new ReportCell(lookups.LeaveType(a.LeaveTypeId)),
                new ReportCell(Ui.DateLong(a.FromDate)),
                new ReportCell(Ui.DateLong(a.ToDate)),
                new ReportCell(LeaveText.Days(a.DaysBp), a.DaysBp / 10000),
                new ReportCell(LeaveText.StateWord(a.State), PillCss: LeaveText.StatePill(a.State)),
                new ReportCell(a.DecisionNote ?? a.Reason),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Application"), new("Code"), new("Name"), new("Type"), new("From"), new("To"),
                new("Days", ReportAlign.Right), new("State"), new("Reason / note", Sortable: false),
            ],
            Rows = rows,
            MatchedRows = matched,
            Totals =
            [
                new ReportCell("Total"), new ReportCell(""), new ReportCell(""), new ReportCell(""),
                new ReportCell(""), new ReportCell(""),
                new ReportCell(LeaveText.Days(applications.Sum(a => a.DaysBp))),
                new ReportCell(""), new ReportCell(""),
            ],
        };
    }
}

/// <summary>
/// §13 Leave — "Per employee per type — opening, accrued, availed, encashed, adjusted, available".
/// §7.6: balances are explained rather than asserted.
/// </summary>
public sealed class LeaveBalanceReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "leave-balance",
        Name = "Leave balance statement",
        Question = "Every balance, explained rather than asserted",
        Category = ReportCategory.Leave,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.LeaveYear,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        // Balances are held per leave *year*, not per arbitrary range, so the period selects a year.
        var year = ctx.Period.Range.From.Year;
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        var query = ctx.Scope.Hr.LeaveBalances.AsNoTracking()
            .Where(b => b.BranchId == ctx.BranchId && b.LeaveYear == year);
        if (ctx.Filters.EmployeeId is { } id) query = query.Where(b => b.EmployeeId == id);

        var balances = await query.Take(ctx.MaxRows + 1).ToListAsync(ct);

        if (balances.Count == 0)
        {
            return ReportTable.Empty(
                $"No leave balance exists for the year beginning {PeriodWords.Day(ctx.Period.Range.From)}. "
                + "Balances are opened by the leave-year process; until then, applications draw on nothing.");
        }

        var matched = balances.Count;
        if (matched > ctx.MaxRows) balances = [.. balances.Take(ctx.MaxRows)];

        var ids = balances.Select(b => b.EmployeeId).Distinct().ToList();
        var names = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeCode, e.FullName }, ct);

        var rows = balances
            .OrderBy(b => names.TryGetValue(b.EmployeeId, out var n) ? n.EmployeeCode : "")
            .ThenBy(b => b.LeaveTypeId)
            .Select(b =>
            {
                names.TryGetValue(b.EmployeeId, out var who);
                // AvailableBp is EF-Ignored (it is arithmetic, not a column), so it is computed here.
                var available = b.AvailableBp;
                return new ReportRow(
                [
                    new ReportCell(who?.EmployeeCode ?? "—", Href: ctx.Employee(b.EmployeeId)),
                    new ReportCell(who?.FullName ?? "—", Href: ctx.Employee(b.EmployeeId)),
                    new ReportCell(lookups.LeaveType(b.LeaveTypeId)),
                    new ReportCell(LeaveText.Days(b.OpeningBp), b.OpeningBp),
                    new ReportCell(LeaveText.Days(b.AccruedBp), b.AccruedBp),
                    new ReportCell(LeaveText.Days(b.AvailedBp), b.AvailedBp),
                    new ReportCell(LeaveText.Days(b.EncashedBp), b.EncashedBp),
                    new ReportCell(LeaveText.Days(b.AdjustmentBp), b.AdjustmentBp),
                    new ReportCell(LeaveText.Days(available), available,
                        PillCss: available < 0 ? "bad" : null),
                ]);
            }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Type"),
                new("Opening", ReportAlign.Right), new("Accrued", ReportAlign.Right),
                new("Availed", ReportAlign.Right), new("Encashed", ReportAlign.Right),
                new("Adjusted", ReportAlign.Right), new("Available", ReportAlign.Right),
            ],
            Rows = rows,
            MatchedRows = matched,
        };
    }
}

/// <summary>§13 Leave — "By type, unit, month — where absence concentrates".</summary>
public sealed class LeaveAvailedAnalysisReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "leave-availed-analysis",
        Name = "Leave availed analysis",
        Question = "Where absence concentrates, by leave type",
        Category = ReportCategory.Leave,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Year,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var (from, to) = (ctx.Period.Range.From, ctx.Period.Range.To);
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        var byType = await ctx.Scope.Hr.LeaveApplications.AsNoTracking()
            .Where(a => a.BranchId == ctx.BranchId && a.FromDate <= to && a.ToDate >= from
                        && (a.State == LeaveApplicationState.Approved
                            || a.State == LeaveApplicationState.Availed))
            .GroupBy(a => a.LeaveTypeId)
            .Select(g => new
            {
                LeaveTypeId = g.Key,
                Applications = g.Count(),
                DaysBp = g.Sum(x => x.DaysBp),
                People = g.Select(x => x.EmployeeId).Distinct().Count(),
            })
            .ToListAsync(ct);

        if (byType.Count == 0)
        {
            return ReportTable.Empty(
                $"No approved leave overlapped {PeriodWords.Range(ctx.Period.Range)}. "
                + "Applications still awaiting a decision are on the leave register, not here.");
        }

        var totalDays = byType.Sum(x => x.DaysBp);
        var rows = byType.OrderByDescending(x => x.DaysBp).Select(x => new ReportRow(
        [
            new ReportCell(lookups.LeaveType(x.LeaveTypeId)),
            new ReportCell(x.Applications.ToString("N0"), x.Applications,
                ctx.Drill("leave-register")),
            new ReportCell(x.People.ToString("N0"), x.People),
            new ReportCell(LeaveText.Days(x.DaysBp), x.DaysBp),
            new ReportCell(totalDays == 0 ? "—" : $"{x.DaysBp * 100L / totalDays}%"),
        ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Leave type"), new("Applications", ReportAlign.Right),
                new("Employees", ReportAlign.Right), new("Days", ReportAlign.Right),
                new("Share", ReportAlign.Right),
            ],
            Rows = rows,
            MatchedRows = byType.Count,
            Totals =
            [
                new ReportCell("Total"),
                new ReportCell(byType.Sum(x => x.Applications).ToString("N0")),
                new ReportCell(""),
                new ReportCell(LeaveText.Days(totalDays)),
                new ReportCell("100%"),
            ],
        };
    }
}

/// <summary>§13 Leave — "What is waiting, and how long" (US16.5's ageing).</summary>
public sealed class PendingLeaveAgeingReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "pending-leave-ageing",
        Name = "Pending approvals ageing",
        Question = "What is waiting for a decision, and how long it has waited",
        Category = ReportCategory.Leave,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);
        var today = ctx.Period.Range.To;

        // Deliberately not period-bounded: an application pending since March is exactly what this
        // report exists to surface, and hiding it behind a period would defeat the purpose.
        var pending = await ctx.Scope.Hr.LeaveApplications.AsNoTracking()
            .Where(a => a.BranchId == ctx.BranchId
                        && (a.State == LeaveApplicationState.Applied
                            || a.State == LeaveApplicationState.Recommended))
            .OrderBy(a => a.AppliedAt)
            .Take(ctx.MaxRows)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            return ReportTable.Empty(
                "Nothing is waiting for a leave decision. This report ignores the period on purpose — "
                + "an application pending since last quarter is exactly what it exists to show.");
        }

        var ids = pending.Select(a => a.EmployeeId).Distinct().ToList();
        var names = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeCode, e.FullName }, ct);

        var rows = pending.Select(a =>
        {
            names.TryGetValue(a.EmployeeId, out var who);
            var waiting = today.DayNumber - Dhaka.DateOf(a.AppliedAt).DayNumber;
            return new ReportRow(
            [
                new ReportCell(a.ApplicationNo),
                new ReportCell(who?.FullName ?? "—", Href: ctx.Employee(a.EmployeeId)),
                new ReportCell(lookups.LeaveType(a.LeaveTypeId)),
                new ReportCell(Ui.DateLong(a.FromDate)),
                new ReportCell(LeaveText.Days(a.DaysBp), a.DaysBp),
                new ReportCell(LeaveText.StateWord(a.State), PillCss: LeaveText.StatePill(a.State)),
                new ReportCell($"{waiting:N0} day(s)", waiting,
                    PillCss: waiting >= 7 ? "bad" : waiting >= 3 ? "warn" : null),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Application"), new("Name"), new("Type"), new("Starts"),
                new("Days", ReportAlign.Right), new("State"), new("Waiting", ReportAlign.Right),
            ],
            Rows = rows,
            MatchedRows = pending.Count,
        };
    }
}

/// <summary>§13 Leave — "Unpaid days and their payroll effect".</summary>
public sealed class LeaveWithoutPayReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "leave-without-pay",
        Name = "Leave without pay register",
        Question = "Unpaid leave days, and the run that deducted them",
        Category = ReportCategory.Leave,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var (from, to) = (ctx.Period.Range.From, ctx.Period.Range.To);

        // The payroll line is the authority on what was actually treated as unpaid — spec 0052 made
        // LeaveWithoutPayDaysBp a subset of LeaveDaysBp precisely so a locked run keeps its meaning.
        var runs = await ctx.Scope.Hr.PayrollRuns.AsNoTracking()
            .Where(r => r.BranchId == ctx.BranchId && r.Period >= from && r.Period <= to
                        && r.State != PayrollRunState.Cancelled)
            .Select(r => new { r.Id, r.Period, r.RunNo })
            .ToListAsync(ct);

        if (runs.Count == 0)
        {
            return ReportTable.Empty(
                $"No payroll run covers {PeriodWords.Range(ctx.Period.Range)}, so no leave has yet "
                + "been treated as unpaid. Unpaid days are decided when the run is generated.");
        }

        var runIds = runs.Select(r => r.Id).ToList();
        var lines = await ctx.Scope.Hr.PayrollLines.AsNoTracking()
            .Where(l => runIds.Contains(l.RunId) && l.LeaveWithoutPayDaysBp > 0)
            .Select(l => new { l.RunId, l.EmployeeId, l.EmployeeCode, l.EmployeeName, l.LeaveWithoutPayDaysBp })
            .Take(ctx.MaxRows)
            .ToListAsync(ct);

        if (lines.Count == 0)
        {
            return ReportTable.Empty(
                $"Nobody took unpaid leave in {PeriodWords.Range(ctx.Period.Range)}. "
                + "A leave type counts as unpaid only when its policy says so.");
        }

        var runOf = runs.ToDictionary(r => r.Id);
        var rows = lines.OrderBy(l => l.EmployeeCode).Select(l => new ReportRow(
        [
            new ReportCell(runOf.TryGetValue(l.RunId, out var r) ? Ui.DateLong(r.Period) : "—"),
            new ReportCell(l.EmployeeCode, Href: ctx.Employee(l.EmployeeId)),
            new ReportCell(l.EmployeeName, Href: ctx.Employee(l.EmployeeId)),
            new ReportCell(LeaveText.Days(l.LeaveWithoutPayDaysBp), l.LeaveWithoutPayDaysBp),
            new ReportCell(runOf.TryGetValue(l.RunId, out var r2) ? r2.RunNo : "—"),
        ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Period"), new("Code"), new("Name"),
                new("Unpaid days", ReportAlign.Right), new("Run"),
            ],
            Rows = rows,
            MatchedRows = lines.Count,
            Totals =
            [
                new ReportCell("Total"), new ReportCell(""), new ReportCell(""),
                new ReportCell(LeaveText.Days(lines.Sum(l => l.LeaveWithoutPayDaysBp))),
                new ReportCell(""),
            ],
        };
    }
}
