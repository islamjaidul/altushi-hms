using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Reports;

/// <summary>
/// §13 People. None of these shows pay, so all are <c>hr.reports.view</c> — a department head can
/// read the directory and the headcount without ever seeing a salary (D6).
/// </summary>
internal static class People
{
    /// <summary>The employees a filter selects, as of the end of the period.</summary>
    internal static IQueryable<Employee> Scope(ReportContext ctx)
    {
        var q = ctx.Scope.Hr.Employees.AsNoTracking().Where(e => e.BranchId == ctx.BranchId);

        if (ctx.Filters.EmployeeId is { } id) q = q.Where(e => e.Id == id);

        q = ctx.Filters.Status switch
        {
            "active" => q.Where(e => e.SeparatedOn == null),
            "separated" => q.Where(e => e.SeparatedOn != null),
            { Length: > 0 } s => q.Where(e => e.Status == s),
            _ => q,
        };

        return q;
    }

    /// <summary>
    /// The assignment in force on a date, per employee. Effective-dating is what makes "the org
    /// chart last March" reproducible — the same rule the rate plan lives by.
    /// </summary>
    internal static IQueryable<EmployeeAssignment> AssignmentsOn(ReportContext ctx, DateOnly on)
    {
        var q = ctx.Scope.Hr.Assignments.AsNoTracking()
            .Where(a => a.BranchId == ctx.BranchId
                        && a.EffectiveFrom <= on
                        && (a.EffectiveTo == null || a.EffectiveTo >= on));

        if (ctx.Filters.OrgUnitIds.Count > 0) q = q.Where(a => ctx.Filters.OrgUnitIds.Contains(a.OrgUnitId));
        if (ctx.Filters.DesignationId is { } d) q = q.Where(a => a.DesignationId == d);
        if (ctx.Filters.GradeId is { } g) q = q.Where(a => a.GradeId == g);

        return q;
    }

    internal static string StatusWord(Employee e) => e.SeparatedOn is not null
        ? e.Status switch
        {
            EmploymentStatus.Resigned => "Resigned",
            EmploymentStatus.Terminated => "Terminated",
            EmploymentStatus.Retired => "Retired",
            _ => "Separated",
        }
        : e.Status switch
        {
            EmploymentStatus.Probation => "On probation",
            EmploymentStatus.Confirmed => "Confirmed",
            EmploymentStatus.OnNotice => "On notice",
            _ => e.Status,
        };

    internal static string StatusPill(Employee e) => e.SeparatedOn is not null
        ? "bad"
        : e.Status == EmploymentStatus.Confirmed ? "good" : "warn";
}

/// <summary>§13 People — "Who works here, filtered any way".</summary>
public sealed class EmployeeDirectoryReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "employee-directory",
        Name = "Employee directory",
        Question = "Who works here, filtered any way",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var on = ctx.Period.Range.To;
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        // Two queries joined in memory: EF has no navigation properties here by design, and the
        // assignment side is one row per employee once the as-of-date predicate has applied.
        var assignments = await People.AssignmentsOn(ctx, on)
            .Select(a => new { a.EmployeeId, a.OrgUnitId, a.DesignationId, a.GradeId })
            .ToListAsync(ct);
        var byEmployee = assignments.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        var employees = await People.Scope(ctx)
            .OrderBy(e => e.EmployeeCode)
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        // A unit/designation/grade filter is expressed on the assignment side, so it must narrow the
        // employee list too — otherwise the filter chip would claim a narrowing the rows do not show.
        if (ctx.Filters.OrgUnitIds.Count > 0 || ctx.Filters.DesignationId is not null
            || ctx.Filters.GradeId is not null)
        {
            employees = [.. employees.Where(e => byEmployee.ContainsKey(e.Id))];
        }

        var matched = employees.Count;
        if (matched > ctx.MaxRows) employees = [.. employees.Take(ctx.MaxRows)];

        if (employees.Count == 0)
        {
            return ReportTable.Empty(
                $"No employee matches this selection as at {PeriodWords.Day(on)}. "
                + "Remove a filter, or choose a later period — an employee who joined after this "
                + "date will not appear.");
        }

        var rows = employees.Select(e =>
        {
            byEmployee.TryGetValue(e.Id, out var a);
            return new ReportRow(
            [
                new ReportCell(e.EmployeeCode, Href: ctx.Employee(e.Id)),
                new ReportCell(e.FullName, Href: ctx.Employee(e.Id)),
                new ReportCell(lookups.Unit(a?.OrgUnitId)),
                new ReportCell(lookups.Designation(a?.DesignationId)),
                new ReportCell(lookups.Grade(a?.GradeId)),
                new ReportCell(Ui.DateLong(e.JoinedOn)),
                new ReportCell(People.StatusWord(e), PillCss: People.StatusPill(e)),
                new ReportCell(e.Phone ?? "—"),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Unit"), new("Designation"), new("Grade"),
                new("Joined"), new("Status"), new("Phone", Sortable: false),
            ],
            Rows = rows,
            MatchedRows = matched,
        };
    }
}

