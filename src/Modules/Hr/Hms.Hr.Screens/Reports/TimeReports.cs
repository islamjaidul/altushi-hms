using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Reports;

internal static class Time
{
    /// <summary>
    /// The attendance days a filter selects. Note the index this rides:
    /// <c>(branch_id, on_date, status)</c> already exists, which is what makes a period-ranged
    /// attendance report answerable on the deployment ceiling.
    /// </summary>
    internal static IQueryable<AttendanceDay> Days(ReportContext ctx)
    {
        var (from, to) = (ctx.Period.Range.From, ctx.Period.Range.To);
        var q = ctx.Scope.Hr.AttendanceDays.AsNoTracking()
            .Where(d => d.BranchId == ctx.BranchId && d.OnDate >= from && d.OnDate <= to);

        if (ctx.Filters.EmployeeId is { } id) q = q.Where(d => d.EmployeeId == id);
        return q;
    }

    /// <summary>Restricts to the employees an org/designation/grade filter selects, or null when none is set.</summary>
    internal static async Task<HashSet<long>?> ScopedEmployeesAsync(ReportContext ctx, CancellationToken ct)
    {
        if (ctx.Filters.OrgUnitIds.Count == 0 && ctx.Filters.DesignationId is null
            && ctx.Filters.GradeId is null)
        {
            return null;
        }

        var ids = await People.AssignmentsOn(ctx, ctx.Period.Range.To)
            .Select(a => a.EmployeeId).Distinct().ToListAsync(ct);
        return [.. ids];
    }

    internal static string StatusWord(string status) => status switch
    {
        AttendanceStatus.Present => "Present",
        AttendanceStatus.Absent => "Absent",
        AttendanceStatus.WeeklyOff => "Weekly off",
        AttendanceStatus.Holiday => "Holiday",
        AttendanceStatus.OnLeave => "On leave",
        AttendanceStatus.Errand => "Outdoor duty",
        AttendanceStatus.Incomplete => "Incomplete",
        _ => status,
    };

    /// <summary>The muster roll's single-letter cell code. Letters first, colour second (§7 U12).</summary>
    internal static string Code(string status) => status switch
    {
        AttendanceStatus.Present => "P",
        AttendanceStatus.Absent => "A",
        AttendanceStatus.WeeklyOff => "W",
        AttendanceStatus.Holiday => "H",
        AttendanceStatus.OnLeave => "L",
        AttendanceStatus.Errand => "O",
        AttendanceStatus.Incomplete => "?",
        _ => "·",
    };

    internal static string Pill(string status) => status switch
    {
        AttendanceStatus.Present => "good",
        AttendanceStatus.Absent or AttendanceStatus.Incomplete => "bad",
        AttendanceStatus.OnLeave => "warn",
        _ => "",
    };

    internal static string Hours(int minutes) => $"{minutes / 60}h {minutes % 60:D2}m";
}

