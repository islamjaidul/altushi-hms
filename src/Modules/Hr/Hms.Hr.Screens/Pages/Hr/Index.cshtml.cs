using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

/// <summary>One thing needing a person: a count, a severity, and somewhere to go.</summary>
public sealed record ActionRow(string Label, int Count, string Detail, string Href, string Severity);

/// <summary>A tile. Every figure is a door (D4) — none of these is a dead number.</summary>
public sealed record Tile(string Label, string Value, string Sub, string Href);

public sealed record HeadcountRow(string OrgUnit, int Headcount, string Href);

/// <summary>
/// The HR command centre (module PRD §14.1, US16.5) — what needs me today, and let me act from it.
/// <para>
/// Rebuilt in spec 0055 from five dead tiles and a headcount table. Three things changed: it takes a
/// period rather than being hardcoded to today, every figure links to the rows behind it (D4), and
/// an action-required panel says what is waiting — or says plainly that nothing is.
/// </para>
/// <para>
/// It also fixes a defect. The old exception count read <c>Incomplete</c> only, while
/// <c>AttendanceService.ExceptionsAsync</c> and the Attendance screen both count
/// <c>Incomplete OR Absent</c> — so the dashboard quietly under-reported the very thing that blocks
/// payroll.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.Read)]
public class IndexModel(IHrTx tx, IPeriodCalendarSource calendars, TimeProvider clock) : HmsPageModel
{
    public Period Period { get; private set; } = null!;

    public PeriodBar Bar { get; private set; } = null!;

    public DateOnly Today { get; private set; }

    public IReadOnlyList<ActionRow> Actions { get; private set; } = [];

    public IReadOnlyList<Tile> Tiles { get; private set; } = [];

    public IReadOnlyList<HeadcountRow> ByUnit { get; private set; } = [];

    public string? OpenPayrollPeriod { get; private set; }

    public string? OpenPayrollState { get; private set; }

    public string? OpenPayrollNextAction { get; private set; }

    public bool CanSeeSalary => Can("hr.salary.read");

    private string _query = "";

