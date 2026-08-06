using System.Text.Json;
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
    NumberSeriesService numbers, AuditWriter audit, TimeProvider clock)
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

        // spec 0056: the engagement is decided at hire, not inferred later. Validated on the draft
        // rather than through new parameters, so the twenty existing call sites keep compiling and
        // the ones that set nothing get the permanent default the column already documents.
        if (!EmploymentType.All.Contains(draft.EmploymentType))
            throw new HrException("Choose the kind of employment — permanent, contract, intern and so on.");
        if (EmploymentType.NeedsEndDate(draft.EmploymentType) && draft.ContractEndsOn is null)
            throw new HrException("A contract engagement needs the date the contract ends.");
        if (draft.ContractEndsOn is { } ends && ends <= draft.JoinedOn)
            throw new HrException("A contract cannot end on or before the joining date.");
        if (draft.ProbationDueOn is { } due && due <= draft.JoinedOn)
            throw new HrException("Probation cannot fall due on or before the joining date.");
        if (draft.ProbationDueOn is not null && draft.ConfirmedOn is not null)
            throw new HrException("An employment is either on probation or confirmed, not both.");

        // One series for the life of the organisation, deliberately NOT scoped by fiscal year.
        //
        // The number series is keyed (branch, doc_type, fiscal_year), so a fiscal-year scope restarts
        // the counter every year. With a format carrying no {fy} token, the first hire of each new
        // fiscal year is issued EMP-00001 again — and the unique index on employee_code rejects it.
        // Seeding a hundred people with joining dates spread over seven years is what surfaced it
        // (spec 0036); on a live install it would have appeared as a 500 on the first hire after a
        // fiscal-year rollover, on a screen that had worked all year.
        //
        // An employee code is a person's permanent identifier, not a document number within a
        // period, so the fix is the scope rather than the format: this row's history must not be
        // partitioned by year. "all" is a literal scope key, not a year.
        var (_, code) = await numbers.IssueAsync(kernel, branchId, "employee", "all", "EMP-{n:D5}", ct);

        draft.BranchId = branchId;
        draft.EmployeeCode = code;
        draft.PersonRef = personRef;
        // An operator who states a confirmation date at hire is saying "no probation"; anyone else
        // starts on probation, as the module has always done.
        draft.Status = draft.ConfirmedOn is null ? EmploymentStatus.Probation : EmploymentStatus.Confirmed;
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

    // ---- probation (spec 0056, G13) ---------------------------------------------------------------
    //
    // ConfirmedOn, EmploymentStatus.Confirmed and EmploymentEventKind.Confirmed all shipped with the
    // module. Until now the only code in the product that wrote any of them was the demo seed, so an
    // employee hired through the UI stayed on probation for the life of the install.

    /// <summary>
    /// Ends probation. One transaction moves the status, stamps the date, records the event and
    /// audits it — a confirmation that half-applied would leave a person the payroll treats as
    /// confirmed and the register still lists as due.
    /// </summary>
    public async Task ConfirmProbationAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        DateOnly onDate, string? note, long actorId, string actorName, CancellationToken ct = default)
    {
        var emp = await hr.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
                  ?? throw new HrException("That employee no longer exists.");
        if (emp.SeparatedOn is not null)
            throw new HrException($"{emp.FullName} left on {emp.SeparatedOn:dd MMM yyyy} — there is nothing to confirm.");
        if (emp.ConfirmedOn is not null)
            throw new HrException($"{emp.FullName} was already confirmed on {emp.ConfirmedOn:dd MMM yyyy}.");
        if (onDate < emp.JoinedOn)
            throw new HrException("Someone cannot be confirmed before they joined.");

        var before = new { emp.Status, emp.ConfirmedOn, emp.ProbationDueOn };
        emp.ConfirmedOn = onDate;
        emp.Status = EmploymentStatus.Confirmed;
        emp.ProbationDueOn = null;          // nothing is due once it is decided

        RecordEvent(hr, branchId, employeeId, EmploymentEventKind.Confirmed, onDate, note,
            actorId, actorName);

        audit.Append(kernel, branchId, actorId, actorName, "hr.employee.confirm", "hr.employee",
            employeeId, before: before, after: new { emp.Status, emp.ConfirmedOn, note }, tier: 1);
    }

    /// <summary>
    /// Pushes probation to a new due date. Both dates and the reason go on the event, because "how
    /// many times has this person been extended, and why" is the question an extension exists to
    /// answer.
    /// </summary>
    public async Task ExtendProbationAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        DateOnly newDueOn, string reason, long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new HrException("An extension needs a reason — it is the record the employee may ask to see.");

        var emp = await hr.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
                  ?? throw new HrException("That employee no longer exists.");
        if (emp.ConfirmedOn is not null)
            throw new HrException($"{emp.FullName} was confirmed on {emp.ConfirmedOn:dd MMM yyyy} — probation is over.");
        if (emp.SeparatedOn is not null)
            throw new HrException($"{emp.FullName} left on {emp.SeparatedOn:dd MMM yyyy}.");

        var previous = emp.ProbationDueOn;
        if (previous is not null && newDueOn <= previous)
            throw new HrException(
                $"An extension must move the date forward — it is already due {previous:dd MMM yyyy}.");
        if (newDueOn <= emp.JoinedOn)
            throw new HrException("Probation cannot fall due before the joining date.");

        emp.ProbationDueOn = newDueOn;

        var detail = JsonSerializer.Serialize(new { from = previous, to = newDueOn });
        hr.EmploymentEvents.Add(new EmploymentEvent
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            Kind = EmploymentEventKind.ProbationExtended,
            OnDate = Dhaka.Today(clock),
            Note = reason,
            DetailJson = detail,
            RecordedAt = clock.GetUtcNow(),
            RecordedBy = actorId,
            RecordedByName = actorName,
        });

        audit.Append(kernel, branchId, actorId, actorName, "hr.employee.probation_extend",
            "hr.employee", employeeId, before: new { ProbationDueOn = previous },
            after: new { ProbationDueOn = newDueOn, reason }, tier: 1);
    }

    /// <summary>
    /// Changes the kind of engagement — a contract made permanent, an intern taken on. Dated and
    /// attributed, because eligibility for leave, gratuity and settlement all follow from it.
    /// </summary>
    public async Task ChangeEmploymentTypeAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        string type, DateOnly? contractEndsOn, DateOnly onDate, string? note,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (!EmploymentType.All.Contains(type))
            throw new HrException("That is not an employment type this product knows.");
        if (EmploymentType.NeedsEndDate(type) && contractEndsOn is null)
            throw new HrException("A contract engagement needs the date the contract ends.");

        var emp = await hr.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
                  ?? throw new HrException("That employee no longer exists.");
        if (contractEndsOn is { } ends && ends <= emp.JoinedOn)
            throw new HrException("A contract cannot end on or before the joining date.");

        var before = new { emp.EmploymentType, emp.ContractEndsOn };
        emp.EmploymentType = type;
        emp.ContractEndsOn = contractEndsOn;

        RecordEvent(hr, branchId, employeeId, EmploymentEventKind.TypeChanged, onDate,
            note ?? EmploymentType.Label(type), actorId, actorName);

        audit.Append(kernel, branchId, actorId, actorName, "hr.employee.type_change", "hr.employee",
            employeeId, before: before, after: new { emp.EmploymentType, emp.ContractEndsOn }, tier: 1);
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
        emp.Status = TerminalStatusFor(kind);

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

    /// <summary>
    /// The status an employment lands on, given the event that ended it. Contract-end and death used
    /// to fall through to "resigned", which is not what either of them is (spec 0056).
    /// </summary>
    public static string TerminalStatusFor(string kind) => kind switch
    {
        EmploymentEventKind.Terminated => EmploymentStatus.Terminated,
        EmploymentEventKind.Retired => EmploymentStatus.Retired,
        EmploymentEventKind.ContractEnded => EmploymentStatus.ContractEnded,
        EmploymentEventKind.Deceased => EmploymentStatus.Deceased,
        _ => EmploymentStatus.Resigned,
    };

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