/// <summary>
/// §13 Time — "Employee × day matrix, the paper format HR already uses". The register a Bangladeshi
/// HR office keeps on paper, so it prints landscape and codes each cell with a letter (G30).
/// </summary>
public sealed class AttendanceRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "attendance-register",
        Name = "Monthly attendance register (muster roll)",
        Question = "Employee × day, the format the office already keeps on paper",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
        Landscape = true,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var range = ctx.Period.Range;

        // A muster roll is only readable a month at a time, and employee × day grows as their
        // product: 1,000 people over a year is 365,000 cells, which is not a sheet anybody reads.
        if (range.Days > 31)
        {
            return ReportTable.Empty(
                $"The muster roll is a month at a time — this period is {range.Days:N0} days. "
                + "Choose Month in the period selector, or a custom range of 31 days or fewer.");
        }

        var scoped = await Time.ScopedEmployeesAsync(ctx, ct);
        var days = await Time.Days(ctx)
            .Select(d => new { d.EmployeeId, d.OnDate, d.Status })
            .ToListAsync(ct);

        if (scoped is not null) days = [.. days.Where(d => scoped.Contains(d.EmployeeId))];

        if (days.Count == 0)
        {
            return ReportTable.Empty(
                $"No attendance has been derived for {PeriodWords.Range(range)}. "
                + "Import punches on Attendance review, then the register fills in.");
        }

        var employeeIds = days.Select(d => d.EmployeeId).Distinct().ToList();
        var employees = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && employeeIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeCode, e.FullName })
            .OrderBy(e => e.EmployeeCode)
            .Take(ctx.MaxRows)
            .ToListAsync(ct);

        var cell = days.ToLookup(d => (d.EmployeeId, d.OnDate));

        var columns = new List<ReportColumn> { new("Code"), new("Name") };
        var dates = new List<DateOnly>();
        for (var d = range.From; d <= range.To; d = d.AddDays(1))
        {
            dates.Add(d);
            columns.Add(new ReportColumn(d.Day.ToString(), ReportAlign.Centre, Sortable: false));
        }
        columns.Add(new ReportColumn("P", ReportAlign.Right, Sortable: false));
        columns.Add(new ReportColumn("A", ReportAlign.Right, Sortable: false));
        columns.Add(new ReportColumn("L", ReportAlign.Right, Sortable: false));

        var rows = new List<ReportRow>(employees.Count);
        foreach (var e in employees)
        {
            var cells = new List<ReportCell>
            {
                new(e.EmployeeCode, Href: ctx.Employee(e.Id)),
                new(e.FullName, Href: ctx.Employee(e.Id)),
            };
            int present = 0, absent = 0, leave = 0;

            foreach (var d in dates)
            {
                var status = cell[(e.Id, d)].FirstOrDefault()?.Status;
                if (status is null) { cells.Add(new ReportCell("·")); continue; }

                if (status == AttendanceStatus.Present) present++;
                else if (status == AttendanceStatus.Absent) absent++;
                else if (status == AttendanceStatus.OnLeave) leave++;

                cells.Add(new ReportCell(Time.Code(status)));
            }

            cells.Add(new ReportCell(present.ToString(), present));
            cells.Add(new ReportCell(absent.ToString(), absent));
            cells.Add(new ReportCell(leave.ToString(), leave));
            rows.Add(new ReportRow(cells));
        }

        return new ReportTable
        {
            Columns = columns,
            Rows = rows,
            MatchedRows = employeeIds.Count,
        };
    }
}

