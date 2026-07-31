using System.Text.Json;
using Hms.Hr.Contracts;
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
/// Payroll, walking §11: Generated → Exceptions Reviewed → Approved⚿ → Locked → Posted to Accounts.
///
/// Three rules govern everything here.
///
/// 1. <b>A locked run is never edited</b> (hard rule 4). A mistake is undone by a reversal run that
///    references the original; a late attendance correction becomes an arrears line in the next run.
/// 2. <b>Every figure is reproducible</b> (hard rule 5). Each line pins the pay structure and the
///    policy rows it resolved, so re-opening last March shows last March's numbers even after every
///    rate has changed since.
/// 3. <b>No statutory number lives in this file</b> (ADR-0027). The engine knows how to apply a slab
///    set, a percentage and a multiplier. What those are is the employer's data, and when it is
///    missing the run says so rather than assuming.
/// </summary>
public sealed class PayrollService(
    NumberSeriesService numbers, AuditWriter audit, ApprovalEngine approvals,
    PolicyResolver policies, FiscalCalendar fiscal, TimeProvider clock)
{
    /// <summary>
    /// Builds a draft run for a period. Exceptions are counted, not hidden: US16.1 promises the HR
    /// officer that every unresolved attendance day is pre-listed before any money is approved.
    /// </summary>
    public async Task<PayrollRun> GenerateAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, DateOnly period,
        long actorId, string actorName, string kind = PayrollRunKind.Regular,
        CancellationToken ct = default)
    {
        period = new DateOnly(period.Year, period.Month, 1);
        var periodEnd = period.AddMonths(1).AddDays(-1);

        var existing = await hr.PayrollRuns.AsNoTracking()
            .Where(r => r.BranchId == branchId && r.Period == period && r.Kind == kind
                        && r.State != PayrollRunState.Cancelled)
            .FirstOrDefaultAsync(ct);
        if (existing is not null && kind == PayrollRunKind.Regular)
            throw new HrException(
                $"{period:MMMM yyyy} already has a payroll run ({existing.RunNo}, {existing.State.Replace('_', ' ')}).");

        var policy = await policies.PayrollAsync(hr, branchId, periodEnd, ct)
                     ?? throw new HrException(
                         "No payroll policy is configured for this period. Set the day-count convention "
                         + "and minimum net pay under HR → Policies before running payroll.");

        var sequence = await hr.PayrollRuns.AsNoTracking()
            .Where(r => r.BranchId == branchId && r.Period == period && r.Kind == kind)
            .CountAsync(ct) + 1;

        var (_, runNo) = await numbers.IssueAsync(
            kernel, branchId, "payroll", fiscal.FiscalYearOf(period), "PR-{fy}-{n:D4}", ct);

        var run = new PayrollRun
        {
            BranchId = branchId,
            RunNo = runNo,
            Period = period,
            Kind = kind,
            Sequence = sequence,
            State = PayrollRunState.Generated,
            GeneratedAt = clock.GetUtcNow(),
            GeneratedBy = actorId,
        };
        hr.PayrollRuns.Add(run);
        await hr.SaveChangesAsync(ct);

        var employees = await hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId
                        && e.JoinedOn <= periodEnd
                        && (e.SeparatedOn == null || e.SeparatedOn >= period))
            .OrderBy(e => e.EmployeeCode)
            .ToListAsync(ct);

        var components = await hr.PayComponents.AsNoTracking()
            .Where(c => c.BranchId == branchId && c.Active)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync(ct);

        long totalGross = 0, totalDeduction = 0, totalNet = 0, totalEmployerCost = 0;
        var exceptionCount = 0;

        foreach (var employee in employees)
        {
            var line = await BuildLineAsync(
                hr, branchId, run, employee, period, periodEnd, policy, components, ct);
            hr.PayrollLines.Add(line.Line);
            await hr.SaveChangesAsync(ct);

            foreach (var cl in line.Components)
            {
                cl.PayrollLineId = line.Line.Id;
                cl.RunId = run.Id;
                hr.PayrollComponentLines.Add(cl);
            }

            totalGross += line.Line.GrossEarningsTaka;
            totalDeduction += line.Line.TotalDeductionsTaka;
            totalNet += line.Line.NetPayTaka;
            totalEmployerCost += line.Line.EmployerCostTaka;
            exceptionCount += line.Exceptions;
        }

        run.EmployeeCount = employees.Count;
        run.TotalGrossTaka = totalGross;
        run.TotalDeductionTaka = totalDeduction;
        run.TotalNetTaka = totalNet;
        run.TotalEmployerCostTaka = totalEmployerCost;
        run.ExceptionCount = exceptionCount;

        audit.Append(kernel, branchId, actorId, actorName, "hr.payroll.generate", "hr.payroll_run",
            run.Id, after: new { runNo, period, employees.Count, totalNet, exceptionCount }, tier: 1);

        return run;
    }

    private sealed record BuiltLine(PayrollLine Line, List<PayrollComponentLine> Components, int Exceptions);

    private async Task<BuiltLine> BuildLineAsync(
        HrDbContext hr, long branchId, PayrollRun run, Employee employee,
        DateOnly period, DateOnly periodEnd, PayrollPolicy policy,
        IReadOnlyList<PayComponent> components, CancellationToken ct)
    {
        var assignment = await policies.AssignmentAsync(hr, employee.Id, periodEnd, ct);
        var structure = await policies.PayStructureAsync(hr, employee.Id, periodEnd, ct);

        var line = new PayrollLine
        {
            RunId = run.Id,
            BranchId = branchId,
            EmployeeId = employee.Id,
            EmployeeCode = employee.EmployeeCode,
            EmployeeName = employee.FullName,
            OrgUnitId = assignment?.OrgUnitId,
            DesignationId = assignment?.DesignationId,
            GradeId = assignment?.GradeId,
            PayStructureId = structure?.Id,
        };

        var lines = new List<PayrollComponentLine>();

        if (structure is null)
        {
            line.Note = "No pay structure is effective for this period — nothing was computed.";
            return new BuiltLine(line, lines, 1);
        }

        // --- how much of the period this person was actually employed for (mid-month join/exit)
        var activeFrom = employee.JoinedOn > period ? employee.JoinedOn : period;
        var activeTo = employee.SeparatedOn is { } sep && sep < periodEnd ? sep : periodEnd;
        var employedDays = activeTo.DayNumber - activeFrom.DayNumber + 1;
        var denominator = policy.DayCountConvention switch
        {
            DayCountConvention.Fixed30 => 30,
            DayCountConvention.WorkingDays => await WorkingDaysAsync(hr, branchId, period, periodEnd, ct),
            _ => periodEnd.Day,
        };
        if (denominator <= 0) denominator = periodEnd.Day;

        line.PeriodDays = periodEnd.Day;
        var employedBp = (int)Math.Min(Taka.Bp, (long)employedDays * Taka.Bp / denominator);

        // --- attendance for the period
        var days = await hr.AttendanceDays.AsNoTracking()
            .Where(d => d.EmployeeId == employee.Id && d.OnDate >= activeFrom && d.OnDate <= activeTo)
            .ToListAsync(ct);

        line.PresentDaysBp = days.Where(d => d.Status == AttendanceStatus.Present).Sum(d => d.PayableFractionBp);
        line.AbsentDaysBp = days.Count(d => d.Status == AttendanceStatus.Absent) * 10000;
        line.LeaveDaysBp = days.Count(d => d.Status == AttendanceStatus.OnLeave) * 10000;
        line.LateCount = days.Count(d => d.LateMinutes > 0);
        line.OvertimeMinutes = days.Sum(d => d.OvertimeMinutes);
        line.PayableDaysBp = employedBp;

        var exceptions = days.Count(d => d.Status is AttendanceStatus.Incomplete);

        // --- earnings from the structure, prorated by the employed fraction
        var structureComponents = await hr.PayStructureComponents.AsNoTracking()
            .Where(c => c.PayStructureId == structure.Id)
            .ToListAsync(ct);
        var byId = structureComponents.ToDictionary(c => c.ComponentId);

        var basicAmount = 0L;
        var earnings = new List<(PayComponent Def, long Amount)>();

        foreach (var def in components.Where(c => c.Kind == PayComponentKind.Earning))
        {
            if (!byId.TryGetValue(def.Id, out var configured) && def.CalcMethod != PayComponentCalc.PercentOf)
                continue;

            var amount = def.CalcMethod switch
            {
                PayComponentCalc.Fixed => configured?.AmountTaka ?? 0,
                PayComponentCalc.PercentOf => Taka.ApplyBp(
                    def.BasedOnComponentId is { } baseId && byId.TryGetValue(baseId, out var b) ? b.AmountTaka : 0,
                    configured?.PercentBpOverride ?? def.PercentBp),
                _ => 0,
            };
            if (amount == 0) continue;

            amount = Taka.Prorate(amount, employedBp);
            if (basicAmount == 0) basicAmount = amount;
            earnings.Add((def, amount));
        }

        // --- computed components: attendance and policy driven, never a rate we invented
        var deductionRule = await policies.DeductionAsync(hr, branchId, periodEnd, ct);
        var graceRule = await policies.GraceTimeAsync(hr, branchId, periodEnd, ct);
        var overtimeRule = await policies.OvertimeAsync(hr, branchId, assignment?.GradeId, periodEnd, ct);

        var grossBeforeComputed = earnings.Sum(e => e.Amount);
        var dayRate = denominator > 0 ? grossBeforeComputed / denominator : 0;

        var deductions = new List<(PayComponent Def, long Amount, string Basis)>();

        if (line.AbsentDaysBp > 0 && deductionRule is not null)
        {
            var def = components.FirstOrDefault(
                c => c.ComputedKind == ComputedComponent.AbsenceDeduction);
            if (def is not null)
            {
                var amount = Taka.ApplyBp(dayRate * (line.AbsentDaysBp / 10000), deductionRule.PerAbsentDayBp);
                if (amount > 0)
                    deductions.Add((def, amount, $"{line.AbsentDaysBp / 10000} absent day(s)"));
            }
        }

        if (graceRule is not null && line.LateCount > graceRule.FreeLateCountPerMonth)
        {
            var def = components.FirstOrDefault(c => c.ComputedKind == ComputedComponent.LateDeduction);
            if (def is not null)
            {
                var excess = line.LateCount - graceRule.FreeLateCountPerMonth;
                var amount = Taka.ApplyBp(dayRate * excess, graceRule.DeductionPerLateBp);
                if (amount > 0)
                    deductions.Add((def, amount, $"{excess} late arrival(s) beyond {graceRule.FreeLateCountPerMonth}"));
            }
        }

        if (overtimeRule is { BankInsteadOfPay: false } && line.OvertimeMinutes > overtimeRule.ThresholdMinutes)
        {
            var def = components.FirstOrDefault(c => c.ComputedKind == ComputedComponent.OvertimePay);
            if (def is not null)
            {
                var minutes = Math.Min(
                    line.OvertimeMinutes,
                    overtimeRule.MaxMinutesPerMonth > 0 ? overtimeRule.MaxMinutesPerMonth : line.OvertimeMinutes);
                var minuteRate = dayRate > 0 ? dayRate / (8 * 60) : 0;
                var amount = Taka.ApplyBp(minuteRate * minutes, overtimeRule.MultiplierBp);
                if (amount > 0)
                {
                    earnings.Add((def, amount));
                    lines.Add(Component(def, amount, $"{minutes} OT minute(s)"));
                }
            }
        }

        // --- arrears from corrections against an already-locked run (hard rule 4)
        var arrears = await hr.AttendanceCorrections.AsNoTracking()
            .Where(c => c.BranchId == branchId && c.ArrearsForRunId != null && !c.ArrearsSettled)
            .Join(hr.AttendanceDays.AsNoTracking(), c => c.AttendanceDayId, d => d.Id, (c, d) => new { c, d })
            .Where(x => x.d.EmployeeId == employee.Id)
            .ToListAsync(ct);
        if (arrears.Count > 0)
        {
            var def = components.FirstOrDefault(c => c.ComputedKind == ComputedComponent.Arrears);
            if (def is not null)
            {
                var amount = dayRate * arrears.Count(a => a.c.ToStatus == AttendanceStatus.Present);
                if (amount > 0)
                {
                    earnings.Add((def, amount));
                    lines.Add(Component(def, amount, $"{arrears.Count} correction(s) to a locked period"));
                }
            }
        }

        // --- provident fund, if and only if the employer configured one
        var pf = await policies.PfAsync(hr, branchId, periodEnd, ct);
        var employerCost = 0L;
        if (pf is not null && basicAmount > 0)
        {
            var serviceMonths = MonthsBetween(employee.JoinedOn, periodEnd);
            if (serviceMonths >= pf.EligibilityMonths)
            {
                var pfBase = earnings.Where(e => e.Def.PfApplicable).Sum(e => e.Amount);
                if (pfBase == 0) pfBase = basicAmount;

                var employeeShare = Taka.ApplyBp(pfBase, pf.EmployeeShareBp);
                if (pf.MonthlyCapTaka is { } cap && employeeShare > cap) employeeShare = cap;

                var def = components.FirstOrDefault(c => c.ComputedKind == ComputedComponent.ProvidentFund);
                if (def is not null && employeeShare > 0)
                    deductions.Add((def, employeeShare, $"{pf.EmployeeShareBp / 100m:0.##}% of {pfBase}"));

                employerCost += Taka.ApplyBp(pfBase, pf.EmployerShareBp);
            }
        }

        // --- income tax, if and only if the employer configured a slab table (P26)
        var slabs = await policies.TaxSlabsAsync(hr, branchId, periodEnd, null, ct);
        if (slabs.Count > 0)
        {
            var taxable = earnings.Where(e => e.Def.Taxable).Sum(e => e.Amount);
            var tax = ApplySlabs(taxable, slabs);
            var def = components.FirstOrDefault(c => c.ComputedKind == ComputedComponent.IncomeTax);
            if (def is not null && tax > 0)
                deductions.Add((def, tax, $"{slabs.Count}-band table effective {slabs[0].EffectiveFrom:dd MMM yyyy}"));
        }

        foreach (var (def, amount) in earnings)
            if (lines.All(l => l.ComponentId != def.Id))
                lines.Add(Component(def, amount, null));

        foreach (var (def, amount, basis) in deductions)
            lines.Add(Component(def, amount, basis));

        var gross = earnings.Sum(e => e.Amount);
        var totalDeductions = deductions.Sum(d => d.Amount);
        var net = gross - totalDeductions;

        // The negative-net floor: never hand someone a payslip that owes money. The shortfall is
        // carried, not forgiven, and Wave C's loan ledger is where it lands.
        if (net < policy.MinimumNetPayTaka)
        {
            line.CarriedShortfallTaka = policy.MinimumNetPayTaka - net;
            net = policy.MinimumNetPayTaka;
        }

        line.GrossEarningsTaka = gross;
        line.TotalDeductionsTaka = totalDeductions;
        line.NetPayTaka = net;
        line.EmployerCostTaka = employerCost;
        line.PolicyStampJson = JsonSerializer.Serialize(new
        {
            payrollPolicy = policy.Id,
            deduction = deductionRule?.Id,
            grace = graceRule?.Id,
            overtime = overtimeRule?.Id,
            pf = pf?.Id,
            taxSlabsFrom = slabs.Count > 0 ? slabs[0].EffectiveFrom : (DateOnly?)null,
            payStructure = structure.Id,
            dayCount = policy.DayCountConvention,
            denominator,
        });

        return new BuiltLine(line, lines, exceptions);

        PayrollComponentLine Component(PayComponent def, long amount, string? basis) => new()
        {
            ComponentId = def.Id,
            ComponentCode = def.Code,
            ComponentName = def.Name,
            Kind = def.Kind,
            AmountTaka = amount,
            DisplayOrder = def.DisplayOrder,
            Basis = basis,
        };
    }

    /// <summary>Applies a progressive band set. The engine knows the shape; the bands are data.</summary>
    public static long ApplySlabs(long taxable, IReadOnlyList<TaxSlab> slabs)
    {
        long tax = 0, floor = 0;
        foreach (var slab in slabs.OrderBy(s => s.Ordinal))
        {
            if (taxable <= floor) break;
            var ceiling = slab.UpToTaka <= 0 ? taxable : Math.Min(taxable, slab.UpToTaka);
            if (ceiling > floor) tax += Taka.ApplyBp(ceiling - floor, slab.RateBp);
            floor = slab.UpToTaka <= 0 ? taxable : slab.UpToTaka;
        }
        return tax;
    }

    public async Task ReviewAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var run = await LoadAsync(hr, runId, ct);
        Require(run, PayrollRunState.Generated, "reviewed");

        run.State = PayrollRunState.ExceptionsReviewed;
        run.ReviewedAt = clock.GetUtcNow();
        run.ReviewedBy = actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.payroll.review", "hr.payroll_run",
            run.Id, after: new { run.RunNo, run.ExceptionCount }, tier: 1);
    }

    /// <summary>§12: the lock is approved by Accounts Manager / MD, through the kernel engine.</summary>
    public async Task<RaiseResult> RequestApprovalAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId,
        long actorId, string actorName, string requesterRole, CancellationToken ct = default)
    {
        var run = await LoadAsync(hr, runId, ct);
        Require(run, PayrollRunState.ExceptionsReviewed, "sent for approval");

        var raise = await approvals.RaiseAsync(
            kernel, branchId, "payroll-lock", "hr.payroll_run", run.Id, actorId, requesterRole,
            $"Payroll {run.Period:MMMM yyyy} — {run.EmployeeCount} employees", run.TotalNetTaka, ct);

        run.ApprovalRequestId = raise.RequestId;
        if (raise.AutoApproved)
        {
            run.State = PayrollRunState.Approved;
            run.ApprovedAt = clock.GetUtcNow();
            run.ApprovedBy = actorId;
        }

        audit.Append(kernel, branchId, actorId, actorName, "hr.payroll.approval_requested",
            "hr.payroll_run", run.Id, after: new { run.RunNo, run.TotalNetTaka, raise.AutoApproved }, tier: 1);

        return raise;
    }

    public async Task ApproveAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var run = await LoadAsync(hr, runId, ct);
        Require(run, PayrollRunState.ExceptionsReviewed, "approved");

        run.State = PayrollRunState.Approved;
        run.ApprovedAt = clock.GetUtcNow();
        run.ApprovedBy = actorId;

        if (run.ApprovalRequestId is { } requestId)
            await approvals.DecideAsync(kernel, requestId, true, actorId, actorName, "payroll approved", ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.payroll.approve", "hr.payroll_run",
            run.Id, after: new { run.RunNo, run.TotalNetTaka }, tier: 1);
    }

    /// <summary>
    /// Locks the run and builds its journal. After this, the figures are history: the only way to
    /// change them is <see cref="ReverseAsync"/>.
    /// </summary>
    public async Task<PayrollJournal> LockAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var run = await LoadAsync(hr, runId, ct);
        Require(run, PayrollRunState.Approved, "locked");

        var journal = await BuildJournalAsync(hr, run, ct);
        if (!journal.IsBalanced)
            throw new HrException(
                $"The payroll journal does not balance ({journal.TotalDebit} vs {journal.TotalCredit}). "
                + "Nothing was locked — this is a bug, not a data-entry problem.");

        run.State = PayrollRunState.Locked;
        run.LockedAt = clock.GetUtcNow();
        run.LockedBy = actorId;
        run.JournalJson = JsonSerializer.Serialize(journal);

        var settled = await hr.AttendanceCorrections
            .Where(c => c.BranchId == branchId && c.ArrearsForRunId != null && !c.ArrearsSettled)
            .ToListAsync(ct);
        foreach (var c in settled) c.ArrearsSettled = true;

        audit.Append(kernel, branchId, actorId, actorName, "hr.payroll.lock", "hr.payroll_run",
            run.Id, after: new { run.RunNo, run.TotalNetTaka, journal.TotalDebit }, tier: 1);

        return journal;
    }

    /// <summary>
    /// The only way to undo a locked run: a mirrored run that references it. Nothing is deleted and
    /// nothing is edited, so the audit trail reads as what actually happened (hard rule 4).
    /// </summary>
    public async Task<PayrollRun> ReverseAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId, string reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new HrException("A reversal needs a reason — it is a permanent part of the record.");

        var original = await LoadAsync(hr, runId, ct);
        if (original.State is not (PayrollRunState.Locked or PayrollRunState.Posted))
            throw new HrException("Only a locked run needs reversing — an unlocked one can be cancelled.");

        var alreadyReversed = await hr.PayrollRuns.AsNoTracking()
            .AnyAsync(r => r.ReversalOfRunId == runId, ct);
        if (alreadyReversed)
            throw new HrException($"{original.RunNo} has already been reversed.");

        var (_, runNo) = await numbers.IssueAsync(
            kernel, branchId, "payroll", fiscal.FiscalYearOf(original.Period), "PR-{fy}-{n:D4}", ct);

        var reversal = new PayrollRun
        {
            BranchId = branchId,
            RunNo = runNo,
            Period = original.Period,
            Kind = PayrollRunKind.Reversal,
            Sequence = original.Sequence,
            State = PayrollRunState.Locked,
            ReversalOfRunId = original.Id,
            EmployeeCount = original.EmployeeCount,
            TotalGrossTaka = -original.TotalGrossTaka,
            TotalDeductionTaka = -original.TotalDeductionTaka,
            TotalNetTaka = -original.TotalNetTaka,
            TotalEmployerCostTaka = -original.TotalEmployerCostTaka,
            GeneratedAt = clock.GetUtcNow(),
            GeneratedBy = actorId,
            LockedAt = clock.GetUtcNow(),
            LockedBy = actorId,
        };
        hr.PayrollRuns.Add(reversal);
        await hr.SaveChangesAsync(ct);

        var originalLines = await hr.PayrollLines.AsNoTracking()
            .Where(l => l.RunId == original.Id).ToListAsync(ct);
        foreach (var l in originalLines)
        {
            hr.PayrollLines.Add(new PayrollLine
            {
                RunId = reversal.Id,
                BranchId = branchId,
                EmployeeId = l.EmployeeId,
                EmployeeCode = l.EmployeeCode,
                EmployeeName = l.EmployeeName,
                OrgUnitId = l.OrgUnitId,
                DesignationId = l.DesignationId,
                GradeId = l.GradeId,
                PayStructureId = l.PayStructureId,
                PolicyStampJson = l.PolicyStampJson,
                PeriodDays = l.PeriodDays,
                GrossEarningsTaka = -l.GrossEarningsTaka,
                TotalDeductionsTaka = -l.TotalDeductionsTaka,
                NetPayTaka = -l.NetPayTaka,
                EmployerCostTaka = -l.EmployerCostTaka,
                Note = $"Reversal of {original.RunNo}: {reason}",
            });
        }

        audit.Append(kernel, branchId, actorId, actorName, "hr.payroll.reverse", "hr.payroll_run",
            reversal.Id, before: new { original.RunNo, original.TotalNetTaka },
            after: new { reversal.RunNo, reason }, tier: 1);

        return reversal;
    }

    /// <summary>
    /// Hands the journal to whatever implements <see cref="IPayrollPosting"/>. §6.6 forbids an
    /// operational module writing ledger entries itself, and M15 does not exist yet — so the default
    /// implementation simply records that the journal was produced, and the data is ready when it does.
    /// </summary>
    public async Task PostAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId, IPayrollPosting posting,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var run = await LoadAsync(hr, runId, ct);
        Require(run, PayrollRunState.Locked, "posted");

        var journal = await BuildJournalAsync(hr, run, ct);
        await posting.PostAsync(journal, actorId, ct);

        run.State = PayrollRunState.Posted;
        run.PostedAt = clock.GetUtcNow();
        run.PostedBy = actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.payroll.post", "hr.payroll_run",
            run.Id, after: new { run.RunNo, journal.TotalDebit }, tier: 1);
    }

    public async Task<PayrollJournal> BuildJournalAsync(
        HrDbContext hr, PayrollRun run, CancellationToken ct = default)
    {
        var lines = await hr.PayrollLines.AsNoTracking()
            .Where(l => l.RunId == run.Id).ToListAsync(ct);

        var gross = lines.Sum(l => l.GrossEarningsTaka);
        var deductions = lines.Sum(l => l.TotalDeductionsTaka);
        var net = lines.Sum(l => l.NetPayTaka);
        var shortfall = lines.Sum(l => l.CarriedShortfallTaka);

        var journalLines = new List<PayrollJournalLine>
        {
            new("Salary & Wages", $"Payroll {run.Period:MMMM yyyy} gross", gross, 0, null),
        };
        if (deductions != 0)
            journalLines.Add(new("Payroll Deductions Payable", "Deductions withheld", 0, deductions, null));
        if (shortfall != 0)
            journalLines.Add(new("Employee Advances", "Carried shortfall", 0, -shortfall, null));
        journalLines.Add(new("Net Salary Payable", "Net payable to employees", 0, net - shortfall, null));

        return new PayrollJournal(run.Id, run.BranchId, run.Period, run.RunNo, journalLines);
    }

    private static void Require(PayrollRun run, string expected, string verb)
    {
        if (run.State != expected)
            throw new HrException(
                $"{run.RunNo} is {run.State.Replace('_', ' ')} — only a "
                + $"{expected.Replace('_', ' ')} run can be {verb}.");
    }

    private static async Task<PayrollRun> LoadAsync(HrDbContext hr, long runId, CancellationToken ct)
        => await hr.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
           ?? throw new HrException("That payroll run no longer exists.");

    /// <summary>Days in the period that are not holidays. Weekly offs vary per employee, so they are
    /// not netted here — the convention exists to give proration a stable denominator, not to
    /// re-derive attendance.</summary>
    private static async Task<int> WorkingDaysAsync(
        HrDbContext hr, long branchId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var holidays = await hr.Holidays.AsNoTracking()
            .Where(h => h.OnDate >= from && h.OnDate <= to)
            .CountAsync(ct);
        return Math.Max(1, (to.DayNumber - from.DayNumber + 1) - holidays);
    }

    private static int MonthsBetween(DateOnly from, DateOnly to)
        => ((to.Year - from.Year) * 12) + to.Month - from.Month - (to.Day < from.Day ? 1 : 0);
}
