using Hms.Hr.Data;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record HeadcountRow(string OrgUnit, int Headcount);

[Authorize(Policy = HrPerm.Read)]
public class IndexModel(IHrTx tx) : HmsPageModel
{
    public int Headcount { get; private set; }
    public int OnLeaveToday { get; private set; }
    public int AbsentToday { get; private set; }
    public int UnresolvedExceptions { get; private set; }
    public int PendingLeave { get; private set; }
    public IReadOnlyList<HeadcountRow> ByUnit { get; private set; } = [];
    public string? OpenPayrollPeriod { get; private set; }
    public string? OpenPayrollState { get; private set; }

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6));
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        await tx.RunAsync(async s =>
        {
            Headcount = await s.Hr.Employees.AsNoTracking()
                .CountAsync(e => e.BranchId == BranchId && e.SeparatedOn == null);

            var days = await s.Hr.AttendanceDays.AsNoTracking()
                .Where(d => d.BranchId == BranchId && d.OnDate == today)
                .GroupBy(d => d.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            OnLeaveToday = days.FirstOrDefault(d => d.Status == AttendanceStatus.OnLeave)?.Count ?? 0;
            AbsentToday = days.FirstOrDefault(d => d.Status == AttendanceStatus.Absent)?.Count ?? 0;

            UnresolvedExceptions = await s.Hr.AttendanceDays.AsNoTracking()
                .CountAsync(d => d.BranchId == BranchId && d.OnDate >= monthStart && d.OnDate <= today
                                 && d.Status == AttendanceStatus.Incomplete);

            PendingLeave = await s.Hr.LeaveApplications.AsNoTracking()
                .CountAsync(l => l.BranchId == BranchId
                                 && (l.State == LeaveApplicationState.Applied
                                     || l.State == LeaveApplicationState.Recommended));

            var units = await s.Hr.OrgUnits.AsNoTracking()
                .Where(u => u.BranchId == BranchId && u.Active)
                .ToDictionaryAsync(u => u.Id, u => u.Name);

            var assignments = await s.Hr.Assignments.AsNoTracking()
                .Where(a => a.BranchId == BranchId && a.EffectiveTo == null)
                .GroupBy(a => a.OrgUnitId)
                .Select(g => new { OrgUnitId = g.Key, Count = g.Count() })
                .ToListAsync();

            ByUnit = assignments
                .Select(a => new HeadcountRow(
                    units.TryGetValue(a.OrgUnitId, out var name) ? name : "Unassigned", a.Count))
                .OrderByDescending(r => r.Headcount)
                .ToList();

            var run = await s.Hr.PayrollRuns.AsNoTracking()
                .Where(r => r.BranchId == BranchId && r.State != PayrollRunState.Posted
                            && r.State != PayrollRunState.Cancelled)
                .OrderByDescending(r => r.Period)
                .FirstOrDefaultAsync();
            OpenPayrollPeriod = run?.Period.ToString("MMMM yyyy");
            OpenPayrollState = run?.State.Replace('_', ' ');
        });
    }
}
