using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// The four things an employee asks for about their own time (module PRD §7.4, §7.5 — G23, G24, G25,
/// G28, G29): correct my day, approve my overtime, let me take the comp-off I earned, and let me be
/// out for two hours.
/// <para>
/// The load-bearing rule: an approved regularization calls the <b>same</b>
/// <see cref="AttendanceService.CorrectAsync"/> the HR screen calls. A second write path would be a
/// second place for the arrears rule and the audit tier to drift, and spec 0039 already fought that
/// fight once.
/// </para>
/// </summary>
public sealed class TimeRequestService(
    AttendanceService attendance, PolicyResolver policies, AuditWriter audit, TimeProvider clock)
{
    // ---- regularization (G23) --------------------------------------------------------------------

    public async Task<RegularizationRequest> RaiseRegularizationAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId, DateOnly onDate,
        string requestedStatus, string reason, long actorId, string actorName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new HrException("Say what happened — the reason becomes the correction's reason.");
        if (requestedStatus is not (AttendanceStatus.Present or AttendanceStatus.Absent
            or AttendanceStatus.OnLeave or AttendanceStatus.WeeklyOff or AttendanceStatus.Errand))
            throw new HrException("That is not a day this request can ask for.");

        var day = await hr.AttendanceDays.AsNoTracking()
            .FirstOrDefaultAsync(d => d.EmployeeId == employeeId && d.OnDate == onDate, ct)
            ?? throw new HrException(
                $"No attendance is derived for {onDate:dd MMM yyyy} yet. A day has to exist before "
                + "it can be corrected.");

        if (day.Status == requestedStatus)
            throw new HrException($"{onDate:dd MMM yyyy} is already recorded as {requestedStatus}.");

        var live = await hr.RegularizationRequests.AsNoTracking()
            .AnyAsync(r => r.EmployeeId == employeeId && r.OnDate == onDate
                           && (r.State == RequestState.Raised || r.State == RequestState.Recommended
                               || r.State == RequestState.Approved), ct);
        if (live)
            throw new HrException($"There is already a request open for {onDate:dd MMM yyyy}.");

        var request = new RegularizationRequest
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            OnDate = onDate,
            RequestedStatus = requestedStatus,
            Reason = reason,
            RaisedAt = clock.GetUtcNow(),
            RaisedBy = actorId,
            RaisedByName = actorName,
        };
        hr.RegularizationRequests.Add(request);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.regularization.raise",
            "hr.regularization_request", request.Id,
            after: new { employeeId, onDate, requestedStatus, reason }, tier: 2);

        return request;
    }

    /// <summary>
    /// Decides it. Approval produces the one correction, carrying the requester's reason and both
    /// names — indistinguishable in the audit from an HR correction, except that it names two people.
    /// </summary>
    public async Task DecideRegularizationAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long requestId, bool approve,
        string? note, long actorId, string actorName, CancellationToken ct = default)
    {
        var request = await hr.RegularizationRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
                      ?? throw new HrException("That request no longer exists.");
        Require(request.State, RequestState.Raised, RequestState.Recommended);

        request.DecidedBy = actorId;
        request.DecidedByName = actorName;
        request.DecidedAt = clock.GetUtcNow();
        request.DecisionNote = note;

        if (!approve)
        {
            request.State = RequestState.Rejected;
            audit.Append(kernel, branchId, actorId, actorName, "hr.regularization.reject",
                "hr.regularization_request", requestId, after: new { note }, tier: 2);
            return;
        }

        var day = await hr.AttendanceDays.AsNoTracking()
            .FirstOrDefaultAsync(d => d.EmployeeId == request.EmployeeId && d.OnDate == request.OnDate, ct)
            ?? throw new HrException("The attendance day has gone — nothing to correct.");

        // The one correction path. Its reason names the requester, so the audit reads as what it
        // is: an employee said something and somebody with authority agreed.
        var correction = await attendance.CorrectAsync(
            hr, kernel, branchId, day.Id, request.RequestedStatus, toWorkedMinutes: null,
            $"{request.Reason} (requested by {request.RaisedByName})",
            actorId, actorName, ct);

        // Flushed before the id is read: CorrectAsync stages the row on the caller's context, so
        // correction.Id is still 0 until something saves. Without this the request linked to
        // correction #0 — a row that never existed, and a trail that stopped at the request.
        await hr.SaveChangesAsync(ct);

        request.State = RequestState.Applied;
        request.CorrectionId = correction.Id;

        audit.Append(kernel, branchId, actorId, actorName, "hr.regularization.approve",
            "hr.regularization_request", requestId,
            after: new { request.EmployeeId, request.OnDate, request.RequestedStatus,
                correctionId = correction.Id }, tier: 1);
    }

    // ---- overtime approval and banking (G24, G25) ---------------------------------------------------

    public async Task<OvertimeRequest> RaiseOvertimeAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId, DateOnly onDate,
        int minutes, string kind, string? reason, long actorId, string actorName,
        CancellationToken ct = default)
    {
        if (minutes is <= 0 or > 1440)
            throw new HrException("Overtime is 1 to 1440 minutes.");
        if (kind is not (OvertimeApprovalKind.Pre or OvertimeApprovalKind.Post))
            throw new HrException("Overtime is approved before or after the fact.");

        var live = await hr.OvertimeRequests.AsNoTracking()
            .AnyAsync(r => r.EmployeeId == employeeId && r.OnDate == onDate
                           && (r.State == RequestState.Raised || r.State == RequestState.Recommended
                               || r.State == RequestState.Approved), ct);
        if (live)
            throw new HrException($"Overtime for {onDate:dd MMM yyyy} is already open.");

        var request = new OvertimeRequest
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            OnDate = onDate,
            Kind = kind,
            Minutes = minutes,
            Reason = reason,
            RaisedAt = clock.GetUtcNow(),
            RaisedBy = actorId,
            RaisedByName = actorName,
        };
        hr.OvertimeRequests.Add(request);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.overtime.raise",
            "hr.overtime_request", request.Id,
            after: new { employeeId, onDate, minutes, kind }, tier: 2);

        return request;
    }

    /// <summary>
    /// Approves or refuses overtime. On approval the rule in force decides what happens next: paid
    /// through the run, or <b>banked</b> — which is what finally reads
    /// <c>OvertimeRule.BankInsteadOfPay</c>, a column the Policies screen has been writing since the
    /// module shipped and nothing has ever looked at.
    /// </summary>
    public async Task DecideOvertimeAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long requestId, bool approve,
        string? note, long actorId, string actorName, CancellationToken ct = default)
    {
        var request = await hr.OvertimeRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
                      ?? throw new HrException("That request no longer exists.");
        Require(request.State, RequestState.Raised, RequestState.Recommended);

        request.DecidedBy = actorId;
        request.DecidedByName = actorName;
        request.DecidedAt = clock.GetUtcNow();
        request.DecisionNote = note;

        if (!approve)
        {
            request.State = RequestState.Rejected;
            audit.Append(kernel, branchId, actorId, actorName, "hr.overtime.reject",
                "hr.overtime_request", requestId, after: new { note }, tier: 2);
            return;
        }

        var assignment = await policies.AssignmentAsync(hr, request.EmployeeId, request.OnDate, ct);
        var rule = await policies.OvertimeAsync(hr, branchId, assignment?.GradeId, request.OnDate, ct);

        request.State = RequestState.Approved;
        request.Banked = rule?.BankInsteadOfPay ?? false;

        if (request.Banked)
        {
            var expiry = rule!.CompOffExpiryDays > 0
                ? request.OnDate.AddDays(rule.CompOffExpiryDays)
                : (DateOnly?)null;

            hr.OvertimeBank.Add(new OvertimeBankEntry
            {
                BranchId = branchId,
                EmployeeId = request.EmployeeId,
                Kind = OvertimeBankKind.Credit,
                OnDate = request.OnDate,
                Minutes = request.Minutes,
                Narration = $"Overtime banked for {request.OnDate:dd MMM yyyy}",
                ExpiresOn = expiry,
                OvertimeRequestId = request.Id,
                RecordedAt = clock.GetUtcNow(),
                RecordedBy = actorId,
            });
        }

        audit.Append(kernel, branchId, actorId, actorName, "hr.overtime.approve",
            "hr.overtime_request", requestId,
            after: new { request.EmployeeId, request.OnDate, request.Minutes, request.Banked }, tier: 1);
    }

    /// <summary>
    /// The bank balance: credits less debits less expiries. A sum of movements, never a column
    /// somebody updates — the same discipline the member ledger follows.
    /// </summary>
    public static async Task<int> BankBalanceAsync(
        HrDbContext hr, long employeeId, CancellationToken ct = default)
    {
        var movements = await hr.OvertimeBank.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId)
            .Select(e => new { e.Kind, e.Minutes })
            .ToListAsync(ct);

        return Balance(movements.Select(m => (m.Kind, m.Minutes)));
    }

    /// <summary>Pure, so the arithmetic is pinned without a database.</summary>
    public static int Balance(IEnumerable<(string Kind, int Minutes)> movements)
    {
        var balance = 0;
        foreach (var (kind, minutes) in movements)
            balance += kind == OvertimeBankKind.Credit ? minutes : -minutes;
        return balance;
    }

    // ---- comp-off (G25) -------------------------------------------------------------------------------

    public async Task<CompOffRequest> RaiseCompOffAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId, DateOnly onDate,
        int minutes, string? reason, long actorId, string actorName, CancellationToken ct = default)
    {
        if (minutes is <= 0 or > 1440)
            throw new HrException("A comp-off is 1 to 1440 minutes.");

        var balance = await BankBalanceAsync(hr, employeeId, ct);
        if (minutes > balance)
        {
            throw new HrException(
                $"Only {balance / 60}h {balance % 60}m is banked — a comp-off cannot be taken "
                + "against minutes that are not there.");
        }

        var request = new CompOffRequest
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            OnDate = onDate,
            Minutes = minutes,
            Reason = reason,
            RaisedAt = clock.GetUtcNow(),
            RaisedBy = actorId,
            RaisedByName = actorName,
        };
        hr.CompOffRequests.Add(request);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.compoff.raise",
            "hr.comp_off_request", request.Id, after: new { employeeId, onDate, minutes }, tier: 2);

        return request;
    }

    public async Task DecideCompOffAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long requestId, bool approve,
        string? note, long actorId, string actorName, CancellationToken ct = default)
    {
        var request = await hr.CompOffRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
                      ?? throw new HrException("That request no longer exists.");
        Require(request.State, RequestState.Raised, RequestState.Recommended);

        request.DecidedBy = actorId;
        request.DecidedByName = actorName;
        request.DecidedAt = clock.GetUtcNow();
        request.DecisionNote = note;

        if (!approve)
        {
            request.State = RequestState.Rejected;
            audit.Append(kernel, branchId, actorId, actorName, "hr.compoff.reject",
                "hr.comp_off_request", requestId, after: new { note }, tier: 2);
            return;
        }

        // Re-checked at approval, not only at request: minutes may have expired or been spent in
        // between, and the balance at the moment of the decision is the one that matters.
        var balance = await BankBalanceAsync(hr, request.EmployeeId, ct);
        if (request.Minutes > balance)
        {
            throw new HrException(
                $"Only {balance / 60}h {balance % 60}m is banked now — the balance has moved since "
                + "this was requested.");
        }

        request.State = RequestState.Approved;
        hr.OvertimeBank.Add(new OvertimeBankEntry
        {
            BranchId = branchId,
            EmployeeId = request.EmployeeId,
            Kind = OvertimeBankKind.Debit,
            OnDate = request.OnDate,
            Minutes = request.Minutes,
            Narration = $"Comp-off taken on {request.OnDate:dd MMM yyyy}",
            CompOffRequestId = request.Id,
            RecordedAt = clock.GetUtcNow(),
            RecordedBy = actorId,
        });

        audit.Append(kernel, branchId, actorId, actorName, "hr.compoff.approve",
            "hr.comp_off_request", requestId,
            after: new { request.EmployeeId, request.OnDate, request.Minutes }, tier: 1);
    }

    /// <summary>
    /// Lapses banked minutes whose window has closed. Run daily; idempotent, because an expiry is
    /// written against a credit that has not already been expired.
    /// </summary>
    public async Task<int> ExpireBankAsync(
        HrDbContext hr, long branchId, DateOnly today, long actorId, CancellationToken ct = default)
    {
        var credits = await hr.OvertimeBank.AsNoTracking()
            .Where(e => e.BranchId == branchId && e.Kind == OvertimeBankKind.Credit
                        && e.ExpiresOn != null && e.ExpiresOn < today)
            .Select(e => new { e.Id, e.EmployeeId, e.Minutes, e.OnDate })
            .ToListAsync(ct);
        if (credits.Count == 0) return 0;

        var alreadyExpired = await hr.OvertimeBank.AsNoTracking()
            .Where(e => e.BranchId == branchId && e.Kind == OvertimeBankKind.Expiry
                        && e.OvertimeRequestId != null)
            .Select(e => e.OvertimeRequestId!.Value)
            .ToListAsync(ct);
        var seen = alreadyExpired.ToHashSet();

        var expired = 0;
        foreach (var credit in credits)
        {
            if (!seen.Add(credit.Id)) continue;

            // Only what is still there: an employee who has already spent the minutes has nothing
            // left to lapse, and expiring them again would drive the balance negative.
            var balance = await BankBalanceAsync(hr, credit.EmployeeId, ct);
            var lapse = Math.Min(credit.Minutes, balance);
            if (lapse <= 0) continue;

            hr.OvertimeBank.Add(new OvertimeBankEntry
            {
                BranchId = branchId,
                EmployeeId = credit.EmployeeId,
                Kind = OvertimeBankKind.Expiry,
                OnDate = today,
                Minutes = lapse,
                Narration = $"Banked overtime from {credit.OnDate:dd MMM yyyy} lapsed unused",
                OvertimeRequestId = credit.Id,
                RecordedAt = clock.GetUtcNow(),
                RecordedBy = actorId,
            });
            await hr.SaveChangesAsync(ct);
            expired++;
        }

        return expired;
    }

    // ---- short leave and outdoor duty (G29) ------------------------------------------------------------

    public async Task<ShortLeaveRequest> RaiseShortLeaveAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId, DateOnly onDate,
        string kind, TimeOnly from, TimeOnly to, string reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (!ShortLeaveKind.All.Contains(kind))
            throw new HrException("That is not a kind of hourly absence this product records.");
        if (to <= from)
            throw new HrException("The end time has to be after the start.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new HrException("Say where you are going — this is what the manager decides on.");

        var request = new ShortLeaveRequest
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            OnDate = onDate,
            Kind = kind,
            FromTime = from,
            ToTime = to,
            Reason = reason,
            RaisedAt = clock.GetUtcNow(),
            RaisedBy = actorId,
            RaisedByName = actorName,
        };
        hr.ShortLeaveRequests.Add(request);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.short_leave.raise",
            "hr.short_leave_request", request.Id,
            after: new { employeeId, onDate, kind, from, to, reason }, tier: 2);

        return request;
    }

    /// <summary>
    /// Approving marks the day <c>errand</c> — the status that shipped with the module and had no
    /// producer. The day stays payable: an hourly absence does not consume a leave day, which is the
    /// entire point of short leave (§7.4).
    /// </summary>
    public async Task DecideShortLeaveAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long requestId, bool approve,
        string? note, long actorId, string actorName, CancellationToken ct = default)
    {
        var request = await hr.ShortLeaveRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
                      ?? throw new HrException("That request no longer exists.");
        Require(request.State, RequestState.Raised, RequestState.Recommended);

        request.DecidedBy = actorId;
        request.DecidedByName = actorName;
        request.DecidedAt = clock.GetUtcNow();
        request.DecisionNote = note;
        request.State = approve ? RequestState.Approved : RequestState.Rejected;

        if (approve)
        {
            var day = await hr.AttendanceDays
                .FirstOrDefaultAsync(d => d.EmployeeId == request.EmployeeId
                                          && d.OnDate == request.OnDate, ct);
            // Only a present day becomes an errand. Somebody already on leave or absent is not
            // "out for two hours" — the request is approved and the day is left alone.
            if (day is not null && day.Status == AttendanceStatus.Present)
                day.Status = AttendanceStatus.Errand;
        }

        audit.Append(kernel, branchId, actorId, actorName,
            approve ? "hr.short_leave.approve" : "hr.short_leave.reject",
            "hr.short_leave_request", requestId,
            after: new { request.EmployeeId, request.OnDate, request.Kind, note }, tier: 2);
    }

    // ---- shift swap (G28) -------------------------------------------------------------------------------

    public async Task<ShiftSwapRequest> RaiseSwapAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId,
        long requesterId, DateOnly requesterDate, long counterpartId, DateOnly counterpartDate,
        string? reason, long actorId, string actorName, CancellationToken ct = default)
    {
        if (requesterId == counterpartId)
            throw new HrException("Nobody swaps a shift with themselves.");

        var mine = await hr.RosterEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == requesterId && e.OnDate == requesterDate, ct)
            ?? throw new HrException($"You are not rostered on {requesterDate:dd MMM yyyy}.");
        var theirs = await hr.RosterEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == counterpartId && e.OnDate == counterpartDate, ct)
            ?? throw new HrException($"They are not rostered on {counterpartDate:dd MMM yyyy}.");

        if (mine.ShiftId == theirs.ShiftId && requesterDate == counterpartDate)
            throw new HrException("Those are the same shift on the same day — there is nothing to swap.");

        var request = new ShiftSwapRequest
        {
            BranchId = branchId,
            RequesterEmployeeId = requesterId,
            CounterpartEmployeeId = counterpartId,
            RequesterDate = requesterDate,
            CounterpartDate = counterpartDate,
            Reason = reason,
            RaisedAt = clock.GetUtcNow(),
            RaisedBy = actorId,
            RaisedByName = actorName,
        };
        hr.ShiftSwapRequests.Add(request);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.shift_swap.raise",
            "hr.shift_swap_request", request.Id,
            after: new { requesterId, requesterDate, counterpartId, counterpartDate }, tier: 2);

        return request;
    }

    /// <summary>Approving exchanges exactly two roster entries and records both sides.</summary>
    public async Task DecideSwapAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long requestId, bool approve,
        string? note, long actorId, string actorName, CancellationToken ct = default)
    {
        var request = await hr.ShiftSwapRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct)
                      ?? throw new HrException("That request no longer exists.");
        Require(request.State, RequestState.Raised, RequestState.Recommended);

        request.DecidedBy = actorId;
        request.DecidedByName = actorName;
        request.DecidedAt = clock.GetUtcNow();
        request.DecisionNote = note;

        if (!approve)
        {
            request.State = RequestState.Rejected;
            audit.Append(kernel, branchId, actorId, actorName, "hr.shift_swap.reject",
                "hr.shift_swap_request", requestId, after: new { note }, tier: 2);
            return;
        }

        var mine = await hr.RosterEntries
            .FirstOrDefaultAsync(e => e.EmployeeId == request.RequesterEmployeeId
                                      && e.OnDate == request.RequesterDate, ct)
            ?? throw new HrException("The requester's roster entry has gone.");
        var theirs = await hr.RosterEntries
            .FirstOrDefaultAsync(e => e.EmployeeId == request.CounterpartEmployeeId
                                      && e.OnDate == request.CounterpartDate, ct)
            ?? throw new HrException("The other person's roster entry has gone.");

        (mine.ShiftId, theirs.ShiftId) = (theirs.ShiftId, mine.ShiftId);
        request.State = RequestState.Applied;

        audit.Append(kernel, branchId, actorId, actorName, "hr.shift_swap.approve",
            "hr.shift_swap_request", requestId,
            after: new
            {
                request.RequesterEmployeeId, request.RequesterDate,
                request.CounterpartEmployeeId, request.CounterpartDate,
            }, tier: 1);
    }

    // ---- shared -----------------------------------------------------------------------------------------

    private static void Require(string state, params string[] allowed)
    {
        if (allowed.Contains(state)) return;
        throw new HrException(
            $"This request is {RequestState.Label(state).ToLowerInvariant()} — it cannot be decided again.");
    }
}