/// <summary>§13 Time — "Present, absent, late, on leave, on which shift, by unit".</summary>
public sealed class DailyAttendanceSummaryReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "daily-attendance-summary",
        Name = "Daily attendance summary",
        Question = "Present, absent, late and on leave, day by day",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        // Aggregated in SQL. At 1,000 employees a year is ~365,000 rows, which must never be
        // materialised (N-HR1, N-HR2) — GroupBy before ToListAsync, never after.
        var byDay = await Time.Days(ctx)
            .GroupBy(d => d.OnDate)
            .Select(g => new
            {
                OnDate = g.Key,
                Present = g.Count(x => x.Status == AttendanceStatus.Present),
                Absent = g.Count(x => x.Status == AttendanceStatus.Absent),
                Leave = g.Count(x => x.Status == AttendanceStatus.OnLeave),
                Incomplete = g.Count(x => x.Status == AttendanceStatus.Incomplete),
                Late = g.Count(x => x.LateMinutes > 0),
                LateMinutes = g.Sum(x => x.LateMinutes),
                Overtime = g.Sum(x => x.OvertimeMinutes),
            })
            .OrderBy(r => r.OnDate)
            .ToListAsync(ct);

        if (byDay.Count == 0)
        {
            return ReportTable.Empty(
                $"No attendance was derived between {PeriodWords.Range(ctx.Period.Range)}. "
                + "Import punches on Attendance review, or widen the period.");
        }

        var rows = byDay.Select(r => new ReportRow(
        [
            new ReportCell(Ui.DateLong(r.OnDate)),
            new ReportCell(r.Present.ToString("N0"), r.Present),
            new ReportCell(r.Absent.ToString("N0"), r.Absent,
                r.Absent > 0 ? ctx.Drill("absence-register", ("on", r.OnDate.ToString("yyyy-MM-dd"))) : null),
            new ReportCell(r.Leave.ToString("N0"), r.Leave),
            new ReportCell(r.Late.ToString("N0"), r.Late,
                r.Late > 0 ? ctx.Drill("late-register", ("on", r.OnDate.ToString("yyyy-MM-dd"))) : null),
            new ReportCell(Time.Hours(r.LateMinutes), r.LateMinutes),
            new ReportCell(Time.Hours(r.Overtime), r.Overtime),
            new ReportCell(r.Incomplete.ToString("N0"), r.Incomplete,
                r.Incomplete > 0 ? ctx.Drill("attendance-exceptions") : null,
                r.Incomplete > 0 ? "bad" : null),
        ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Date"), new("Present", ReportAlign.Right), new("Absent", ReportAlign.Right),
                new("On leave", ReportAlign.Right), new("Late", ReportAlign.Right),
                new("Late time", ReportAlign.Right), new("Overtime", ReportAlign.Right),
                new("Exceptions", ReportAlign.Right),
            ],
            Rows = rows,
            MatchedRows = byDay.Count,
            Totals =
            [
                new ReportCell("Total"),
                new ReportCell(byDay.Sum(r => r.Present).ToString("N0")),
                new ReportCell(byDay.Sum(r => r.Absent).ToString("N0")),
                new ReportCell(byDay.Sum(r => r.Leave).ToString("N0")),
                new ReportCell(byDay.Sum(r => r.Late).ToString("N0")),
                new ReportCell(Time.Hours(byDay.Sum(r => r.LateMinutes))),
                new ReportCell(Time.Hours(byDay.Sum(r => r.Overtime))),
                new ReportCell(byDay.Sum(r => r.Incomplete).ToString("N0")),
            ],
        };
    }
}

/// <summary>§13 Time — "Who, how often, how many minutes".</summary>
public sealed class LateRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "late-register",
        Name = "Late arrival & early departure register",
        Question = "Who was late, how often and by how much",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var byEmployee = await Time.Days(ctx)
            .Where(d => d.LateMinutes > 0 || d.EarlyOutMinutes > 0)
            .GroupBy(d => d.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                LateDays = g.Count(x => x.LateMinutes > 0),
                LateMinutes = g.Sum(x => x.LateMinutes),
                EarlyDays = g.Count(x => x.EarlyOutMinutes > 0),
                EarlyMinutes = g.Sum(x => x.EarlyOutMinutes),
            })
            .ToListAsync(ct);

        if (byEmployee.Count == 0)
        {
            return ReportTable.Empty(
                $"Nobody was late or left early between {PeriodWords.Range(ctx.Period.Range)}. "
                + "Late minutes are counted against the rostered shift, so an employee with no "
                + "roster produces none.");
        }

        var scoped = await Time.ScopedEmployeesAsync(ctx, ct);
        if (scoped is not null) byEmployee = [.. byEmployee.Where(x => scoped.Contains(x.EmployeeId))];

        var ids = byEmployee.Select(x => x.EmployeeId).ToList();
        var names = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeCode, e.FullName }, ct);

        var ordered = byEmployee.OrderByDescending(x => x.LateMinutes).Take(ctx.MaxRows).ToList();

        var rows = ordered.Select(x =>
        {
            names.TryGetValue(x.EmployeeId, out var who);
            return new ReportRow(
            [
                new ReportCell(who?.EmployeeCode ?? "—", Href: ctx.Employee(x.EmployeeId)),
                new ReportCell(who?.FullName ?? "—", Href: ctx.Employee(x.EmployeeId)),
                new ReportCell(x.LateDays.ToString("N0"), x.LateDays),
                new ReportCell(Time.Hours(x.LateMinutes), x.LateMinutes),
                new ReportCell(x.EarlyDays.ToString("N0"), x.EarlyDays),
                new ReportCell(Time.Hours(x.EarlyMinutes), x.EarlyMinutes),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Late days", ReportAlign.Right),
                new("Late time", ReportAlign.Right), new("Early days", ReportAlign.Right),
                new("Early time", ReportAlign.Right),
            ],
            Rows = rows,
            MatchedRows = byEmployee.Count,
            Totals =
            [
                new ReportCell("Total"), new ReportCell(""),
                new ReportCell(byEmployee.Sum(x => x.LateDays).ToString("N0")),
                new ReportCell(Time.Hours(byEmployee.Sum(x => x.LateMinutes))),
                new ReportCell(byEmployee.Sum(x => x.EarlyDays).ToString("N0")),
                new ReportCell(Time.Hours(byEmployee.Sum(x => x.EarlyMinutes))),
            ],
        };
    }
}

