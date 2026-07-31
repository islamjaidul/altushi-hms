using Hms.Hr.Contracts;
using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// Employee lifecycle (§5 M16 [M] "employee records"). Every method takes the caller's contexts and
/// stages rows; the caller's <see cref="IHrTx"/> commit makes them durable — the house convention, so
/// hiring an employee and its audit event land together or not at all.
/// </summary>
public sealed class EmployeeService(
    NumberSeriesService numbers, AuditWriter audit, FiscalCalendar fiscal, TimeProvider clock)
{
    /// <summary>
    /// Hires a person. A rehire passes the previous employee's <paramref name="personRef"/>: it
    /// creates a NEW employment linked to the same human, because merging two employments would
    /// merge two service histories and silently change gratuity and leave arithmetic.
    /// </summary>
    public async Task<Employee> HireAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId,
        Employee draft, long orgUnitId, long designationId, long gradeId,
        long actorId, string actorName, string? personRef = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(draft.FullName))
            throw new HrException("The employee's name is required.");
        if (draft.JoinedOn == default)
            throw new HrException("A joining date is required — service length depends on it.");

        var fy = fiscal.FiscalYearOf(draft.JoinedOn);
        var (_, code) = await numbers.IssueAsync(kernel, branchId, "employee", fy, "EMP-{n:D5}", ct);

        draft.BranchId = branchId;
        draft.EmployeeCode = code;
        draft.PersonRef = personRef;
        draft.Status = EmploymentStatus.Probation;
        draft.CreatedAt = clock.GetUtcNow();
        draft.CreatedBy = actorId;
        hr.Employees.Add(draft);
        await hr.SaveChangesAsync(ct);

        hr.Assignments.Add(new EmployeeAssignment
        {
            BranchId = branchId,
            EmployeeId = draft.Id,
            OrgUnitId = orgUnitId,
            DesignationId = designationId,
            GradeId = gradeId,
            EffectiveFrom = draft.JoinedOn,
            Reason = "joining",
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actorId,
        });

        RecordEvent(hr, branchId, draft.Id, EmploymentEventKind.Joined, draft.JoinedOn,
            null, actorId, actorName);

        audit.Append(kernel, branchId, actorId, actorName, "hr.employee.hire", "hr.employee",
            draft.Id, after: new { draft.EmployeeCode, draft.FullName, draft.JoinedOn }, tier: 1);

        return draft;
    }

    /// <summary>
    /// Moves an employee to a new unit / designation / grade from a date. Closes the open assignment
    /// the day before, so the two never overlap and "where did they sit last March" stays answerable.
    /// </summary>
    public async Task AssignAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        EmployeeAssignment next, string kind, long actorId, string actorName,
        CancellationToken ct = default)
    {
        var open = await hr.Assignments
            .Where(a => a.EmployeeId == employeeId && a.EffectiveTo == null)
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (open is not null)
        {
            if (next.EffectiveFrom <= open.EffectiveFrom)
                throw new HrException(
                    $"The new assignment must start after the current one ({open.EffectiveFrom:dd MMM yyyy}).");
            open.EffectiveTo = next.EffectiveFrom.AddDays(-1);
        }

        next.BranchId = branchId;
        next.EmployeeId = employeeId;
        next.CreatedAt = clock.GetUtcNow();
        next.CreatedBy = actorId;
        hr.Assignments.Add(next);

        RecordEvent(hr, branchId, employeeId, kind, next.EffectiveFrom, next.Reason, actorId, actorName);

        audit.Append(kernel, branchId, actorId, actorName, $"hr.employee.{kind}", "hr.employee",
            employeeId, before: open is null ? null : new { open.OrgUnitId, open.DesignationId, open.GradeId },
            after: new { next.OrgUnitId, next.DesignationId, next.GradeId, next.EffectiveFrom }, tier: 1);
    }

    /// <summary>
    /// Sets pay from a date. Salary is money, so this is tier-1 audited and the previous structure is
    /// closed rather than overwritten — last year's payslip must still resolve last year's numbers.
    /// </summary>
    public async Task SetPayAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        EmployeePayStructure next, IReadOnlyList<EmployeePayComponent> components,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (components.Count == 0)
            throw new HrException("A pay structure needs at least one component.");
        if (components.Any(c => c.AmountTaka < 0))
            throw new HrException("A pay component cannot be negative.");

        var open = await hr.PayStructures
            .Where(p => p.EmployeeId == employeeId && p.EffectiveTo == null)
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (open is not null)
        {
            if (next.EffectiveFrom <= open.EffectiveFrom)
                throw new HrException(
                    $"The new pay must start after the current structure ({open.EffectiveFrom:dd MMM yyyy}).");
            open.EffectiveTo = next.EffectiveFrom.AddDays(-1);
        }

        next.BranchId = branchId;
        next.EmployeeId = employeeId;
        next.CreatedAt = clock.GetUtcNow();
        next.CreatedBy = actorId;
        hr.PayStructures.Add(next);
        await hr.SaveChangesAsync(ct);

        foreach (var c in components)
        {
            c.PayStructureId = next.Id;
            hr.PayStructureComponents.Add(c);
        }

        audit.Append(kernel, branchId, actorId, actorName, "hr.pay.set", "hr.employee_pay_structure",
            next.Id, after: new { employeeId, next.EffectiveFrom, next.Reason, Total = components.Sum(c => c.AmountTaka) },
            tier: 1);
    }

    /// <summary>Ends an employment. Never deletes: the record and its history stay (hard rule 4).</summary>
    public async Task SeparateAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        string kind, DateOnly onDate, string? note, long actorId, string actorName,
        CancellationToken ct = default)
    {
        var emp = await hr.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
                  ?? throw new HrException("That employee no longer exists.");
        if (emp.SeparatedOn is not null)
            throw new HrException($"{emp.FullName} already left on {emp.SeparatedOn:dd MMM yyyy}.");
        if (onDate < emp.JoinedOn)
            throw new HrException("Someone cannot leave before they joined.");

        var before = new { emp.Status, emp.SeparatedOn };
        emp.SeparatedOn = onDate;
        emp.Status = kind switch
        {
            EmploymentEventKind.Terminated => EmploymentStatus.Terminated,
            EmploymentEventKind.Retired => EmploymentStatus.Retired,
            _ => EmploymentStatus.Resigned,
        };

        var openAssignment = await hr.Assignments
            .Where(a => a.EmployeeId == employeeId && a.EffectiveTo == null)
            .FirstOrDefaultAsync(ct);
        if (openAssignment is not null) openAssignment.EffectiveTo = onDate;

        RecordEvent(hr, branchId, employeeId, kind, onDate, note, actorId, actorName);

        audit.Append(kernel, branchId, actorId, actorName, "hr.employee.separate", "hr.employee",
            employeeId, before: before, after: new { emp.Status, emp.SeparatedOn, note }, tier: 1);
    }

    /// <summary>Projects employees as payees — what M15/M17 will read (PRD §10 consumers).</summary>
    public static async Task<IReadOnlyList<PayeeRecord>> PayeesAsync(
        HrDbContext hr, long branchId, CancellationToken ct = default)
        => await hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId)
            .OrderBy(e => e.EmployeeCode)
            .Select(e => new PayeeRecord(
                e.Id, e.EmployeeCode, e.FullName, e.BankName, e.BankAccountNo, e.BankRoutingNo,
                e.Tin, e.SeparatedOn == null))
            .ToListAsync(ct);

    private void RecordEvent(
        HrDbContext hr, long branchId, long employeeId, string kind, DateOnly onDate,
        string? note, long actorId, string actorName)
        => hr.EmploymentEvents.Add(new EmploymentEvent
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            Kind = kind,
            OnDate = onDate,
            Note = note,
            RecordedAt = clock.GetUtcNow(),
            RecordedBy = actorId,
            RecordedByName = actorName,
        });
}