/// <summary>§13 People — "The shape of the workforce".</summary>
public sealed class HeadcountSummaryReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "headcount-summary",
        Name = "Headcount summary",
        Question = "The shape of the workforce, by unit, designation and grade",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var on = ctx.Period.Range.To;
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        var inService = await People.Scope(ctx)
            .Where(e => e.JoinedOn <= on && (e.SeparatedOn == null || e.SeparatedOn > on))
            .Select(e => e.Id)
            .ToListAsync(ct);

        var assignments = await People.AssignmentsOn(ctx, on)
            .Select(a => new { a.EmployeeId, a.OrgUnitId })
            .ToListAsync(ct);

        var counted = assignments
            .Where(a => inService.Contains(a.EmployeeId))
            .GroupBy(a => a.OrgUnitId)
            .Select(g => new { OrgUnitId = g.Key, Count = g.Select(x => x.EmployeeId).Distinct().Count() })
            .OrderByDescending(x => x.Count)
            .ToList();

        if (counted.Count == 0)
        {
            return ReportTable.Empty(
                $"Nobody was in service on {PeriodWords.Day(on)}. Employees appear here from their "
                + "joining date; check that assignments have been recorded on Employees.");
        }

        var total = counted.Sum(x => x.Count);
        var rows = counted.Select(x => new ReportRow(
        [
            new ReportCell(lookups.Unit(x.OrgUnitId)),
            new ReportCell(x.Count.ToString("N0"), x.Count,
                ctx.Drill("employee-directory", ("unit", x.OrgUnitId.ToString()))),
            new ReportCell(total == 0 ? "—" : $"{x.Count * 100 / total}%"),
        ])).ToList();

        return new ReportTable
        {
            Columns = [new("Unit"), new("Headcount", ReportAlign.Right), new("Share", ReportAlign.Right)],
            Rows = rows,
            MatchedRows = counted.Count,
            Totals = [new ReportCell("Total"), new ReportCell(total.ToString("N0"), total), new ReportCell("100%")],
        };
    }
}