/// <summary>§13 Time — "Absent without leave, by employee and period".</summary>
public sealed class AbsenceRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "absence-register",
        Name = "Absence register",
        Question = "Who was absent, and on which days",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var query = Time.Days(ctx).Where(d => d.Status == AttendanceStatus.Absent);

        // Carried by a drill-through from the daily summary: one day of the period, not all of it.
        if (ctx.Raw.One("on") is { } onRaw && DateOnly.TryParse(onRaw, out var on))
            query = query.Where(d => d.OnDate == on);

        var absences = await query
            .OrderBy(d => d.OnDate)
            .Select(d => new { d.EmployeeId, d.OnDate })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        if (absences.Count == 0)
        {
            return ReportTable.Empty(
                $"Nobody was marked absent between {PeriodWords.Range(ctx.Period.Range)}. "
                + "An approved leave day counts as leave, not absence — the leave register shows those.");
        }

        var matched = absences.Count;
        if (matched > ctx.MaxRows) absences = [.. absences.Take(ctx.MaxRows)];

        var ids = absences.Select(a => a.EmployeeId).Distinct().ToList();
        var names = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeCode, e.FullName }, ct);

        var rows = absences.Select(a =>
        {
            names.TryGetValue(a.EmployeeId, out var who);
            return new ReportRow(
            [
                new ReportCell(Ui.DateLong(a.OnDate)),
                new ReportCell(who?.EmployeeCode ?? "—", Href: ctx.Employee(a.EmployeeId)),
                new ReportCell(who?.FullName ?? "—", Href: ctx.Employee(a.EmployeeId)),
                new ReportCell("Absent", PillCss: "bad"),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns = [new("Date"), new("Code"), new("Name"), new("Status")],
            Rows = rows,
            MatchedRows = matched,
        };
    }
}

/// <summary>§13 Time — "OT minutes by employee, approved vs derived".</summary>
public sealed class OvertimeRegisterReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "overtime-register",
        Name = "Overtime register",
        Question = "Who worked overtime, and how much",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var byEmployee = await Time.Days(ctx)
            .Where(d => d.OvertimeMinutes > 0)
            .GroupBy(d => d.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Days = g.Count(),
                Minutes = g.Sum(x => x.OvertimeMinutes),
            })
            .ToListAsync(ct);

        if (byEmployee.Count == 0)
        {
            return ReportTable.Empty(
                $"No overtime was derived between {PeriodWords.Range(ctx.Period.Range)}. "
                + "Overtime is derived against the rostered shift and an overtime rule — if neither "
                + "is configured, none is produced.");
        }

        var ids = byEmployee.Select(x => x.EmployeeId).ToList();
        var names = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeCode, e.FullName }, ct);

        var rows = byEmployee.OrderByDescending(x => x.Minutes).Take(ctx.MaxRows).Select(x =>
        {
            names.TryGetValue(x.EmployeeId, out var who);
            return new ReportRow(
            [
                new ReportCell(who?.EmployeeCode ?? "—", Href: ctx.Employee(x.EmployeeId)),
                new ReportCell(who?.FullName ?? "—", Href: ctx.Employee(x.EmployeeId)),
                new ReportCell(x.Days.ToString("N0"), x.Days),
                new ReportCell(Time.Hours(x.Minutes), x.Minutes),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Code"), new("Name"), new("Days", ReportAlign.Right),
                new("Overtime", ReportAlign.Right),
            ],
            Rows = rows,
            MatchedRows = byEmployee.Count,
            Totals =
            [
                new ReportCell("Total"), new ReportCell(""),
                new ReportCell(byEmployee.Sum(x => x.Days).ToString("N0")),
                new ReportCell(Time.Hours(byEmployee.Sum(x => x.Minutes))),
            ],
        };
    }
}

