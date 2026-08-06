using Hms.Hr;
using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Reports;

/// <summary>
/// The registers spec 0058 adds (§13 Time &amp; attendance). None of them shows pay: overtime is
/// minutes here, and what those minutes are worth is a payroll question answered on a salary-bearing
/// register (D6).
/// </summary>
public sealed class DeviceHealthReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "device-health",
        Name = "Attendance devices",
        Question = "Which clocks are reporting, and which have gone quiet",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Day,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var now = Dhaka.MidnightUtc(ctx.Period.Range.To).AddDays(1);

        var devices = await ctx.Scope.Hr.Devices.AsNoTracking()
            .Where(d => d.BranchId == ctx.BranchId)
            .OrderBy(d => d.Code)
            .ToListAsync(ct);

        if (devices.Count == 0)
        {
            return ReportTable.Empty(
                "No device is registered. Attendance still arrives by file import and manual entry — "
                + "a device is registered on Setup → Devices, and reports here once it has been "
                + "heard from.");
        }

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Device"), new("Location"), new("Serial"),
                new("Expected every", ReportAlign.Right), new("Last heard"), new("Health"),
            ],
            Rows =
            [
                .. devices.Select(d =>
                {
                    var health = AttendanceDevice.HealthOf(d.LastSeenAt, d.ExpectedIntervalMinutes, now);
                    return new ReportRow(
                    [
                        new ReportCell(d.Code),
                        new ReportCell(d.Name),
                        new ReportCell(d.Location ?? "—"),
                        new ReportCell(d.SerialNo ?? "—"),
                        new ReportCell($"{d.ExpectedIntervalMinutes} min",
                            Number: d.ExpectedIntervalMinutes),
                        new ReportCell(d.LastSeenAt is { } seen
                            ? Ui.DateLong(DateOnly.FromDateTime(Ui.Local(seen).DateTime))
                                + " " + Ui.Time(seen)
                            : "never"),
                        new ReportCell(DeviceHealth.Label(health),
                            PillCss: health switch
                            {
                                DeviceHealth.Healthy => "good",
                                DeviceHealth.Late => "warn",
                                _ => "bad",
                            }),
                    ]);
                }),
            ],
            MatchedRows = devices.Count,
        };
    }
}

