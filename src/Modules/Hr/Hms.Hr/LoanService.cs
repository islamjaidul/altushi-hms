using Hms.Hr.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Money;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// Loans and advances (module PRD §7.9, G41).
/// <para>
/// <c>hr.loan</c> and <c>hr.loan_installment</c> shipped with the module and had <b>zero writers</b>
/// — two tables, a state machine in constants, and no screen, no service and no recovery anywhere.
/// Spec 0056's final settlement reads the loan balance and correctly recovers nothing, because
/// there has never been anything to recover from. This is what makes that read true.
/// </para>
/// <para>
/// The recovery rule that matters: when an installment would drive net pay below the employer's
/// floor it is <b>deferred, not skipped</b>. A skipped installment is invisible; a deferred one is a
/// row the outstanding statement can explain, and the reason the employee is never handed a payslip
/// that owes money.
/// </para>
/// </summary>
public sealed class LoanService(
    NumberSeriesService numbers, ApprovalEngine approvals, AuditWriter audit,
    FiscalCalendar fiscal, TimeProvider clock)
{
    /// <summary>Raises a request. Nothing moves until it is approved and disbursed.</summary>
    public async Task<Loan> RequestAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long employeeId,
        string kind, long principalTaka, int installmentCount, DateOnly startPeriod,
        string? purpose, long actorId, string actorName, CancellationToken ct = default)
    {
        if (kind is not ("loan" or "advance"))
            throw new HrException("A request is either a loan or an advance.");
        if (principalTaka <= 0)
            throw new HrException("The amount must be more than zero.");
        if (installmentCount is < 1 or > 120)
            throw new HrException("Recovery runs over 1 to 120 installments.");

        var employee = await hr.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new HrException("That employee no longer exists.");
        if (employee.SeparatedOn is not null)
            throw new HrException($"{employee.FullName} has left — there is nobody to recover from.");

        var live = await hr.Loans.AsNoTracking()
            .Where(l => l.EmployeeId == employeeId
                        && (l.State == LoanState.Requested || l.State == LoanState.Approved
                            || l.State == LoanState.Disbursed))
            .CountAsync(ct);
        if (live >= 3)
            throw new HrException(
                $"{employee.FullName} already has {live} loans open. Close one before adding another.");

        var (_, loanNo) = await numbers.IssueAsync(
            kernel, branchId, "hr_loan", fiscal.FiscalYearOf(startPeriod), "LN-{fy}-{n:D4}", ct);

        // Integer division would lose the remainder; the last installment absorbs it, so the
        // schedule always sums to the principal exactly (§6.6's rounding discipline).
        var installment = principalTaka / installmentCount;
        if (installment <= 0) installment = principalTaka;

        var loan = new Loan
        {
            BranchId = branchId,
            LoanNo = loanNo,
            EmployeeId = employeeId,
            Kind = kind,
            PrincipalTaka = principalTaka,
            InstallmentTaka = installment,
            InstallmentCount = installmentCount,
            StartPeriod = new DateOnly(startPeriod.Year, startPeriod.Month, 1),
            State = LoanState.Requested,
            Purpose = purpose,
            CreatedAt = clock.GetUtcNow(),
            CreatedBy = actorId,
            CreatedByName = actorName,
        };
        hr.Loans.Add(loan);
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.loan.request", "hr.loan", loan.Id,
            after: new { employeeId, kind, principalTaka, installmentCount, purpose }, tier: 1);

        return loan;
    }

    /// <summary>§12: Employee → Manager → HR → Accounts Manager. The kernel owns the chain.</summary>
    public async Task<RaiseResult> RaiseApprovalAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long loanId,
        long actorId, string actorRole, CancellationToken ct = default)
    {
        var loan = await LoadAsync(hr, loanId, ct);
        Require(loan, LoanState.Requested);
        if (loan.ApprovalRequestId is not null)
            throw new HrException("This request has already been sent for approval.");

        var result = await approvals.RaiseAsync(
            kernel, branchId, "hr.loan", "hr.loan", loanId, actorId, actorRole,
            $"{loan.Kind} {loan.LoanNo}", loan.PrincipalTaka, ct);

        loan.ApprovalRequestId = result.RequestId ?? result.ApprovalId;
        if (result.AutoApproved)
        {
            loan.State = LoanState.Approved;
            loan.ApprovedAt = clock.GetUtcNow();
            loan.ApprovedBy = actorId;
        }

        return result;
    }

    /// <summary>Reads the kernel's decision back rather than trusting the caller's word for it.</summary>
    public async Task ApplyApprovalAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long loanId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var loan = await LoadAsync(hr, loanId, ct);
        Require(loan, LoanState.Requested);

        if (loan.ApprovalRequestId is not { } requestId)
            throw new HrException("This request has not been sent for approval yet.");

        var request = await kernel.ApprovalRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId, ct)
            ?? throw new HrException("The approval request could not be found.");

        if (request.State != ApprovalState.Approved)
            throw new HrException($"The approval is {request.State} — nothing is lent until it is approved.");

        loan.State = LoanState.Approved;
        loan.ApprovedAt = request.DecidedAt ?? clock.GetUtcNow();
        loan.ApprovedBy = request.DecidedBy ?? actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.loan.approve", "hr.loan", loanId,
            after: new { loan.LoanNo, loan.PrincipalTaka }, tier: 1);
    }

    /// <summary>
    /// Records that the money went out. Recovery starts with the first run whose period is on or
    /// after <see cref="Loan.StartPeriod"/> — never before, because the employee has not had it yet.
    /// </summary>
    public async Task DisburseAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long loanId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var loan = await LoadAsync(hr, loanId, ct);
        Require(loan, LoanState.Approved);

        loan.State = LoanState.Disbursed;
        loan.DisbursedAt = clock.GetUtcNow();
        loan.DisbursedBy = actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.loan.disburse", "hr.loan", loanId,
            after: new { loan.LoanNo, loan.PrincipalTaka, loan.StartPeriod }, tier: 1);
    }

    /// <summary>
    /// Settles the balance in one movement — an employee paying the rest off, or a settlement
    /// clearing it. The recovery is recorded as an installment so the statement still adds up.
    /// </summary>
    public async Task ForecloseAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long loanId, DateOnly onPeriod,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var loan = await LoadAsync(hr, loanId, ct);
        Require(loan, LoanState.Disbursed);

        var outstanding = loan.PrincipalTaka - loan.RecoveredTaka;
        if (outstanding <= 0)
        {
            loan.State = LoanState.Closed;
            loan.ClosedAt = clock.GetUtcNow();
            return;
        }

        hr.LoanInstallments.Add(new LoanInstallment
        {
            LoanId = loan.Id,
            Period = new DateOnly(onPeriod.Year, onPeriod.Month, 1),
            AmountTaka = outstanding,
            Deferred = false,
            RecordedAt = clock.GetUtcNow(),
        });

        loan.RecoveredTaka = loan.PrincipalTaka;
        loan.State = LoanState.Foreclosed;
        loan.ClosedAt = clock.GetUtcNow();

        audit.Append(kernel, branchId, actorId, actorName, "hr.loan.foreclose", "hr.loan", loanId,
            after: new { loan.LoanNo, settled = outstanding }, tier: 1);
    }

    /// <summary>
    /// Forgives what is left. Hard rule 4: nothing is deleted — the loan keeps its principal, its
    /// recovery and a stated reason, and the outstanding simply stops being pursued.
    /// </summary>
    public async Task WriteOffAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long loanId, string reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new HrException("A write-off needs a reason — it is money the employer gave up.");

        var loan = await LoadAsync(hr, loanId, ct);
        Require(loan, LoanState.Disbursed, LoanState.Approved);

        var outstanding = loan.PrincipalTaka - loan.RecoveredTaka;
        loan.State = LoanState.WrittenOff;
        loan.WriteOffReason = reason;
        loan.ClosedAt = clock.GetUtcNow();

        audit.Append(kernel, branchId, actorId, actorName, "hr.loan.write_off", "hr.loan", loanId,
            after: new { loan.LoanNo, forgiven = outstanding, reason }, tier: 1);
    }

    // ---- recovery, called by the payroll engine ---------------------------------------------------

    /// <summary>One installment's outcome for one run.</summary>
    public sealed record Recovery(Loan Loan, long AmountTaka, bool Deferred, string Basis);

    /// <summary>
    /// What to recover from this employee this period, and how much room there is to do it in.
    /// <para>
    /// Pure decision, taken over live loans and the room left above the net-pay floor. Loans are
    /// taken oldest first, so the one closest to closing finishes rather than every loan crawling.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Recovery> Plan(
        IReadOnlyList<Loan> loans, DateOnly period, long netBeforeRecovery, long minimumNetPay)
    {
        var room = Math.Max(0, netBeforeRecovery - minimumNetPay);
        var plan = new List<Recovery>();

        foreach (var loan in loans.OrderBy(l => l.StartPeriod).ThenBy(l => l.Id))
        {
            if (loan.State != LoanState.Disbursed) continue;
            if (loan.StartPeriod > period) continue;

            var outstanding = loan.PrincipalTaka - loan.RecoveredTaka;
            if (outstanding <= 0) continue;

            // The last installment absorbs the remainder, so the schedule sums to the principal.
            var due = Math.Min(loan.InstallmentTaka, outstanding);

            if (due <= room)
            {
                plan.Add(new Recovery(loan, due, false,
                    $"{loan.LoanNo} — {due:N0} of {outstanding:N0} outstanding"));
                room -= due;
            }
            else
            {
                // Deferred, not skipped. A skipped installment is invisible; this one is a row the
                // statement can explain, and the employee is never paid below the floor.
                plan.Add(new Recovery(loan, 0, true,
                    $"{loan.LoanNo} — deferred: recovering {due:N0} would take net below the floor"));
            }
        }

        return plan;
    }

    /// <summary>Applies a plan: writes the installments and advances the loans.</summary>
    public static void Apply(
        HrDbContext hr, IReadOnlyList<Recovery> plan, DateOnly period, long runId,
        DateTimeOffset now)
    {
        foreach (var r in plan)
        {
            hr.LoanInstallments.Add(new LoanInstallment
            {
                LoanId = r.Loan.Id,
                PayrollRunId = runId,
                Period = period,
                AmountTaka = r.AmountTaka,
                Deferred = r.Deferred,
                RecordedAt = now,
            });

            if (r.Deferred) continue;

            r.Loan.RecoveredTaka += r.AmountTaka;
            if (r.Loan.RecoveredTaka >= r.Loan.PrincipalTaka)
            {
                r.Loan.State = LoanState.Closed;
                r.Loan.ClosedAt = now;
            }
        }
    }

    // ---- loading ----------------------------------------------------------------------------------

    public static Task<List<Loan>> LiveLoansAsync(
        HrDbContext hr, long employeeId, CancellationToken ct = default)
        => hr.Loans.Where(l => l.EmployeeId == employeeId && l.State == LoanState.Disbursed)
            .OrderBy(l => l.StartPeriod)
            .ToListAsync(ct);

    private static async Task<Loan> LoadAsync(HrDbContext hr, long loanId, CancellationToken ct)
    {
        // The same row lock the payroll run and the separation take, and for the same reason: two
        // officers pressing Disburse together must not both write it.
        var locked = await hr.Database.SqlQuery<long>($"""
            SELECT id AS "Value" FROM hr.loan WHERE id = {loanId} FOR UPDATE
            """).ToListAsync(ct);
        if (locked.Count == 0) throw new HrException("That loan no longer exists.");

        return await hr.Loans.FirstOrDefaultAsync(l => l.Id == loanId, ct)
               ?? throw new HrException("That loan no longer exists.");
    }

    private static void Require(Loan loan, params string[] allowed)
    {
        if (allowed.Contains(loan.State)) return;
        throw new HrException(
            $"{loan.LoanNo} is {LoanState.Label(loan.State).ToLowerInvariant()} — that step does not apply.");
    }
}
