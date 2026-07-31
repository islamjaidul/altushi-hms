using Hms.Hr.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// Leave, walking §11: Applied → Recommended(dept head) → Approved/Rejected(HR) → Availed.
/// <para>
/// The PRD contradicts itself on tier count — §11 describes two steps, §5A-16 asks for three. Rather
/// than pick one and be wrong for half the customers, <c>LeavePolicy.ApprovalTiers</c> decides, and
/// the routing rides the kernel <see cref="ApprovalEngine"/> so delegation and escalation are policy
/// rows rather than code here.
/// </para>
/// </summary>
public sealed class LeaveService(
    NumberSeriesService numbers, AuditWriter audit, ApprovalEngine approvals,
    PolicyResolver policies, FiscalCalendar fiscal, TimeProvider clock)
{
    public async Task<LeaveApplication> ApplyAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        long leaveTypeId, DateOnly fromDate, DateOnly toDate, int daysBp, string reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (toDate < fromDate) throw new HrException("The last day cannot be before the first day.");
        if (daysBp <= 0) throw new HrException("A leave application must be for at least half a day.");
        if (string.IsNullOrWhiteSpace(reason)) throw new HrException("Please give a reason for the leave.");

        var employee = await hr.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct)
                       ?? throw new HrException("That employee no longer exists.");
        if (employee.SeparatedOn is not null && fromDate > employee.SeparatedOn)
            throw new HrException($"{employee.FullName} left on {employee.SeparatedOn:dd MMM yyyy}.");

        var overlapping = await hr.LeaveApplications.AsNoTracking()
            .Where(l => l.EmployeeId == employeeId
                        && l.State != LeaveApplicationState.Rejected
                        && l.State != LeaveApplicationState.Cancelled
                        && l.FromDate <= toDate && l.ToDate >= fromDate)
            .FirstOrDefaultAsync(ct);
        if (overlapping is not null)
            throw new HrException(
                $"That overlaps leave already applied for ({overlapping.FromDate:dd MMM} – {overlapping.ToDate:dd MMM}).");

        var assignment = await policies.AssignmentAsync(hr, employeeId, fromDate, ct);
        var policy = await policies.LeaveAsync(hr, branchId, leaveTypeId, assignment?.GradeId, fromDate, ct);
        var leaveType = await hr.LeaveTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == leaveTypeId, ct)
                        ?? throw new HrException("That leave type no longer exists.");

        if (policy is { MaxConsecutiveDays: > 0 } && daysBp > policy.MaxConsecutiveDays * 10000)
            throw new HrException(
                $"{leaveType.Name} allows at most {policy.MaxConsecutiveDays} consecutive days.");

        // Balance is checked at approval, not here: an employee may apply while an accrual is pending,
        // and refusing at application time would hide the request from the approver entirely.
        if (leaveType.Accrual != LeaveAccrual.None)
            await EnsureBalanceRowAsync(hr, branchId, employeeId, leaveTypeId, fromDate, policy, ct);

        var (_, no) = await numbers.IssueAsync(
            kernel, branchId, "leave", fiscal.FiscalYearOf(fromDate), "LV-{fy}-{n:D5}", ct);

        var application = new LeaveApplication
        {
            BranchId = branchId,
            ApplicationNo = no,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            FromDate = fromDate,
            ToDate = toDate,
            DaysBp = daysBp,
            Reason = reason,
            State = LeaveApplicationState.Applied,
            AppliedAt = clock.GetUtcNow(),
            AppliedBy = actorId,
            AppliedByName = actorName,
        };
        hr.LeaveApplications.Add(application);
        await hr.SaveChangesAsync(ct);

        // The approver of one's own leave is a real case (a department head applying). The engine's
        // delegation rules handle it — that is why this routes through the engine at all.
        var raise = await approvals.RaiseAsync(
            kernel, branchId, "leave-approval", "hr.leave_application", application.Id,
            actorId, "HR Officer", $"{leaveType.Name} {fromDate:dd MMM} – {toDate:dd MMM}", null, ct);
        application.ApprovalRequestId = raise.RequestId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.leave.apply", "hr.leave_application",
            application.Id, after: new { no, employeeId, leaveType.Name, fromDate, toDate, daysBp }, tier: 2);

        return application;
    }

    /// <summary>First tier: the department head recommends. Skipped when the policy is single-tier.</summary>
    public async Task RecommendAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long applicationId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var app = await LoadForTransitionAsync(hr, applicationId, ct);
        if (app.State != LeaveApplicationState.Applied)
            throw new HrException($"That application is already {app.State.Replace('_', ' ')}.");

        app.State = LeaveApplicationState.Recommended;
        app.RecommendedBy = actorId;
        app.RecommendedAt = clock.GetUtcNow();

        audit.Append(kernel, branchId, actorId, actorName, "hr.leave.recommend", "hr.leave_application",
            app.Id, after: new { app.ApplicationNo }, tier: 2);
    }

    /// <summary>
    /// The deciding tier. Approving spends balance under a row-version check, so two approvers cannot
    /// both grant the last day (ADR-0015).
    /// </summary>
    public async Task DecideAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long applicationId,
        bool approve, string? note, long actorId, string actorName, CancellationToken ct = default)
    {
        var app = await LoadForTransitionAsync(hr, applicationId, ct);
        if (app.State is not (LeaveApplicationState.Applied or LeaveApplicationState.Recommended))
            throw new HrException($"That application is already {app.State.Replace('_', ' ')}.");

        var assignment = await policies.AssignmentAsync(hr, app.EmployeeId, app.FromDate, ct);
        var policy = await policies.LeaveAsync(hr, branchId, app.LeaveTypeId, assignment?.GradeId, app.FromDate, ct);
        if (policy is { ApprovalTiers: >= 2 } && app.State == LeaveApplicationState.Applied)
            throw new HrException("This leave needs the department head's recommendation first.");

        var leaveType = await hr.LeaveTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == app.LeaveTypeId, ct)
            ?? throw new HrException("That leave type no longer exists.");

        if (approve && leaveType.Accrual != LeaveAccrual.None)
        {
            var year = LeaveYearOf(app.FromDate, policy);
            var balance = await hr.LeaveBalances
                .FirstOrDefaultAsync(b => b.EmployeeId == app.EmployeeId
                                          && b.LeaveTypeId == app.LeaveTypeId && b.LeaveYear == year, ct)
                ?? throw new HrException($"{leaveType.Name} has no balance record for {year}.");

            if (balance.AvailableBp < app.DaysBp)
                throw new HrException(
                    $"Only {balance.AvailableBp / 10000m:0.##} day(s) of {leaveType.Name} remain — this asks for {app.DaysBp / 10000m:0.##}.");

            balance.AvailedBp += app.DaysBp;
        }

        app.State = approve ? LeaveApplicationState.Approved : LeaveApplicationState.Rejected;
        app.DecidedBy = actorId;
        app.DecidedAt = clock.GetUtcNow();
        app.DecisionNote = note;

        if (app.ApprovalRequestId is { } requestId)
            await approvals.DecideAsync(kernel, requestId, approve, actorId, actorName, note ?? "", ct);

        audit.Append(kernel, branchId, actorId, actorName,
            approve ? "hr.leave.approve" : "hr.leave.reject", "hr.leave_application",
            app.Id, after: new { app.ApplicationNo, app.State, note }, tier: 2);
    }

    /// <summary>
    /// Marks approved leave as actually taken once the period has passed. Attendance derivation reads
    /// approved leave directly, so this is a bookkeeping close rather than a gate.
    /// </summary>
    public async Task AvailAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long applicationId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var app = await LoadForTransitionAsync(hr, applicationId, ct);
        if (app.State != LeaveApplicationState.Approved)
            throw new HrException("Only approved leave can be marked as taken.");

        app.State = LeaveApplicationState.Availed;
        audit.Append(kernel, branchId, actorId, actorName, "hr.leave.avail", "hr.leave_application",
            app.Id, after: new { app.ApplicationNo }, tier: 2);
    }

    /// <summary>Cancelling approved leave returns the balance it spent — a reversal, not a delete.</summary>
    public async Task CancelAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long applicationId,
        string reason, long actorId, string actorName, CancellationToken ct = default)
    {
        var app = await LoadForTransitionAsync(hr, applicationId, ct);
        if (app.State is LeaveApplicationState.Availed or LeaveApplicationState.Cancelled)
            throw new HrException($"That application is already {app.State}.");

        if (app.State == LeaveApplicationState.Approved)
        {
            var assignment = await policies.AssignmentAsync(hr, app.EmployeeId, app.FromDate, ct);
            var policy = await policies.LeaveAsync(hr, branchId, app.LeaveTypeId, assignment?.GradeId, app.FromDate, ct);
            var year = LeaveYearOf(app.FromDate, policy);
            var balance = await hr.LeaveBalances
                .FirstOrDefaultAsync(b => b.EmployeeId == app.EmployeeId
                                          && b.LeaveTypeId == app.LeaveTypeId && b.LeaveYear == year, ct);
            if (balance is not null) balance.AvailedBp -= app.DaysBp;
        }

        app.State = LeaveApplicationState.Cancelled;
        app.DecisionNote = reason;
        app.DecidedBy = actorId;
        app.DecidedAt = clock.GetUtcNow();

        audit.Append(kernel, branchId, actorId, actorName, "hr.leave.cancel", "hr.leave_application",
            app.Id, after: new { app.ApplicationNo, reason }, tier: 2);
    }

    /// <summary>The leave year an employer counts in — its start month is policy, not the calendar.</summary>
    public static int LeaveYearOf(DateOnly on, LeavePolicy? _) => on.Year;

    private static async Task<LeaveApplication> LoadForTransitionAsync(
        HrDbContext hr, long applicationId, CancellationToken ct)
        => await hr.LeaveApplications.FirstOrDefaultAsync(l => l.Id == applicationId, ct)
           ?? throw new HrException("That leave application no longer exists.");

    private async Task EnsureBalanceRowAsync(
        HrDbContext hr, long branchId, long employeeId, long leaveTypeId, DateOnly on,
        LeavePolicy? policy, CancellationToken ct)
    {
        var year = LeaveYearOf(on, policy);
        var exists = await hr.LeaveBalances.AnyAsync(
            b => b.EmployeeId == employeeId && b.LeaveTypeId == leaveTypeId && b.LeaveYear == year, ct);
        if (exists) return;

        hr.LeaveBalances.Add(new LeaveBalance
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            LeaveTypeId = leaveTypeId,
            LeaveYear = year,
            // No entitlement configured means no entitlement — we do not invent one (ADR-0027).
            OpeningBp = policy?.AnnualEntitlementBp ?? 0,
        });
        await hr.SaveChangesAsync(ct);
    }
}