/// <summary>§13 — the requests employees raised about their own time (G23, G24, G25, G29).</summary>
public sealed class TimeRequestReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "time-requests",
        Name = "Time requests",
        Question = "What employees asked for about their own attendance, and what was decided",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
        Landscape = true,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;
        var employeeFilter = ctx.Filters.EmployeeId;

        var rows = new List<(string Kind, DateOnly On, long EmployeeId, string Code, string Name,
            string What, string? Reason, string State, string? DecidedBy)>();

        rows.AddRange((await (
            from r in ctx.Scope.Hr.RegularizationRequests.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on r.EmployeeId equals e.Id
            where r.BranchId == ctx.BranchId && r.OnDate >= range.From && r.OnDate <= range.To
                  && (employeeFilter == null || r.EmployeeId == employeeFilter)
            select new { r.OnDate, e.Id, e.EmployeeCode, e.FullName, r.RequestedStatus,
                r.Reason, r.State, r.DecidedByName })
            .ToListAsync(ct))
            .Select(r => ("Regularization", r.OnDate, r.Id, r.EmployeeCode, r.FullName,
                "mark as " + r.RequestedStatus, (string?)r.Reason, r.State, (string?)r.DecidedByName)));

        rows.AddRange((await (
            from r in ctx.Scope.Hr.OvertimeRequests.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on r.EmployeeId equals e.Id
            where r.BranchId == ctx.BranchId && r.OnDate >= range.From && r.OnDate <= range.To
                  && (employeeFilter == null || r.EmployeeId == employeeFilter)
            select new { r.OnDate, e.Id, e.EmployeeCode, e.FullName, r.Minutes, r.Banked,
                r.Reason, r.State, r.DecidedByName })
            .ToListAsync(ct))
            .Select(r => ("Overtime", r.OnDate, r.Id, r.EmployeeCode, r.FullName,
                $"{r.Minutes} min{(r.Banked ? ", banked" : "")}", (string?)r.Reason, r.State,
                (string?)r.DecidedByName)));

        rows.AddRange((await (
            from r in ctx.Scope.Hr.CompOffRequests.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on r.EmployeeId equals e.Id
            where r.BranchId == ctx.BranchId && r.OnDate >= range.From && r.OnDate <= range.To
                  && (employeeFilter == null || r.EmployeeId == employeeFilter)
            select new { r.OnDate, e.Id, e.EmployeeCode, e.FullName, r.Minutes,
                r.Reason, r.State, r.DecidedByName })
            .ToListAsync(ct))
            .Select(r => ("Comp-off", r.OnDate, r.Id, r.EmployeeCode, r.FullName,
                $"{r.Minutes} min off", (string?)r.Reason, r.State, (string?)r.DecidedByName)));

        rows.AddRange((await (
            from r in ctx.Scope.Hr.ShortLeaveRequests.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on r.EmployeeId equals e.Id
            where r.BranchId == ctx.BranchId && r.OnDate >= range.From && r.OnDate <= range.To
                  && (employeeFilter == null || r.EmployeeId == employeeFilter)
            select new { r.OnDate, e.Id, e.EmployeeCode, e.FullName, r.Kind, r.FromTime, r.ToTime,
                r.Reason, r.State, r.DecidedByName })
            .ToListAsync(ct))
            .Select(r => (ShortLeaveKind.Label(r.Kind), r.OnDate, r.Id, r.EmployeeCode, r.FullName,
                $"{r.FromTime:HH\\:mm}–{r.ToTime:HH\\:mm}", (string?)r.Reason, r.State,
                (string?)r.DecidedByName)));

        if (rows.Count == 0)
        {
            return ReportTable.Empty(
                $"No time request falls in {PeriodWords.Describe(ctx.Period)}. Requests are raised "
                + "and decided on the time desk.");
        }

        var ordered = rows.OrderByDescending(r => r.On).ThenBy(r => r.Name).ToList();
        var matched = ordered.Count;
        if (matched > ctx.MaxRows) ordered = [.. ordered.Take(ctx.MaxRows)];

        return new ReportTable
        {
            Columns =
            [
                new("Kind"), new("Date"), new("Code"), new("Employee"),
                new("Asked for"), new("Reason"), new("State"), new("Decided by"),
            ],
            Rows =
            [
                .. ordered.Select(r => new ReportRow(
                [
                    new ReportCell(r.Kind),
                    new ReportCell(Ui.DateLong(r.On)),
                    new ReportCell(r.Code, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.Name, Href: ctx.Employee(r.EmployeeId)),
                    new ReportCell(r.What),
                    new ReportCell(r.Reason ?? "—"),
                    new ReportCell(RequestState.Label(r.State), PillCss: RequestState.Tone(r.State)),
                    new ReportCell(r.DecidedBy ?? "—"),
                ])),
            ],
            MatchedRows = matched,
        };
    }
}

/// <summary>
/// §13 — the overtime bank (G25). Minutes, not money: what a banked minute is worth depends on
/// whether it is ever paid, and it is not (D6 keeps this off the salary permission).
/// </summary>
public sealed class OvertimeBankReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "overtime-bank",
        Name = "Overtime bank",
        Question = "Whose overtime is banked, how much is left, and what has lapsed",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Year,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var movements = await (
            from b in ctx.Scope.Hr.OvertimeBank.AsNoTracking()
            join e in ctx.Scope.Hr.Employees.AsNoTracking() on b.EmployeeId equals e.Id
            where b.BranchId == ctx.BranchId
                  && (ctx.Filters.EmployeeId == null || b.EmployeeId == ctx.Filters.EmployeeId)
            select new { b.EmployeeId, e.EmployeeCode, e.FullName, b.Kind, b.Minutes })
            .ToListAsync(ct);

        if (movements.Count == 0)
        {
            return ReportTable.Empty(
                "Nothing is banked. Overtime credits the bank only when the employer's rule says to "
                + "bank it instead of paying — set that on Policies → Overtime.");
        }

        var byEmployee = movements
            .GroupBy(m => (m.EmployeeId, m.EmployeeCode, m.FullName))
            .Select(g => new
            {
                g.Key.EmployeeId, g.Key.EmployeeCode, g.Key.FullName,
                Credited = g.Where(x => x.Kind == OvertimeBankKind.Credit).Sum(x => x.Minutes),
                Taken = g.Where(x => x.Kind == OvertimeBankKind.Debit).Sum(x => x.Minutes),
                Lapsed = g.Where(x => x.Kind == OvertimeBankKind.Expiry).Sum(x => x.Minutes),
            })
            .OrderBy(x => x.EmployeeCode)
            .ToList();

        var matched = byEmployee.Count;
        if (matched > ctx.MaxRows) byEmployee = [.. byEmployee.Take(ctx.MaxRows)];

        static ReportCell Mins(int minutes) =>
            new(minutes == 0 ? "—" : $"{minutes / 60}h {minutes % 60:00}m", Number: minutes);

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Employee"),
                new("Banked", ReportAlign.Right), new("Taken", ReportAlign.Right),
                new("Lapsed", ReportAlign.Right), new("Left", ReportAlign.Right),
            ],
            Rows =
            [
                .. byEmployee.Select(x => new ReportRow(
                [
                    new ReportCell(x.EmployeeCode, Href: ctx.Employee(x.EmployeeId)),
                    new ReportCell(x.FullName, Href: ctx.Employee(x.EmployeeId)),
                    Mins(x.Credited), Mins(x.Taken), Mins(x.Lapsed),
                    Mins(x.Credited - x.Taken - x.Lapsed),
                ])),
            ],
            MatchedRows = matched,
            Totals =
            [
                new ReportCell(""),
                new ReportCell($"{byEmployee.Count} employee{(byEmployee.Count == 1 ? "" : "s")}"),
                Mins(byEmployee.Sum(x => x.Credited)),
                Mins(byEmployee.Sum(x => x.Taken)),
                Mins(byEmployee.Sum(x => x.Lapsed)),
                Mins(byEmployee.Sum(x => x.Credited - x.Taken - x.Lapsed)),
            ],
        };
    }
}