    public async Task OnGetAsync(CancellationToken ct)
    {
        Today = Dhaka.Today(clock);
        var calendar = await calendars.ForAsync(BranchId, Today, ct);

        Period = PeriodBinding.Resolve(Request, Today, calendar, PeriodGrain.Month);
        Bar = PeriodBar.For(Period, Request, calendar, Today);
        PeriodBinding.Remember(Response, Period);

        _query = string.Join('&', PeriodBinding.PeriodPairs(Period)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        await tx.RunAsync(async s => await LoadAsync(s, ct), ct);
    }

    private string Report(string key, string? extra = null)
        => $"/hr/reports/{key}?{_query}" + (extra is null ? "" : "&" + extra);

    private async Task LoadAsync(HrScope s, CancellationToken ct)
    {
        var (from, to) = (Period.Range.From, Period.Range.To);

        var headcount = await s.Hr.Employees.AsNoTracking()
            .CountAsync(e => e.BranchId == BranchId && e.JoinedOn <= to
                             && (e.SeparatedOn == null || e.SeparatedOn > to), ct);

        var joiners = await s.Hr.Employees.AsNoTracking()
            .CountAsync(e => e.BranchId == BranchId && e.JoinedOn >= from && e.JoinedOn <= to, ct);

        var leavers = await s.Hr.Employees.AsNoTracking()
            .CountAsync(e => e.BranchId == BranchId && e.SeparatedOn != null
                             && e.SeparatedOn >= from && e.SeparatedOn <= to, ct);

        // One round trip for today's composition, picked apart in memory.
        var todayCounts = await s.Hr.AttendanceDays.AsNoTracking()
            .Where(d => d.BranchId == BranchId && d.OnDate == Today)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Count(string status) => todayCounts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        var lateToday = await s.Hr.AttendanceDays.AsNoTracking()
            .CountAsync(d => d.BranchId == BranchId && d.OnDate == Today && d.LateMinutes > 0, ct);

        var overtimeMinutes = await s.Hr.AttendanceDays.AsNoTracking()
            .Where(d => d.BranchId == BranchId && d.OnDate >= from && d.OnDate <= to)
            .SumAsync(d => (int?)d.OvertimeMinutes, ct) ?? 0;

        // The definition AttendanceService and the Attendance screen use — see the class comment.
        var exceptions = await s.Hr.AttendanceDays.AsNoTracking()
            .CountAsync(d => d.BranchId == BranchId && d.OnDate >= from && d.OnDate <= to
                             && (d.Status == AttendanceStatus.Incomplete
                                 || d.Status == AttendanceStatus.Absent), ct);

        var pendingLeave = await s.Hr.LeaveApplications.AsNoTracking()
            .Where(l => l.BranchId == BranchId
                        && (l.State == LeaveApplicationState.Applied
                            || l.State == LeaveApplicationState.Recommended))
            .Select(l => l.AppliedAt)
            .ToListAsync(ct);

        var tiles = new List<Tile>
        {
            new("Headcount", headcount.ToString("N0"), $"in service at {Ui.DateLong(to)}",
                Report("employee-directory")),
            new("Joiners", joiners.ToString("N0"), "started in this period", Report("new-joiners")),
            new("Leavers", leavers.ToString("N0"), "left in this period", Report("separations")),
            new("Present today", Count(AttendanceStatus.Present).ToString("N0"),
                Ui.DateLong(Today), Report("daily-attendance-summary")),
            new("Absent today", Count(AttendanceStatus.Absent).ToString("N0"),
                "no punch, no leave", Report("absence-register")),
            new("On leave today", Count(AttendanceStatus.OnLeave).ToString("N0"),
                "approved leave in effect", Report("leave-register")),
            new("Late today", lateToday.ToString("N0"), "arrived after the shift start",
                Report("late-register")),
            new("Overtime", $"{overtimeMinutes / 60:N0} h", "in this period",
                Report("overtime-register")),
        };

        // D6: the cost tiles exist only for a reader holding salary read. Not hidden with CSS —
        // never computed, so there is nothing in the response to find.
        if (CanSeeSalary)
        {
            var totals = await s.Hr.PayrollRuns.AsNoTracking()
                .Where(r => r.BranchId == BranchId && r.Period >= from && r.Period <= to
                            && r.State != PayrollRunState.Cancelled)
                .GroupBy(r => 1)
                .Select(g => new
                {
                    Gross = g.Sum(x => x.TotalGrossTaka),
                    People = g.Sum(x => x.EmployeeCount),
                })
                .FirstOrDefaultAsync(ct);

            if (totals is not null)
            {
                tiles.Add(new Tile("Payroll cost", Ui.Group(totals.Gross), "gross for this period",
                    Report("salary-summary-by-unit")));
                tiles.Add(new Tile("Average per head",
                    totals.People == 0 ? "—" : Ui.Group(totals.Gross / totals.People),
                    "gross ÷ employees on the run", Report("salary-sheet")));
            }
        }

        Tiles = tiles;

        var units = await s.Hr.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        // As at the end of the period, not "the row with no end date" — that is what makes the
        // headcount for last March reproducible rather than today's answer wearing March's label.
        var byUnit = await s.Hr.Assignments.AsNoTracking()
            .Where(a => a.BranchId == BranchId && a.EffectiveFrom <= to
                        && (a.EffectiveTo == null || a.EffectiveTo >= to))
            .GroupBy(a => a.OrgUnitId)
            .Select(g => new { OrgUnitId = g.Key, Count = g.Select(x => x.EmployeeId).Distinct().Count() })
            .ToListAsync(ct);

        ByUnit = [.. byUnit
            .Select(x => new HeadcountRow(
                units.GetValueOrDefault(x.OrgUnitId, "Unassigned"),
                x.Count,
                Report("employee-directory", $"unit={x.OrgUnitId}")))
            .OrderByDescending(r => r.Headcount)];

        var open = await s.Hr.PayrollRuns.AsNoTracking()
            .Where(r => r.BranchId == BranchId && r.State != PayrollRunState.Posted
                        && r.State != PayrollRunState.Cancelled)
            .OrderByDescending(r => r.Period)
            .FirstOrDefaultAsync(ct);

        if (open is not null)
        {
            OpenPayrollPeriod = open.Period.ToString("MMMM yyyy");
            OpenPayrollState = Reports.Pay.StateWord(open.State);
            OpenPayrollNextAction = NextAction(open.State);
        }

        BuildActions(exceptions, pendingLeave, open);
    }

    /// <summary>
    /// §14.1 row 1 — the panel that decides whether the module is useful at 9am. Each item is a
    /// count, a severity and a link; when it is empty the screen says so rather than showing nothing.
    /// </summary>
    private void BuildActions(int exceptions, List<DateTimeOffset> pendingLeave, PayrollRun? open)
    {
        var actions = new List<ActionRow>();

        if (exceptions > 0)
        {
            actions.Add(new ActionRow(
                "Attendance exceptions", exceptions,
                "payroll for this period is not trustworthy until these are reviewed",
                Report("attendance-exceptions"), "bad"));
        }

        if (pendingLeave.Count > 0)
        {
            var age = Today.DayNumber - Dhaka.DateOf(pendingLeave.Min()).DayNumber;
            actions.Add(new ActionRow(
                "Leave awaiting a decision", pendingLeave.Count,
                $"the oldest has waited {age:N0} day(s)",
                Report("pending-leave-ageing"), age >= 7 ? "bad" : "warn"));
        }

        if (open is not null)
        {
            actions.Add(new ActionRow(
                $"Payroll — {open.Period:MMMM yyyy}", open.EmployeeCount,
                $"{Reports.Pay.StateWord(open.State).ToLowerInvariant()} · {NextAction(open.State)}",
                "/hr/payroll", "warn"));
        }

        Actions = actions;
    }

    private static string NextAction(string state) => state switch
    {
        PayrollRunState.Generated => "review the exceptions next",
        PayrollRunState.ExceptionsReviewed => "send it for approval next",
        PayrollRunState.Approved => "lock it next",
        PayrollRunState.Locked => "post it to accounts next",
        _ => "open it to see what is next",
    };
}
