using Hms.Hr.Data;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record AssignmentView(
    string Unit, string Designation, string Grade, DateOnly From, DateOnly? To, string? Reason);

public sealed record PayLineView(string Component, string Kind, string Basis, long AmountTaka);

public sealed record BalanceView(string LeaveType, int EntitledBp, int AvailedBp, int AvailableBp);

public sealed record LeaveView(
    string ApplicationNo, string LeaveType, DateOnly From, DateOnly To, int DaysBp, string State);

public sealed record AttendanceView(
    DateOnly OnDate, string Status, DateTimeOffset? In, DateTimeOffset? Out,
    int WorkedMinutes, int LateMinutes);

public sealed record EventView(string Kind, DateOnly OnDate, string? Note, string By);

/// <summary>
/// One employee's record — the page every name on <c>/hr/employees</c> already linked to, and which
/// did not exist. The most obvious click on the busiest HR screen returned 404.
/// <para>
/// Pay is behind <c>hr.salary.read</c>, separately from <c>hr.read</c>, so a department head can open
/// a team member's record to check a joining date without seeing what they earn — the confidentiality
/// split spec 0035 established, enforced here rather than assumed.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.Read)]
public class EmployeeModel(IHrTx tx) : HmsPageModel
{
    public Employee? Person { get; private set; }
    public AssignmentView? Current { get; private set; }
    public IReadOnlyList<AssignmentView> History { get; private set; } = [];
    public IReadOnlyList<PayLineView> PayLines { get; private set; } = [];
    public long GrossTaka { get; private set; }
    public DateOnly? PayEffectiveFrom { get; private set; }
    public IReadOnlyList<BalanceView> Balances { get; private set; } = [];
    public IReadOnlyList<LeaveView> Leave { get; private set; } = [];
    public IReadOnlyList<AttendanceView> Attendance { get; private set; } = [];
    public IReadOnlyList<EventView> Events { get; private set; } = [];

    public bool CanSeePay => Can(HrPerm.Claim.SalaryRead);

    public async Task<IActionResult> OnGetAsync(long id)
    {
        await tx.RunAsync(async s =>
        {
            Person = await s.Hr.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id && e.BranchId == BranchId);
            if (Person is null) return;

            var units = await s.Hr.OrgUnits.AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => x.Name);
            var designations = await s.Hr.Designations.AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => x.Name);
            var grades = await s.Hr.Grades.AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => x.Name);

            var assignments = await s.Hr.Assignments.AsNoTracking()
                .Where(a => a.EmployeeId == id)
                .OrderByDescending(a => a.EffectiveFrom)
                .ToListAsync();

            History = assignments.Select(a => new AssignmentView(
                units.GetValueOrDefault(a.OrgUnitId, "—"),
                designations.GetValueOrDefault(a.DesignationId, "—"),
                grades.GetValueOrDefault(a.GradeId, "—"),
                a.EffectiveFrom, a.EffectiveTo, a.Reason)).ToList();
            Current = History.FirstOrDefault(a => a.To is null) ?? History.FirstOrDefault();

            var leaveTypes = await s.Hr.LeaveTypes.AsNoTracking()
                .ToDictionaryAsync(x => x.Id, x => x.Name);

            Balances = await s.Hr.LeaveBalances.AsNoTracking()
                .Where(b => b.EmployeeId == id)
                .OrderBy(b => b.LeaveTypeId)
                .Select(b => new BalanceView(
                    leaveTypes.GetValueOrDefault(b.LeaveTypeId, "—"),
                    b.OpeningBp + b.AccruedBp + b.AdjustmentBp,
                    b.AvailedBp,
                    b.OpeningBp + b.AccruedBp + b.AdjustmentBp - b.AvailedBp - b.EncashedBp))
                .ToListAsync();

            Leave = await s.Hr.LeaveApplications.AsNoTracking()
                .Where(l => l.EmployeeId == id)
                .OrderByDescending(l => l.FromDate).Take(10)
                .Select(l => new LeaveView(
                    l.ApplicationNo, leaveTypes.GetValueOrDefault(l.LeaveTypeId, "—"),
                    l.FromDate, l.ToDate, l.DaysBp, l.State))
                .ToListAsync();

            Attendance = await s.Hr.AttendanceDays.AsNoTracking()
                .Where(d => d.EmployeeId == id)
                .OrderByDescending(d => d.OnDate).Take(30)
                .Select(d => new AttendanceView(
                    d.OnDate, d.Status, d.FirstIn, d.LastOut, d.WorkedMinutes, d.LateMinutes))
                .ToListAsync();

            Events = await s.Hr.EmploymentEvents.AsNoTracking()
                .Where(e => e.EmployeeId == id)
                .OrderByDescending(e => e.OnDate).ThenByDescending(e => e.Id)
                .Select(e => new EventView(e.Kind, e.OnDate, e.Note, e.RecordedByName))
                .ToListAsync();

            // The pay structure is loaded only when the viewer may see it. Fetching it and hiding it
            // in the view would put salary figures in a response that a permission check said no to.
            if (!CanSeePay) return;

            var structure = await s.Hr.PayStructures.AsNoTracking()
                .Where(p => p.EmployeeId == id && p.EffectiveTo == null)
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync();
            if (structure is null) return;

            PayEffectiveFrom = structure.EffectiveFrom;
            var components = await s.Hr.PayComponents.AsNoTracking()
                .Where(c => c.BranchId == BranchId).ToListAsync();

            var lines = await s.Hr.PayStructureComponents.AsNoTracking()
                .Where(l => l.PayStructureId == structure.Id).ToListAsync();

            PayLines = [.. lines
                .Select(l => (Line: l, Component: components.FirstOrDefault(c => c.Id == l.ComponentId)))
                .Where(x => x.Component is not null)
                .OrderBy(x => x.Component!.DisplayOrder)
                .Select(x => new PayLineView(
                    x.Component!.Name,
                    x.Component.Kind,
                    x.Component.CalcMethod == PayComponentCalc.PercentOf
                        ? $"{(x.Line.PercentBpOverride ?? x.Component.PercentBp) / 100}% of basic"
                        : "Fixed",
                    x.Line.AmountTaka))];

            GrossTaka = PayLines.Where(l => l.Kind == PayComponentKind.Earning).Sum(l => l.AmountTaka);
        });

        return Person is null ? NotFound() : Page();
    }
}