/// <summary>§13 — roster coverage (§7.5): required against rostered, per shift per day.</summary>
public sealed class RosterCoverageReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "roster-coverage",
        Name = "Roster coverage",
        Question = "Which shifts are short, on which days, and who is on leave anyway",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
        Landscape = true,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var units = ctx.Filters.OrgUnitIds.Count > 0
            ? ctx.Filters.OrgUnitIds
            : await ctx.Scope.Hr.OrgUnits.AsNoTracking()
                .Where(u => u.BranchId == ctx.BranchId && u.Active)
                .Select(u => u.Id)
                .ToListAsync(ct);

        if (units.Count == 0)
        {
            return ReportTable.Empty(
                "No org unit exists, so there is nothing to cover. Create the org structure first.");
        }

        var names = await ctx.Scope.Hr.OrgUnits.AsNoTracking()
            .ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var cells = new List<(long UnitId, CoverageCell Cell)>();
        foreach (var unit in units)
        {
            var coverage = await RosterPlanService.CoverageAsync(
                ctx.Scope.Hr, ctx.BranchId, unit, ctx.Period.Range.From, ctx.Period.Range.To, ct);
            cells.AddRange(coverage.Select(c => (unit, c)));
        }

        if (cells.Count == 0)
        {
            return ReportTable.Empty(
                $"Nothing is rostered in {PeriodWords.Describe(ctx.Period)}, and no shift requirement "
                + "is configured. Coverage compares what a shift needs against who is on it — set "
                + "the requirement on the roster planner, or roster somebody.");
        }

        var ordered = cells.OrderByDescending(c => c.Cell.IsShort)
            .ThenBy(c => c.Cell.OnDate).ThenBy(c => c.Cell.ShiftName).ToList();
        var matched = ordered.Count;
        if (matched > ctx.MaxRows) ordered = [.. ordered.Take(ctx.MaxRows)];

        return new ReportTable
        {
            Columns =
            [
                new("Date"), new("Unit"), new("Shift"),
                new("Required", ReportAlign.Right), new("Rostered", ReportAlign.Right),
                new("On leave", ReportAlign.Right), new("Available", ReportAlign.Right),
                new("Cover"),
            ],
            Rows =
            [
                .. ordered.Select(x => new ReportRow(
                [
                    new ReportCell(Ui.DateLong(x.Cell.OnDate)),
                    new ReportCell(names.GetValueOrDefault(x.UnitId, "—")),
                    new ReportCell(x.Cell.ShiftName),
                    new ReportCell(x.Cell.Required == 0 ? "—" : x.Cell.Required.ToString(),
                        Number: x.Cell.Required),
                    new ReportCell(x.Cell.Rostered.ToString(), Number: x.Cell.Rostered),
                    new ReportCell(x.Cell.OnLeave == 0 ? "—" : x.Cell.OnLeave.ToString(),
                        Number: x.Cell.OnLeave),
                    new ReportCell(x.Cell.Available.ToString(), Number: x.Cell.Available),
                    new ReportCell(
                        x.Cell.Required == 0 ? "no requirement set"
                            : x.Cell.IsShort ? $"{x.Cell.Short} short" : "covered",
                        PillCss: x.Cell.Required == 0 ? "" : x.Cell.IsShort ? "bad" : "good"),
                ])),
            ],
            MatchedRows = matched,
        };
    }
}
