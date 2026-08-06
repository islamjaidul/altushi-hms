using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record TeamMemberRow(
    long EmployeeId, string Code, string Name, string Unit, string Designation,
    string Status, string StatusPill, string Detail);

public sealed record TeamApprovalRow(
    long ApplicationId, string ApplicationNo, string EmployeeName, string LeaveType,
    DateOnly From, DateOnly To, int DaysBp, string State, int WaitingDays, int ClashCount);

/// <summary>
/// Manager self-service (module PRD §7.11, §14.2). Deliberately small: a department head uses this
/// weekly, not daily.
/// <para>
/// <b>No salary appears here at all</b> (D6, §7.11) — not a figure, not a tooltip, not an export.
/// The screen reads no pay table, so there is nothing to leak rather than a rule to remember.
/// </para>
/// <para>
/// <b>Scope is structural</b> (§11 scoping rule 1). The team is derived from the signed-in user's own
/// employment — who reports to them, and which units they head. There is no unit id in the URL, so
/// there is no parameter to change in order to read another manager's team.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.TeamView)]
public class TeamModel(IHrTx tx, IPeriodCalendarSource calendars, TimeProvider clock) : HmsPageModel
{
    public Employee? Me { get; private set; }

    public Period Period { get; private set; } = null!;

    public PeriodBar Bar { get; private set; } = null!;

    public DateOnly Today { get; private set; }

    public IReadOnlyList<TeamMemberRow> Today_Team { get; private set; } = [];

    public IReadOnlyList<TeamApprovalRow> Approvals { get; private set; } = [];

    public int TeamSize { get; private set; }

    public int PresentCount { get; private set; }

    public int AbsentCount { get; private set; }

    public int LeaveCount { get; private set; }

    public int LateCount { get; private set; }

    public int ExceptionCount { get; private set; }

    public string ExceptionHref { get; private set; } = "";

    public async Task OnGetAsync(CancellationToken ct)
    {
        Today = Dhaka.Today(clock);
        var calendar = await calendars.ForAsync(BranchId, Today, ct);

        Period = PeriodBinding.Resolve(Request, Today, calendar, PeriodGrain.Month);
        Bar = PeriodBar.For(Period, Request, calendar, Today);
        PeriodBinding.Remember(Response, Period);

        var pairs = string.Join('&', PeriodBinding.PeriodPairs(Period)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        ExceptionHref = $"/hr/reports/attendance-exceptions?{pairs}";

        await tx.RunAsync(async s => await LoadAsync(s, ct), ct);
    }

    private async Task LoadAsync(HrScope s, CancellationToken ct)
    {
        var userRef = ActorId.ToString();
        Me = await s.Hr.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.BranchId == BranchId && e.UserRef == userRef, ct);

        if (Me is null)
        {
            Fail("Your login is not linked to an employee record yet, so there is no team to show. "
                 + "Ask HR to link it on your employee record.");
            return;
        }

        // Two ways to be someone's manager: named as their reporting manager, or head of their unit.
        // Both are read from the assignment in force today, so a transfer moves someone's manager
        // without anybody editing a list.
        var headedUnits = await s.Hr.OrgUnits.AsNoTracking()
            .Where(u => u.BranchId == BranchId && u.HeadEmployeeId == Me.Id)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var assignments = await s.Hr.Assignments.AsNoTracking()
            .Where(a => a.BranchId == BranchId
                        && a.EffectiveFrom <= Today
                        && (a.EffectiveTo == null || a.EffectiveTo >= Today)
                        && (a.ReportsToEmployeeId == Me.Id || headedUnits.Contains(a.OrgUnitId)))
            .Select(a => new { a.EmployeeId, a.OrgUnitId, a.DesignationId })
            .ToListAsync(ct);

        var memberIds = assignments.Select(a => a.EmployeeId).Distinct().ToList();
        TeamSize = memberIds.Count;
        if (memberIds.Count == 0) return;

        var lookups = await HrLookupsFor(s, ct);

        var members = await s.Hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == BranchId && memberIds.Contains(e.Id) && e.SeparatedOn == null)
            .OrderBy(e => e.FullName)
            .ToListAsync(ct);

        var days = await s.Hr.AttendanceDays.AsNoTracking()
            .Where(d => d.BranchId == BranchId && d.OnDate == Today && memberIds.Contains(d.EmployeeId))
            .ToDictionaryAsync(d => d.EmployeeId, ct);

