using Hms.Hr.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// Separation, clearance and final settlement (module PRD §7.1, §9, G18 / G19).
/// <para>
/// Before this, ending an employment was <c>EmployeeService.SeparateAsync</c> — a date, a status and
/// an event — and that method had <b>no caller anywhere in the product</b>. §9 describes seven
/// states; the module implemented part of the first.
/// </para>
/// <para>
/// Every transition loads the row <b>under a lock</b>, the shape spec 0052 established for payroll
/// runs: two officers clicking Approve at once means one succeeds and the other is told the state
/// moved, rather than both proceeding from a stale read. The states are §9's, in order, and skipping
/// one is refused by name.
/// </para>
/// </summary>
public sealed class SeparationService(
    EmployeeService employees, PolicyResolver policies, ApprovalEngine approvals,
    AuditWriter audit, TimeProvider clock)
{
    /// <summary>
    /// Serves notice. Creates the separation, seeds §7.1's clearance checklist, and moves the
    /// employment to <c>on_notice</c> — a status the module has always had and never set.
    /// </summary>
    public async Task<Separation> InitiateAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        string kind, DateOnly noticeOn, DateOnly intendedLastWorkingDay, string? reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (!SeparationKind.All.Contains(kind))
            throw new HrException("Choose how the employment is ending.");

        var emp = await hr.Employees.FirstOrDefaultAsync(e => e.Id == employeeId, ct)
                  ?? throw new HrException("That employee no longer exists.");
        if (emp.SeparatedOn is not null)
            throw new HrException($"{emp.FullName} already left on {emp.SeparatedOn:dd MMM yyyy}.");
        if (intendedLastWorkingDay < emp.JoinedOn)
            throw new HrException("Someone cannot leave before they joined.");
        if (intendedLastWorkingDay < noticeOn)
            throw new HrException("The last working day cannot fall before notice was served.");

        var live = await hr.Separations
            .Where(s => s.EmployeeId == employeeId && s.State != SeparationState.Cancelled)
            .FirstOrDefaultAsync(ct);
        if (live is not null)
            throw new HrException(
                $"{emp.FullName} already has a separation in progress ({SeparationState.Label(live.State)}).");

        var policy = await EmploymentPolicyAsync(hr, branchId, noticeOn, ct);

        var separation = new Separation
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            Kind = kind,
            State = SeparationState.Initiated,
            NoticeOn = noticeOn,
            // Snapshotted, not resolved later: the notice the employment required is the one in
            // force when notice was served, exactly as a payroll line pins its policy stamp.
            NoticeDays = policy?.NoticePeriodDays ?? 0,
            IntendedLastWorkingDay = intendedLastWorkingDay,
            LastWorkingDay = intendedLastWorkingDay,
            Reason = reason,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actorId,
            CreatedByName = actorName,
        };
        hr.Separations.Add(separation);
        await hr.SaveChangesAsync(ct);

        var unit = await hr.Assignments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.EffectiveTo == null)
            .Select(a => (long?)a.OrgUnitId)
            .FirstOrDefaultAsync(ct);

        foreach (var department in ClearanceDepartment.All)
        {
            hr.ClearanceItems.Add(new ClearanceItem
            {
                BranchId = branchId,
                SeparationId = separation.Id,
                Department = department,
                // Only the department-head item has an owner in the org tree; the rest are functions.
                OrgUnitId = department == ClearanceDepartment.DepartmentHead ? unit : null,
                Status = ClearanceStatus.Pending,
            });
        }

        emp.Status = EmploymentStatus.OnNotice;

        hr.EmploymentEvents.Add(new EmploymentEvent
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            Kind = EmploymentEventKind.NoticeServed,
            OnDate = noticeOn,
            Note = reason,
            RecordedAt = clock.GetUtcNow(),
            RecordedBy = actorId,
            RecordedByName = actorName,
        });

        audit.Append(kernel, branchId, actorId, actorName, "hr.separation.initiate", "hr.separation",
            separation.Id, after: new { employeeId, kind, noticeOn, intendedLastWorkingDay, reason },
            tier: 1);

        return separation;
    }

    /// <summary>Records one department's sign-off, with what it wants recovered.</summary>
    public async Task SignOffClearanceAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long itemId,
        string status, string? note, long duesTaka, long actorId, string actorName,
        CancellationToken ct = default)
    {
        if (status is not (ClearanceStatus.Cleared or ClearanceStatus.ClearedWithDues
            or ClearanceStatus.Blocked or ClearanceStatus.Pending))
            throw new HrException("That is not a clearance outcome.");
        if (duesTaka < 0)
            throw new HrException("Dues cannot be negative — a refund is not a clearance.");
        if (status == ClearanceStatus.ClearedWithDues && duesTaka <= 0)
            throw new HrException("Cleared with dues needs the amount to recover.");
        if (status == ClearanceStatus.Blocked && string.IsNullOrWhiteSpace(note))
            throw new HrException("Say what is blocking it — the note is what the employee is told.");

        var item = await hr.ClearanceItems.FirstOrDefaultAsync(i => i.Id == itemId, ct)
                   ?? throw new HrException("That clearance item no longer exists.");

        // Locked too: a sign-off can move the separation to in_clearance, and two departments
        // signing at the same moment must not both write that transition.
        var separation = await LoadAsync(hr, item.SeparationId, ct);
        if (separation.State is SeparationState.Approved or SeparationState.Paid
            or SeparationState.DocumentsIssued)
            throw new HrException(
                "The settlement is already approved — clearance can no longer change what it was computed from.");

        var before = new { item.Status, item.DuesTaka, item.Note };
        item.Status = status;
        item.Note = note;
        item.DuesTaka = status == ClearanceStatus.ClearedWithDues ? duesTaka : 0;
        item.SignedOffBy = actorId;
        item.SignedOffByName = actorName;
        item.SignedOffAt = clock.GetUtcNow();

        // The first sign-off is what moves a separation out of "initiated": clearance has begun.
        if (separation.State == SeparationState.Initiated)
            separation.State = SeparationState.InClearance;

        audit.Append(kernel, branchId, actorId, actorName, "hr.clearance.signoff", "hr.clearance_item",
            item.Id, before: before, after: new { item.Status, item.DuesTaka, item.Note }, tier: 1);
    }

    /// <summary>
    /// Computes the settlement. Refuses while any department is outstanding — a statement computed
    /// over an incomplete clearance is a number that will change, and this one gets approved and
    /// paid.
    /// </summary>
    public async Task<SettlementDraft> ComputeAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long separationId,
        DateOnly lastWorkingDay, long actorId, string actorName, CancellationToken ct = default)
    {
        var separation = await LoadAsync(hr, separationId, ct);
        Require(separation, SeparationState.Initiated, SeparationState.InClearance,
            SeparationState.Computed);

        var employee = await hr.Employees.FirstOrDefaultAsync(e => e.Id == separation.EmployeeId, ct)
                       ?? throw new HrException("That employee no longer exists.");
        if (lastWorkingDay < employee.JoinedOn)
            throw new HrException("The last working day cannot fall before the joining date.");

        var items = await hr.ClearanceItems.AsNoTracking()
            .Where(i => i.SeparationId == separationId)
            .ToListAsync(ct);

        var outstanding = items
            .Where(i => i.Status is ClearanceStatus.Pending or ClearanceStatus.Blocked)
            .Select(i => ClearanceDepartment.Label(i.Department))
            .ToList();
        if (outstanding.Count > 0)
            throw new HrException(
                "Clearance is not complete — " + string.Join(", ", outstanding)
                + (outstanding.Count == 1 ? " has not signed off." : " have not signed off."));

        var input = await GatherAsync(hr, branchId, employee, separation, lastWorkingDay, items, ct);
        var draft = SettlementCalculator.Compute(input);

        // Recomputing replaces the previous statement outright. The lines are a derived view of the
        // inputs, not a ledger — the audit row below is what keeps the history.
        var existing = await hr.SettlementLines.Where(l => l.SeparationId == separationId).ToListAsync(ct);
        hr.SettlementLines.RemoveRange(existing);

        foreach (var line in draft.Lines)
        {
            hr.SettlementLines.Add(new SettlementLine
            {
                BranchId = branchId,
                SeparationId = separationId,
                Ordinal = line.Ordinal,
                Label = line.Label,
                Kind = line.Kind,
                AmountTaka = line.AmountTaka,
                Source = line.Source,
                Basis = line.Basis,
            });
        }

        var before = new { separation.State, separation.NetPayableTaka };
        separation.LastWorkingDay = lastWorkingDay;
        separation.State = SeparationState.Computed;
        separation.ComputedAt = clock.GetUtcNow();
        separation.ComputedBy = actorId;
        separation.NetPayableTaka = draft.NetPayableTaka;
        separation.ComputationNotes = draft.Notes.Count == 0 ? null : string.Join('\n', draft.Notes);

        audit.Append(kernel, branchId, actorId, actorName, "hr.settlement.compute", "hr.separation",
            separationId, before: before,
            after: new { lastWorkingDay, draft.NetPayableTaka, lines = draft.Lines.Count, notes = draft.Notes },
            tier: 1);

        return draft;
    }

    /// <summary>Adds a line the calculation cannot know about — an ex-gratia, a negotiated deduction.</summary>
    public async Task AddManualLineAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long separationId,
        string label, string kind, long amountTaka, string basis,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new HrException("Name the line — it is printed on the statement.");
        if (string.IsNullOrWhiteSpace(basis))
            throw new HrException("Say why: every line on a settlement states its basis.");
        if (amountTaka <= 0)
            throw new HrException("An amount is required. A deduction is a deduction line, not a negative earning.");
        if (kind is not (PayComponentKind.Earning or PayComponentKind.Deduction))
            throw new HrException("A line is either an earning or a deduction.");

        var separation = await LoadAsync(hr, separationId, ct);
        Require(separation, SeparationState.Computed);

        var next = await hr.SettlementLines.Where(l => l.SeparationId == separationId)
            .Select(l => (int?)l.Ordinal).MaxAsync(ct) ?? 0;

        hr.SettlementLines.Add(new SettlementLine
        {
            BranchId = branchId,
            SeparationId = separationId,
            Ordinal = next + 1,
            Label = label,
            Kind = kind,
            AmountTaka = amountTaka,
            Source = SettlementSource.Manual,
            Basis = basis,
            AddedBy = actorId,
            AddedByName = actorName,
        });

        separation.NetPayableTaka += kind == PayComponentKind.Earning ? amountTaka : -amountTaka;

        audit.Append(kernel, branchId, actorId, actorName, "hr.settlement.add_line",
            "hr.separation", separationId,
            after: new { label, kind, amountTaka, basis, net = separation.NetPayableTaka }, tier: 1);
    }

    /// <summary>
    /// Sends the statement for approval (§12: HR Officer → Accounts Manager → MD). The kernel
    /// approval engine owns routing and thresholds; this module never encodes who approves what.
    /// </summary>
    public async Task<RaiseResult> RaiseApprovalAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long separationId,
        long actorId, string actorRole, CancellationToken ct = default)
    {
        var separation = await LoadAsync(hr, separationId, ct);
        Require(separation, SeparationState.Computed);
        if (separation.ApprovalRequestId is not null)
            throw new HrException("This settlement has already been sent for approval.");

        var result = await approvals.RaiseAsync(
            kernel, branchId, "hr.settlement", "hr.separation", separationId,
            actorId, actorRole, $"Final settlement for separation #{separationId}",
            separation.NetPayableTaka, ct);

        separation.ApprovalRequestId = result.RequestId ?? result.ApprovalId;

        if (result.AutoApproved)
        {
            separation.State = SeparationState.Approved;
            separation.ApprovedAt = clock.GetUtcNow();
            separation.ApprovedBy = actorId;
        }

        return result;
    }

    /// <summary>
    /// Marks the settlement approved once the kernel request has been decided. The approval decision
    /// itself is made in the approvals inbox — this reads it back rather than trusting the caller.
    /// </summary>
    public async Task ApplyApprovalAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long separationId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var separation = await LoadAsync(hr, separationId, ct);
        Require(separation, SeparationState.Computed);

        if (separation.ApprovalRequestId is not { } requestId)
            throw new HrException("This settlement has not been sent for approval yet.");

        var request = await kernel.ApprovalRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new HrException("The approval request could not be found.");

        if (request.State != ApprovalState.Approved)
            throw new HrException(
                $"The approval is {request.State} — a settlement is paid only once it is approved.");

        separation.State = SeparationState.Approved;
        separation.ApprovedAt = request.DecidedAt ?? clock.GetUtcNow();
        separation.ApprovedBy = request.DecidedBy ?? actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.settlement.approve", "hr.separation",
            separationId, after: new { requestId, separation.NetPayableTaka }, tier: 1);
    }

    /// <summary>
    /// Records payment against a supplementary payroll run. A settlement is never paid outside
    /// payroll: the money has to appear in a run for the journal M15 receives to be true.
    /// </summary>
    public async Task MarkPaidAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long separationId, long runId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var separation = await LoadAsync(hr, separationId, ct);
        Require(separation, SeparationState.Approved);

        var run = await hr.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct)
                  ?? throw new HrException("That payroll run no longer exists.");
        if (run.Kind != PayrollRunKind.Supplementary)
            throw new HrException(
                "A final settlement is paid through a supplementary run, not the regular monthly one.");
        if (run.State is not (PayrollRunState.Locked or PayrollRunState.Posted))
            throw new HrException($"Run {run.RunNo} is {run.State} — it has not paid anything yet.");

        var employee = await hr.Employees.FirstOrDefaultAsync(e => e.Id == separation.EmployeeId, ct)
                       ?? throw new HrException("That employee no longer exists.");

        separation.State = SeparationState.Paid;
        separation.PaidRunId = runId;
        separation.PaidAt = clock.GetUtcNow();

        // Only now does the employment actually end: SeparatedOn, the terminal status, the closed
        // assignment and the event, in the one transaction that pays them.
        await employees.SeparateAsync(hr, kernel, branchId, separation.EmployeeId,
            EventKindFor(separation.Kind), separation.LastWorkingDay, separation.Reason,
            actorId, actorName, ct);

        hr.EmploymentEvents.Add(new EmploymentEvent
        {
            BranchId = branchId,
            EmployeeId = separation.EmployeeId,
            Kind = EmploymentEventKind.Settled,
            OnDate = separation.LastWorkingDay,
            Note = $"Final settlement {separation.NetPayableTaka:N0} paid through run {run.RunNo}",
            RecordedAt = clock.GetUtcNow(),
            RecordedBy = actorId,
            RecordedByName = actorName,
        });

        audit.Append(kernel, branchId, actorId, actorName, "hr.settlement.pay", "hr.separation",
            separationId, after: new { runId, run.RunNo, separation.NetPayableTaka, employee.EmployeeCode },
            tier: 1);
    }

    /// <summary>Closes the workflow: relieving date recorded, documents issued.</summary>
    public async Task MarkDocumentsIssuedAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long separationId,
        DateOnly relievingOn, long actorId, string actorName, CancellationToken ct = default)
    {
        var separation = await LoadAsync(hr, separationId, ct);
        Require(separation, SeparationState.Paid);
        if (relievingOn < separation.LastWorkingDay)
            throw new HrException("Relieving cannot precede the last working day.");

        separation.State = SeparationState.DocumentsIssued;
        separation.RelievingOn = relievingOn;
        separation.DocumentsIssuedAt = clock.GetUtcNow();

        hr.EmploymentEvents.Add(new EmploymentEvent
        {
            BranchId = branchId,
            EmployeeId = separation.EmployeeId,
            Kind = EmploymentEventKind.Relieved,
            OnDate = relievingOn,
            RecordedAt = clock.GetUtcNow(),
            RecordedBy = actorId,
            RecordedByName = actorName,
        });

        audit.Append(kernel, branchId, actorId, actorName, "hr.separation.relieve", "hr.separation",
            separationId, after: new { relievingOn }, tier: 1);
    }

    /// <summary>
    /// Withdraws a separation — a resignation retracted, a termination reversed on appeal. Only
    /// before the money is approved; afterwards the correction is a payroll reversal, not a delete.
    /// </summary>
    public async Task CancelAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long separationId, string reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new HrException("Say why the separation is being withdrawn.");

        var separation = await LoadAsync(hr, separationId, ct);
        Require(separation, SeparationState.Initiated, SeparationState.InClearance,
            SeparationState.Computed);

        var employee = await hr.Employees.FirstOrDefaultAsync(e => e.Id == separation.EmployeeId, ct)
                       ?? throw new HrException("That employee no longer exists.");

        separation.State = SeparationState.Cancelled;
        separation.Reason = string.IsNullOrWhiteSpace(separation.Reason)
            ? reason : separation.Reason + " · withdrawn: " + reason;

        // Back to whichever status the employment was in, which is a fact the record still carries.
        employee.Status = employee.ConfirmedOn is not null
            ? EmploymentStatus.Confirmed : EmploymentStatus.Probation;

        audit.Append(kernel, branchId, actorId, actorName, "hr.separation.cancel", "hr.separation",
            separationId, after: new { reason, employee.Status }, tier: 1);
    }

    // ---- loading ---------------------------------------------------------------------------------

    /// <summary>The employer's employment rules in force on a date, or null when none is configured.</summary>
    public static Task<EmploymentPolicy?> EmploymentPolicyAsync(
        HrDbContext hr, long branchId, DateOnly on, CancellationToken ct = default)
        => hr.EmploymentPolicies.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.EffectiveFrom <= on
                        && (p.EffectiveTo == null || p.EffectiveTo >= on))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    private async Task<SettlementInput> GatherAsync(
        HrDbContext hr, long branchId, Employee employee, Separation separation,
        DateOnly lastWorkingDay, IReadOnlyList<ClearanceItem> clearance, CancellationToken ct)
    {
        var policy = await policies.PayrollAsync(hr, branchId, lastWorkingDay, ct);
        var employment = await EmploymentPolicyAsync(hr, branchId, lastWorkingDay, ct);
        var gratuityRule = await hr.GratuityRules.AsNoTracking()
            .Where(r => r.BranchId == branchId && r.EffectiveFrom <= lastWorkingDay
                        && (r.EffectiveTo == null || r.EffectiveTo >= lastWorkingDay))
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        // Gross from the structure in force on the last working day — hard rule 5's shape: the
        // settlement reproduces the pay that was actually in force, not today's.
        var structure = await policies.PayStructureAsync(hr, employee.Id, lastWorkingDay, ct);
        var componentAmounts = structure is null
            ? []
            : await hr.PayStructureComponents.AsNoTracking()
                .Where(c => c.PayStructureId == structure.Id)
                .ToListAsync(ct);

        var earningIds = await hr.PayComponents.AsNoTracking()
            .Where(c => c.BranchId == branchId && c.Kind == PayComponentKind.Earning)
            .Select(c => c.Id)
            .ToListAsync(ct);

        var gross = componentAmounts.Where(c => earningIds.Contains(c.ComponentId)).Sum(c => c.AmountTaka);

        var gratuityBase = 0L;
        if (gratuityRule?.BasedOnComponentId is { } baseId)
            gratuityBase = componentAmounts.FirstOrDefault(c => c.ComponentId == baseId)?.AmountTaka ?? 0;

        // The last period a locked or posted run actually paid this person for.
        var paidThrough = await (
            from line in hr.PayrollLines.AsNoTracking()
            join run in hr.PayrollRuns.AsNoTracking() on line.RunId equals run.Id
            where line.EmployeeId == employee.Id
                  && (run.State == PayrollRunState.Locked || run.State == PayrollRunState.Posted)
            orderby run.Period descending
            select (DateOnly?)run.Period).FirstOrDefaultAsync(ct);

        var encashableName = (string?)null;
        var encashableBp = 0;
        if (employment is { EncashLeaveOnSettlement: true, EncashableLeaveTypeId: { } typeId })
        {
            encashableName = await hr.LeaveTypes.AsNoTracking()
                .Where(t => t.Id == typeId).Select(t => t.Name).FirstOrDefaultAsync(ct);

            var balances = await hr.LeaveBalances.AsNoTracking()
                .Where(b => b.EmployeeId == employee.Id && b.LeaveTypeId == typeId)
                .Select(b => b.OpeningBp + b.AccruedBp + b.AdjustmentBp - b.AvailedBp - b.EncashedBp)
                .ToListAsync(ct);
            encashableBp = Math.Max(0, balances.Sum());
        }

        // Zero today — hr.loan still has no writer, and spec 0057 brings the request/disburse side
        // with it. Reading it now rather than hardcoding zero means the settlement starts recovering
        // loans the day loans exist, with no change here.
        var loanOutstanding = await hr.Loans.AsNoTracking()
            .Where(l => l.EmployeeId == employee.Id && l.State == LoanState.Disbursed)
            .SumAsync(l => l.PrincipalTaka - l.RecoveredTaka, ct);

        var denominator = policy?.DayCountConvention switch
        {
            DayCountConvention.Fixed30 => 30,
            _ => DateTime.DaysInMonth(lastWorkingDay.Year, lastWorkingDay.Month),
        };

        return new SettlementInput
        {
            JoinedOn = employee.JoinedOn,
            LastWorkingDay = lastWorkingDay,
            Kind = separation.Kind,
            NoticeOn = separation.NoticeOn,
            NoticeDays = separation.NoticeDays,
            AdjustNoticePay = employment?.AdjustNoticePay ?? false,
            MonthlyGrossTaka = gross,
            DayCountDenominator = denominator,
            PaidThrough = paidThrough is { } period
                ? period.AddMonths(1).AddDays(-1)     // the run's period is its first day
                : null,
            EncashLeave = employment?.EncashLeaveOnSettlement ?? false,
            EncashableLeaveTypeName = encashableName,
            EncashableBalanceBp = encashableBp,
            Gratuity = gratuityRule is null ? null : new GratuityInput(
                gratuityRule.Id, gratuityRule.MinimumServiceMonths, gratuityRule.DaysPerYearBp,
                gratuityBase),
            LoanOutstandingTaka = loanOutstanding,
            ClearanceDuesTaka = clearance.Sum(i => i.DuesTaka),
        };
    }

    /// <summary>
    /// Loads a separation for a state transition, under a row lock — the idiom
    /// <c>PayrollService.LoadAsync</c> established in spec 0052, and for the same reason. Two
    /// officers pressing Approve together both read <c>computed</c>, both proceed, and both write an
    /// audit row for a decision that happened once. Postgres serialises them here; the loser re-reads
    /// the state the winner wrote and <see cref="Require"/> refuses it in a sentence.
    /// <para>
    /// The lock is taken by id alone, but the entity still comes from the branch-filtered set, so a
    /// separation in another branch is locked briefly and then reported as not existing.
    /// </para>
    /// </summary>
    private static async Task<Separation> LoadAsync(HrDbContext hr, long separationId, CancellationToken ct)
    {
        var locked = await hr.Database.SqlQuery<long>($"""
            SELECT id AS "Value" FROM hr.separation WHERE id = {separationId} FOR UPDATE
            """).ToListAsync(ct);
        if (locked.Count == 0) throw new HrException("That separation no longer exists.");

        return await hr.Separations.FirstOrDefaultAsync(s => s.Id == separationId, ct)
               ?? throw new HrException("That separation no longer exists.");
    }

    /// <summary>Maps §9's separation kinds onto the append-only employment-event vocabulary.</summary>
    public static string EventKindFor(string separationKind) => separationKind switch
    {
        SeparationKind.Termination => EmploymentEventKind.Terminated,
        SeparationKind.Retirement => EmploymentEventKind.Retired,
        SeparationKind.ContractEnd => EmploymentEventKind.ContractEnded,
        SeparationKind.Death => EmploymentEventKind.Deceased,
        _ => EmploymentEventKind.Resigned,
    };

    /// <summary>
    /// §9's order, enforced by name. The message says where the separation actually is, because
    /// "invalid state" tells an operator nothing they can act on.
    /// </summary>
    private static void Require(Separation separation, params string[] allowed)
    {
        if (allowed.Contains(separation.State)) return;

        if (separation.State == SeparationState.Cancelled)
            throw new HrException("This separation was withdrawn.");

        throw new HrException(
            $"This separation is {SeparationState.Label(separation.State).ToLowerInvariant()}; "
            + "that step is " + (Position(separation.State) > Position(allowed[0]) ? "already done." : "not reached yet."));
    }

    private static int Position(string state) => Array.IndexOf(SeparationState.Order, state);
}