/// <summary>§13 Time — "What needed a human, what was done, by whom, why" (G3 for attendance).</summary>
public sealed class AttendanceExceptionsReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "attendance-exceptions",
        Name = "Attendance exception log",
        Question = "What needed a human, and whether it has been resolved",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        // The same definition the Attendance screen and AttendanceService use: incomplete OR absent.
        // The old dashboard counted only `incomplete` and therefore under-reported what blocks payroll.
        var exceptions = await Time.Days(ctx)
            .Where(d => d.Status == AttendanceStatus.Incomplete || d.Status == AttendanceStatus.Absent)
            .OrderBy(d => d.OnDate)
            .Select(d => new { d.Id, d.EmployeeId, d.OnDate, d.Status, d.Corrected, d.FirstIn, d.LastOut })
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        if (exceptions.Count == 0)
        {
            return ReportTable.Empty(
                $"No attendance exception was raised between {PeriodWords.Range(ctx.Period.Range)} — "
                + "nothing is blocking payroll for this period.");
        }

        var matched = exceptions.Count;
        if (matched > ctx.MaxRows) exceptions = [.. exceptions.Take(ctx.MaxRows)];

        var ids = exceptions.Select(e => e.EmployeeId).Distinct().ToList();
        var names = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeCode, e.FullName }, ct);

        var rows = exceptions.Select(x =>
        {
            names.TryGetValue(x.EmployeeId, out var who);
            return new ReportRow(
            [
                new ReportCell(Ui.DateLong(x.OnDate)),
                new ReportCell(who?.EmployeeCode ?? "—", Href: ctx.Employee(x.EmployeeId)),
                new ReportCell(who?.FullName ?? "—", Href: ctx.Employee(x.EmployeeId)),
                new ReportCell(Time.StatusWord(x.Status), PillCss: Time.Pill(x.Status)),
                new ReportCell(x.FirstIn is { } i ? Ui.Time(i) : "—"),
                new ReportCell(x.LastOut is { } o ? Ui.Time(o) : "—"),
                new ReportCell(x.Corrected ? "Corrected" : "Open",
                    PillCss: x.Corrected ? "good" : "warn"),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Date"), new("Code"), new("Name"), new("Exception"),
                new("First in", Sortable: false), new("Last out", Sortable: false), new("State"),
            ],
            Rows = rows,
            MatchedRows = matched,
        };
    }
}