/// <summary>§13 People — "Opening, joiners, leavers, closing — for any period".</summary>
public sealed class HeadcountMovementReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "headcount-movement",
        Name = "Headcount movement",
        Question = "Opening, joiners, leavers and closing for the period",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var (from, to) = (ctx.Period.Range.From, ctx.Period.Range.To);

        var all = await People.Scope(ctx)
            .Select(e => new { e.Id, e.JoinedOn, e.SeparatedOn })
            .ToListAsync(ct);

        var opening = all.Count(e => e.JoinedOn < from && (e.SeparatedOn == null || e.SeparatedOn >= from));
        var joiners = all.Count(e => e.JoinedOn >= from && e.JoinedOn <= to);
        var leavers = all.Count(e => e.SeparatedOn is { } s && s >= from && s <= to);
        var closing = all.Count(e => e.JoinedOn <= to && (e.SeparatedOn == null || e.SeparatedOn > to));

        if (all.Count == 0)
        {
            return ReportTable.Empty(
                "No employee records exist for this branch yet. Add employees on the Employees "
                + "screen and this report will show how the headcount moved.");
        }

        // Attrition against the average headcount, the convention finance uses; stated in the row so
        // nobody has to guess which denominator was taken.
        var average = (opening + closing) / 2;
        var attrition = average == 0 ? "—" : $"{leavers * 100 / Math.Max(average, 1)}%";

        var rows = new List<ReportRow>
        {
            Row("Opening headcount", opening, from.AddDays(-1), null),
            Row("Joiners", joiners, null, ctx.Drill("new-joiners")),
            Row("Leavers", leavers, null, ctx.Drill("separations")),
            Row("Closing headcount", closing, to, null),
            new([
                new ReportCell("Attrition (leavers ÷ average headcount)"),
                new ReportCell(attrition),
                new ReportCell(average == 0 ? "—" : $"average {average:N0}"),
            ]),
        };

        return new ReportTable
        {
            Columns = [new("Movement"), new("Count", ReportAlign.Right), new("As at", Sortable: false)],
            Rows = rows,
            MatchedRows = rows.Count,
        };

        static ReportRow Row(string label, int count, DateOnly? asAt, string? href) => new([
            new ReportCell(label),
            new ReportCell(count.ToString("N0"), count, href),
            new ReportCell(asAt is { } d ? Ui.DateLong(d) : "in the period"),
        ]);
    }
}

/// <summary>§13 People — "Who started in this period" (§5A-17).</summary>
public sealed class NewJoinersReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "new-joiners",
        Name = "New joiners",
        Question = "Who started in this period",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var (from, to) = (ctx.Period.Range.From, ctx.Period.Range.To);
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        var joiners = await People.Scope(ctx)
            .Where(e => e.JoinedOn >= from && e.JoinedOn <= to)
            .OrderBy(e => e.JoinedOn).ThenBy(e => e.EmployeeCode)
            .Take(ctx.MaxRows)
            .ToListAsync(ct);

        if (joiners.Count == 0)
        {
            return ReportTable.Empty(
                $"Nobody joined between {PeriodWords.Range(ctx.Period.Range)}. "
                + "Step back a period with the arrow, or widen it to a quarter or a year.");
        }

        var assignments = await People.AssignmentsOn(ctx, to)
            .Select(a => new { a.EmployeeId, a.OrgUnitId, a.DesignationId })
            .ToListAsync(ct);
        var byEmployee = assignments.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        var rows = joiners.Select(e =>
        {
            byEmployee.TryGetValue(e.Id, out var a);
            return new ReportRow(
            [
                new ReportCell(Ui.DateLong(e.JoinedOn)),
                new ReportCell(e.EmployeeCode, Href: ctx.Employee(e.Id)),
                new ReportCell(e.FullName, Href: ctx.Employee(e.Id)),
                new ReportCell(lookups.Unit(a?.OrgUnitId)),
                new ReportCell(lookups.Designation(a?.DesignationId)),
                new ReportCell(People.StatusWord(e), PillCss: People.StatusPill(e)),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Joined"), new("Code"), new("Name"), new("Unit"), new("Designation"), new("Status"),
            ],
            Rows = rows,
            MatchedRows = joiners.Count,
        };
    }
}