        var byEmployee = assignments.GroupBy(a => a.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        Today_Team = members.Select(e =>
        {
            byEmployee.TryGetValue(e.Id, out var a);
            days.TryGetValue(e.Id, out var day);

            var (word, pill, detail) = day is null
                ? ("Not derived", "", "No attendance derived for today yet")
                : (Reports.Time.StatusWord(day.Status), Reports.Time.Pill(day.Status), Detail(day));

            if (day is not null)
            {
                if (day.Status == AttendanceStatus.Present) PresentCount++;
                else if (day.Status == AttendanceStatus.Absent) AbsentCount++;
                else if (day.Status == AttendanceStatus.OnLeave) LeaveCount++;
                if (day.LateMinutes > 0) LateCount++;
            }

            return new TeamMemberRow(e.Id, e.EmployeeCode, e.FullName,
                lookups.Unit(a?.OrgUnitId), lookups.Designation(a?.DesignationId), word, pill, detail);
        }).ToList();

        ExceptionCount = await s.Hr.AttendanceDays.AsNoTracking()
            .CountAsync(d => d.BranchId == BranchId && memberIds.Contains(d.EmployeeId)
                             && d.OnDate >= Period.Range.From && d.OnDate <= Period.Range.To
                             && (d.Status == AttendanceStatus.Incomplete
                                 || d.Status == AttendanceStatus.Absent), ct);

        await LoadApprovalsAsync(s, memberIds, lookups, ct);
    }

    private async Task LoadApprovalsAsync(HrScope s, List<long> memberIds, Lookups lookups,
        CancellationToken ct)
    {
        // Not period-bounded on purpose: an application pending since last month is precisely what a
        // manager needs to see when they open this screen.
        var pending = await s.Hr.LeaveApplications.AsNoTracking()
            .Where(a => a.BranchId == BranchId && memberIds.Contains(a.EmployeeId)
                        && (a.State == LeaveApplicationState.Applied
                            || a.State == LeaveApplicationState.Recommended))
            .OrderBy(a => a.AppliedAt)
            .Take(50)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        var names = await s.Hr.Employees.AsNoTracking()
            .Where(e => memberIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        // US16.12: the approver sees who else is off before they approve. Approved leave for the
        // rest of the team, overlapping the requested dates.
        var approved = await s.Hr.LeaveApplications.AsNoTracking()
            .Where(a => a.BranchId == BranchId && memberIds.Contains(a.EmployeeId)
                        && (a.State == LeaveApplicationState.Approved
                            || a.State == LeaveApplicationState.Availed))
            .Select(a => new { a.EmployeeId, a.FromDate, a.ToDate })
            .ToListAsync(ct);

        Approvals = pending.Select(a => new TeamApprovalRow(
            a.Id,
            a.ApplicationNo,
            names.GetValueOrDefault(a.EmployeeId, "—"),
            lookups.LeaveType(a.LeaveTypeId),
            a.FromDate,
            a.ToDate,
            a.DaysBp,
            a.State,
            Today.DayNumber - Dhaka.DateOf(a.AppliedAt).DayNumber,
            approved.Count(o => o.EmployeeId != a.EmployeeId
                                && o.FromDate <= a.ToDate && o.ToDate >= a.FromDate))).ToList();
    }

    private static string Detail(AttendanceDay day)
    {
        var parts = new List<string>();
        if (day.FirstIn is { } i) parts.Add($"in {Ui.Time(i)}");
        if (day.LastOut is { } o) parts.Add($"out {Ui.Time(o)}");
        if (day.LateMinutes > 0) parts.Add($"{day.LateMinutes} min late");
        if (day.OvertimeMinutes > 0) parts.Add($"{day.OvertimeMinutes} min OT");
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }

    /// <summary>Local master-name cache — the manager screen needs three of the four HrLookups holds.</summary>
    private sealed record Lookups(
        IReadOnlyDictionary<long, string> Units,
        IReadOnlyDictionary<long, string> Designations,
        IReadOnlyDictionary<long, string> LeaveTypes)
    {
        public string Unit(long? id) => id is { } k && Units.TryGetValue(k, out var n) ? n : "—";

        public string Designation(long? id)
            => id is { } k && Designations.TryGetValue(k, out var n) ? n : "—";

        public string LeaveType(long? id)
            => id is { } k && LeaveTypes.TryGetValue(k, out var n) ? n : "—";
    }

    private static async Task<Lookups> HrLookupsFor(HrScope s, CancellationToken ct) => new(
        await s.Hr.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct),
        await s.Hr.Designations.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct),
        await s.Hr.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct));
}
