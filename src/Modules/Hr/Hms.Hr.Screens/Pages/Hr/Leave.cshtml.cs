using Hms.Hr.Data;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record LeaveRow(
    long Id, string ApplicationNo, string EmployeeName, string EmployeeCode, string LeaveType,
    DateOnly From, DateOnly To, int DaysBp, string Reason, string State, string? Unit);

/// <summary>
/// The leave inbox, walking §11: Applied → Recommended → Approved/Rejected → Availed.
/// <para>
/// Guarded by <c>hr.read</c> so a department head can see their team's requests, with the two
/// decisions gated separately inside: recommending needs <c>hr.leave.recommend</c>, deciding needs
/// <c>hr.leave.approve</c>. That split is the §12 matrix — a dept head recommends, HR decides — and
/// it is enforced at the handler, not by hiding the button.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.Read)]
public class LeaveModel(IHrTx tx, LeaveService leave) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public bool ShowDecided { get; set; }

    public IReadOnlyList<LeaveRow> Rows { get; private set; } = [];
    public int PendingCount { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostRecommendAsync(long id)
    {
        if (!Can("hr.leave.recommend"))
            return Forbid();

        return await ActAsync(s => leave.RecommendAsync(
            s.Hr, s.Kernel, BranchId, id, ActorId, ActorName), "Recommended to HR");
    }

    public async Task<IActionResult> OnPostDecideAsync(long id, bool approve, string? note)
    {
        if (!Can("hr.leave.approve"))
            return Forbid();

        return await ActAsync(
            s => leave.DecideAsync(s.Hr, s.Kernel, BranchId, id, approve, note, ActorId, ActorName),
            approve ? "Leave approved — the balance is spent" : "Leave rejected");
    }

    private async Task<IActionResult> ActAsync(Func<HrScope, Task> body, string toast)
    {
        try
        {
            await tx.RunAsync(body);
            Toast(toast, "event_available");
            return Redirect($"/hr/leave?ShowDecided={ShowDecided}");
        }
        catch (HrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        var pending = new[] { LeaveApplicationState.Applied, LeaveApplicationState.Recommended };

        PendingCount = await s.Hr.LeaveApplications.AsNoTracking()
            .CountAsync(l => l.BranchId == BranchId && pending.Contains(l.State));

        var query = s.Hr.LeaveApplications.AsNoTracking().Where(l => l.BranchId == BranchId);
        if (!ShowDecided) query = query.Where(l => pending.Contains(l.State));

        var apps = await query.OrderByDescending(l => l.AppliedAt).Take(200).ToListAsync();
        var ids = apps.Select(a => a.EmployeeId).ToList();

        var employees = await s.Hr.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeCode, e.FullName });
        var types = await s.Hr.LeaveTypes.AsNoTracking()
            .Where(t => t.BranchId == BranchId).ToDictionaryAsync(t => t.Id, t => t.Name);
        var assignments = await s.Hr.Assignments.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId) && a.EffectiveTo == null).ToListAsync();
        var units = await s.Hr.OrgUnits.AsNoTracking()
            .Where(u => u.BranchId == BranchId).ToDictionaryAsync(u => u.Id, u => u.Name);

        Rows = apps.Select(a =>
        {
            var assignment = assignments.FirstOrDefault(x => x.EmployeeId == a.EmployeeId);
            return new LeaveRow(
                a.Id, a.ApplicationNo,
                employees.TryGetValue(a.EmployeeId, out var e) ? e.FullName : "(removed)",
                employees.TryGetValue(a.EmployeeId, out var c) ? c.EmployeeCode : "?",
                types.GetValueOrDefault(a.LeaveTypeId, "—"),
                a.FromDate, a.ToDate, a.DaysBp, a.Reason, a.State,
                assignment is null ? null : units.GetValueOrDefault(assignment.OrgUnitId));
        }).ToList();
    });
}