/// <summary>§13 People — "Who left, when, why" (§5A-17).</summary>
public sealed class SeparationsReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "separations",
        Name = "Separations",
        Question = "Who left, when and why",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var (from, to) = (ctx.Period.Range.From, ctx.Period.Range.To);
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        var leavers = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId
                        && e.SeparatedOn != null && e.SeparatedOn >= from && e.SeparatedOn <= to)
            .OrderBy(e => e.SeparatedOn).ThenBy(e => e.EmployeeCode)
            .Take(ctx.MaxRows)
            .ToListAsync(ct);

        if (leavers.Count == 0)
        {
            return ReportTable.Empty(
                $"Nobody left between {PeriodWords.Range(ctx.Period.Range)}. "
                + "Widen the period, or check that separations have been recorded on the employee's record.");
        }

        var ids = leavers.Select(e => e.Id).ToList();
        // The employment event carries the reason and who recorded it; the employee row carries only
        // the date and the resulting status.
        var events = await ctx.Scope.Hr.EmploymentEvents.AsNoTracking()
            .Where(v => v.BranchId == ctx.BranchId && ids.Contains(v.EmployeeId)
                        && (v.Kind == EmploymentEventKind.Resigned
                            || v.Kind == EmploymentEventKind.Terminated
                            || v.Kind == EmploymentEventKind.Retired))
            .ToListAsync(ct);
        var byEmployee = events.GroupBy(v => v.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.OnDate).First());

        var assignments = await People.AssignmentsOn(ctx, from)
            .Select(a => new { a.EmployeeId, a.OrgUnitId })
            .ToListAsync(ct);
        var unitOf = assignments.GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First().OrgUnitId);

        var rows = leavers.Select(e =>
        {
            byEmployee.TryGetValue(e.Id, out var ev);
            var service = e.SeparatedOn!.Value.DayNumber - e.JoinedOn.DayNumber;
            return new ReportRow(
            [
                new ReportCell(Ui.DateLong(e.SeparatedOn!.Value)),
                new ReportCell(e.EmployeeCode, Href: ctx.Employee(e.Id)),
                new ReportCell(e.FullName, Href: ctx.Employee(e.Id)),
                new ReportCell(unitOf.TryGetValue(e.Id, out var u) ? lookups.Unit(u) : "—"),
                new ReportCell(People.StatusWord(e), PillCss: "bad"),
                new ReportCell(ev?.Note ?? "—"),
                new ReportCell($"{service / 365} y {(service % 365) / 30} m", service),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Left on"), new("Code"), new("Name"), new("Unit"), new("Kind"),
                new("Reason", Sortable: false), new("Service", ReportAlign.Right),
            ],
            Rows = rows,
            MatchedRows = leavers.Count,
        };
    }
}

/// <summary>§13 People — "Who has served how long".</summary>
public sealed class ServiceLengthReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "service-length",
        Name = "Service length",
        Question = "Who has served how long",
        Category = ReportCategory.People,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var on = ctx.Period.Range.To;
        var lookups = await HrLookups.LoadAsync(ctx.Scope, ct);

        var employees = await People.Scope(ctx)
            .Where(e => e.JoinedOn <= on && (e.SeparatedOn == null || e.SeparatedOn > on))
            .ToListAsync(ct);

        if (employees.Count == 0)
        {
            return ReportTable.Empty(
                $"Nobody was in service on {PeriodWords.Day(on)}.");
        }

        var assignments = await People.AssignmentsOn(ctx, on)
            .Select(a => new { a.EmployeeId, a.OrgUnitId })
            .ToListAsync(ct);
        var unitOf = assignments.GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First().OrgUnitId);

        var rows = employees
            .Select(e => new { Employee = e, Days = on.DayNumber - e.JoinedOn.DayNumber })
            .OrderByDescending(x => x.Days)
            .Take(ctx.MaxRows)
            .Select(x => new ReportRow(
            [
                new ReportCell(x.Employee.EmployeeCode, Href: ctx.Employee(x.Employee.Id)),
                new ReportCell(x.Employee.FullName, Href: ctx.Employee(x.Employee.Id)),
                new ReportCell(unitOf.TryGetValue(x.Employee.Id, out var u) ? lookups.Unit(u) : "—"),
                new ReportCell(Ui.DateLong(x.Employee.JoinedOn)),
                new ReportCell($"{x.Days / 365} y {(x.Days % 365) / 30} m", x.Days),
            ]))
            .ToList();

        return new ReportTable
        {
            Columns = [new("Code"), new("Name"), new("Unit"), new("Joined"), new("Service", ReportAlign.Right)],
            Rows = rows,
            MatchedRows = employees.Count,
        };
    }
}
