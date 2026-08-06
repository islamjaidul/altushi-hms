using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

/// <summary>
/// Who is out, when (module PRD §7.6, G32).
/// <para>
/// The clash an approver needs to see <b>before</b> they approve, on the one month grid three specs
/// deferred: 0055 called the leave calendar "calendar-shaped", 0058 deferred the attendance and
/// holiday calendars for the same reason, and this is the component all three were waiting for.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.Read)]
public class LeaveCalendarModel(IHrTx tx, IPeriodCalendarSource calendars) : HmsPageModel
{
    public MonthGrid Grid { get; private set; } = null!;
    public List<SelectListItem> Units { get; private set; } = [];
    public DateOnly Month { get; private set; }
    public long? UnitId { get; private set; }
    public int OutToday { get; private set; }

    public async Task OnGetAsync(string? month, long? unit)
    {
        var today = Dhaka.Today(TimeProvider.System);
        Month = FlexibleDate.TryParse(month ?? "", out var m) && m != default
            ? new DateOnly(m.Year, m.Month, 1)
            : new DateOnly(today.Year, today.Month, 1);
        UnitId = unit;

        var calendar = await calendars.ForAsync(BranchId, Month);
        // Named monthStart/monthEnd rather than from/to: `from` is a LINQ query keyword, and a local
        // called `from` inside a query expression is a parse error rather than a shadowing warning.
        // The same trap `by` sprang in spec 0056.
        var monthStart = Month;
        var monthEnd = Month.AddMonths(1).AddDays(-1);

        await tx.RunAsync(async s =>
        {
            Units = await s.Hr.OrgUnits.AsNoTracking()
                .Where(u => u.BranchId == BranchId && u.Active).OrderBy(u => u.Name)
                .Select(u => new SelectListItem(u.Name, u.Id.ToString()))
                .ToListAsync();

            // Scoped to a unit when one is chosen: "who is out" is a question about a team, and the
            // whole employer's calendar is unreadable at 300 people.
            var members = unit is { } chosen
                ? await s.Hr.Assignments.AsNoTracking()
                    .Where(a => a.OrgUnitId == chosen && a.EffectiveFrom <= monthEnd
                                && (a.EffectiveTo == null || a.EffectiveTo >= monthStart))
                    .Select(a => a.EmployeeId).Distinct().ToListAsync()
                : null;

            var leave = await (
                from l in s.Hr.LeaveApplications.AsNoTracking()
                join e in s.Hr.Employees.AsNoTracking() on l.EmployeeId equals e.Id
                join t in s.Hr.LeaveTypes.AsNoTracking() on l.LeaveTypeId equals t.Id
                where l.FromDate <= monthEnd && l.ToDate >= monthStart
                      && (l.State == LeaveApplicationState.Approved
                          || l.State == LeaveApplicationState.Availed
                          || l.State == LeaveApplicationState.Applied)
                orderby e.EmployeeCode
                select new
                {
                    e.Id, e.EmployeeCode, e.FullName, l.FromDate, l.ToDate, l.State,
                    TypeName = t.Name,
                })
                .ToListAsync();

            if (members is not null)
                leave = [.. leave.Where(l => members.Contains(l.Id))];

            if (leave.Count == 0)
            {
                Grid = MonthGrid.Empty(Month, calendar.WeekStartsOn,
                    "Nobody is on leave this month" + (unit is null ? "." : " in this unit."));
                return;
            }

            var rows = leave
                .GroupBy(l => (l.Id, l.EmployeeCode, l.FullName))
                .OrderBy(g => g.Key.EmployeeCode)
                .Select(g => new GridRow(
                    g.Key.FullName, g.Key.EmployeeCode,
                    [
                        .. Enumerable.Range(0, monthEnd.DayNumber - monthStart.DayNumber + 1)
                            .Select(monthStart.AddDays)
                            .Select(day => (Day: day, App: g.FirstOrDefault(
                                l => l.FromDate <= day && l.ToDate >= day)))
                            .Where(x => x.App is not null)
                            .Select(x => new GridCell(
                                x.Day,
                                // Applied but not yet decided reads differently from approved: it is
                                // the clash an approver is looking at when they decide.
                                x.App!.State == LeaveApplicationState.Applied ? "?" : "L",
                                x.App.State == LeaveApplicationState.Applied ? "warn" : "bad",
                                $"{x.App.TypeName} — {x.App.State}")),
                    ],
                    $"/hr/employees/{g.Key.Id}"))
                .ToList();

            Grid = new MonthGrid(Month, calendar.WeekStartsOn, rows, MonthGrid.DaysOf(Month));

            var todayInMonth = today >= monthStart && today <= monthEnd ? today : monthStart;
            OutToday = leave.Count(l => l.FromDate <= todayInMonth && l.ToDate >= todayInMonth);
        });
    }

    public string MonthHref(int delta) =>
        $"/hr/leave-calendar?month={Month.AddMonths(delta):dd/MM/yyyy}"
        + (UnitId is { } u ? $"&unit={u}" : "");
}