/// <summary>§13 Time — "Every amendment with its reason".</summary>
public sealed class CorrectionLogReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "correction-log",
        Name = "Attendance correction log",
        Question = "Every amendment, who made it and why",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var (start, end) = ctx.Period.Range.Instants();

        var corrections = await ctx.Scope.Hr.AttendanceCorrections.AsNoTracking()
            .Where(c => c.BranchId == ctx.BranchId && c.CorrectedAt >= start && c.CorrectedAt < end)
            .OrderByDescending(c => c.CorrectedAt)
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        if (corrections.Count == 0)
        {
            return ReportTable.Empty(
                $"No attendance was corrected between {PeriodWords.Range(ctx.Period.Range)}. "
                + "Corrections are recorded when someone amends a derived day on Attendance review.");
        }

        var matched = corrections.Count;
        if (matched > ctx.MaxRows) corrections = [.. corrections.Take(ctx.MaxRows)];

        // The correction points at the attendance day, which is what names the employee and the date.
        var dayIds = corrections.Select(c => c.AttendanceDayId).Distinct().ToList();
        var days = await ctx.Scope.Hr.AttendanceDays.AsNoTracking()
            .Where(d => dayIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => new { d.EmployeeId, d.OnDate }, ct);

        var employeeIds = days.Values.Select(d => d.EmployeeId).Distinct().ToList();
        var names = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && employeeIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        var rows = corrections.Select(c =>
        {
            days.TryGetValue(c.AttendanceDayId, out var day);
            var who = day is not null && names.TryGetValue(day.EmployeeId, out var n) ? n : "—";
            return new ReportRow(
            [
                new ReportCell(day is not null ? Ui.DateLong(day.OnDate) : "—"),
                new ReportCell(who, Href: day is not null ? ctx.Employee(day.EmployeeId) : null),
                new ReportCell(Time.StatusWord(c.FromStatus)),
                new ReportCell(Time.StatusWord(c.ToStatus)),
                // §12.6: a reason is first-class and shown where the change is seen, never buried.
                new ReportCell(c.Reason),
                new ReportCell(c.CorrectedByName),
                new ReportCell(Ui.DateTimeShort(c.CorrectedAt)),
                new ReportCell(c.ArrearsForRunId is null ? "—" : c.ArrearsSettled ? "Arrears paid" : "Arrears pending",
                    PillCss: c.ArrearsForRunId is null ? null : c.ArrearsSettled ? "good" : "warn"),
            ]);
        }).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Day"), new("Employee"), new("From"), new("To"), new("Reason", Sortable: false),
                new("Corrected by"), new("Corrected at"), new("Arrears"),
            ],
            Rows = rows,
            MatchedRows = matched,
        };
    }
}

/// <summary>§13 Time — "Which devices reported, which files landed, what was rejected".</summary>
public sealed class ImportHealthReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "import-health",
        Name = "Device & import health",
        Question = "Which files landed, what was accepted and what was rejected",
        Category = ReportCategory.Time,
        SalaryBearing = false,
        DefaultGrain = PeriodGrain.Month,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var (start, end) = ctx.Period.Range.Instants();

        var batches = await ctx.Scope.Hr.PunchImportBatches.AsNoTracking()
            .Where(b => b.BranchId == ctx.BranchId && b.ImportedAt >= start && b.ImportedAt < end)
            .OrderByDescending(b => b.ImportedAt)
            .Take(ctx.MaxRows)
            .ToListAsync(ct);

        if (batches.Count == 0)
        {
            return ReportTable.Empty(
                $"No punch file was imported between {PeriodWords.Range(ctx.Period.Range)}. "
                + "Files are imported on Attendance review; a live device feed is not yet built.");
        }

        var rows = batches.Select(b => new ReportRow(
        [
            new ReportCell(Ui.DateTimeShort(b.ImportedAt)),
            new ReportCell(b.FileName),
            new ReportCell(b.RowsRead.ToString("N0"), b.RowsRead),
            new ReportCell(b.RowsAccepted.ToString("N0"), b.RowsAccepted),
            // Duplicates are the normal, healthy result of re-importing an overlapping export —
            // UNIQUE(employee, device, punched_at) is what makes that a no-op rather than a mess.
            new ReportCell(b.RowsDuplicate.ToString("N0"), b.RowsDuplicate),
            new ReportCell(b.RowsRejected.ToString("N0"), b.RowsRejected,
                PillCss: b.RowsRejected > 0 ? "bad" : null),
        ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("Imported at"), new("File"), new("Rows read", ReportAlign.Right),
                new("Accepted", ReportAlign.Right), new("Already had", ReportAlign.Right),
                new("Rejected", ReportAlign.Right),
            ],
            Rows = rows,
            MatchedRows = batches.Count,
            Totals =
            [
                new ReportCell("Total"), new ReportCell(""),
                new ReportCell(batches.Sum(b => b.RowsRead).ToString("N0")),
                new ReportCell(batches.Sum(b => b.RowsAccepted).ToString("N0")),
                new ReportCell(batches.Sum(b => b.RowsDuplicate).ToString("N0")),
                new ReportCell(batches.Sum(b => b.RowsRejected).ToString("N0")),
            ],
        };
    }
}
