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

        // Which of this period's leave days were on an UNPAID leave type (spec 0052 WP1). §5 M16
        // requires without-pay handling and the module modelled it — LeaveType.Paid was written by
        // the masters screen, seeded by the demo, and read by nothing, so a month of unpaid leave
        // paid a full month's salary. Resolved once for the whole run rather than per employee:
        // it is the same three-table join for all of them.
        var unpaidLeaveDays = await hr.AttendanceDays.AsNoTracking()
            .Where(d => d.OnDate >= period && d.OnDate <= periodEnd
                        && d.Status == AttendanceStatus.OnLeave && d.LeaveApplicationId != null)
            .Join(hr.LeaveApplications.AsNoTracking(),
                d => d.LeaveApplicationId, a => (long?)a.Id, (d, a) => new { d.EmployeeId, a.LeaveTypeId })
            .Join(hr.LeaveTypes.AsNoTracking().Where(t => !t.Paid),
                x => x.LeaveTypeId, t => t.Id, (x, _) => x.EmployeeId)
            .GroupBy(id => id)
            .Select(g => new { EmployeeId = g.Key, Days = g.Count() })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Days, ct);

        // Spec 0057 (G46): who is held this month. One query for the run rather than one per person.
        var holds = (await DisbursementService.LiveHoldsAsync(hr, branchId, ct))
            .ToDictionary(h => h.EmployeeId);

        long totalGross = 0, totalDeduction = 0, totalNet = 0, totalEmployerCost = 0;
        var exceptionCount = 0;
        var heldCount = 0;

        foreach (var employee in employees)
        {
            var line = await BuildLineAsync(
                hr, branchId, run, employee, period, periodEnd, policy, components,
                unpaidLeaveDays.GetValueOrDefault(employee.Id),
                holds.GetValueOrDefault(employee.Id), ct);
            hr.PayrollLines.Add(line.Line);
            await hr.SaveChangesAsync(ct);

            foreach (var cl in line.Components)
            {
                cl.PayrollLineId = line.Line.Id;
                cl.RunId = run.Id;
                hr.PayrollComponentLines.Add(cl);
            }

            // Spec 0057: the installments and the member-ledger movements this line produced.
            LoanService.Apply(hr, line.Recoveries, period, run.Id, clock.GetUtcNow());
            foreach (var entry in line.Ledger)
            {
                entry.PayrollRunId = run.Id;
                hr.LedgerEntries.Add(entry);
            }
            if (line.Line.Held) heldCount++;

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
            run.Id, after: new { runNo, period, employees.Count, totalNet, exceptionCount, heldCount },
            tier: 1);

        return run;
    }

    /// <summary>
    /// What one employee's line produced. Spec 0057 adds two things the engine used to compute and
    /// then throw away: the loan installments it recovered, and the member-ledger movements behind
    /// the provident-fund and income-tax deductions it had always been putting on the payslip.
    /// </summary>
    private sealed record BuiltLine(
        PayrollLine Line, List<PayrollComponentLine> Components, int Exceptions,
        IReadOnlyList<LoanService.Recovery> Recoveries,
        IReadOnlyList<EmployeeLedgerEntry> Ledger);

    private async Task<BuiltLine> BuildLineAsync(
        HrDbContext hr, long branchId, PayrollRun run, Employee employee,
        DateOnly period, DateOnly periodEnd, PayrollPolicy policy,
        IReadOnlyList<PayComponent> components, int unpaidLeaveDays, SalaryHold? hold,
        CancellationToken ct)
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
            return new BuiltLine(line, lines, 1, [], []);
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
        // The unpaid subset of LeaveDaysBp, not a separate count — see the property's own note.
        line.LeaveWithoutPayDaysBp = unpaidLeaveDays * 10000;
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
            // A component the structure does not configure is not paid — for every calc method.
            // PercentOf used to be exempt here, which paid a percent-of allowance to every
            // employee whose structure omitted it, computed off whatever base WAS configured
            // (AUD-M16-05: 6,000 Tk of HRA from nothing, per run, per unconfigured employee).
            if (!byId.TryGetValue(def.Id, out var configured))
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

        // Leave without pay (spec 0052 WP1, §5 M16 `[M]`). Same shape as the absence deduction
        // above, and the same ADR-0027 discipline: the employer's rate or nothing. When there are
        // unpaid days and no way to price them the line says so, because silently paying a full
        // month for a month of unpaid leave is what this fix exists to stop.
        if (line.LeaveWithoutPayDaysBp > 0)
        {
            var def = components.FirstOrDefault(
                c => c.ComputedKind == ComputedComponent.LeaveWithoutPay);
            var unpaidDays = line.LeaveWithoutPayDaysBp / 10000;

            if (deductionRule is null || def is null)
            {
                line.Note = Append(line.Note,
                    $"{unpaidDays} unpaid leave day(s) were not deducted: "
                    + (deductionRule is null
                        ? "no unpaid-leave deduction rule is configured for this period."
                        : "no leave-without-pay component exists on this branch."));
            }
            else
            {
                var amount = Taka.ApplyBp(
                    dayRate * unpaidDays, deductionRule.PerLeaveWithoutPayDayBp);
                if (amount > 0)
                    deductions.Add((def, amount, $"{unpaidDays} unpaid leave day(s)"));
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
                // Multiply first, divide once, round once at the end (C3). The old code
                // integer-divided the day rate to a whole-taka per-MINUTE rate before the
                // multiplier: 29% underpayment at a 680 Tk day rate, and zero — however many
                // hours were worked — below 480 Tk/day, i.e. most of a Bangladeshi hospital's
                // staff on the Fixed30 convention (AUD-M16-07).
                var amount = Taka.RoundHalfUp(
                    (decimal)dayRate * minutes * overtimeRule.MultiplierBp / (8 * 60 * Taka.Bp));
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

        // --- loan recovery (spec 0057, G41). Computed against the room above the employer's floor,
        // so an installment that would breach it is DEFERRED rather than skipped: a skipped
        // installment is invisible, a deferred one is a row the outstanding statement explains.
        var loans = await LoanService.LiveLoansAsync(hr, employee.Id, ct);
        var recoveries = LoanService.Plan(loans, period, net, policy.MinimumNetPayTaka);

        foreach (var recovery in recoveries.Where(r => !r.Deferred && r.AmountTaka > 0))
        {
            var def = components.FirstOrDefault(c => c.ComputedKind == ComputedComponent.LoanInstallment);
            if (def is null) break;                 // no component configured: nothing to show it on
            deductions.Add((def, recovery.AmountTaka, recovery.Basis));
            totalDeductions += recovery.AmountTaka;
            net -= recovery.AmountTaka;
            lines.Add(Component(def, recovery.AmountTaka, recovery.Basis));
        }

        foreach (var deferred in recoveries.Where(r => r.Deferred))
            line.Note = Append(line.Note, deferred.Basis + ".");

        // --- the member ledgers (spec 0057, G42/G43/G44). The engine has always computed the PF and
        // tax deductions and put them on the payslip; it never told the member's balance, so the
        // money was taken every month and the balance it was taken into did not exist.
        var ledger = new List<EmployeeLedgerEntry>();

        if (pf is not null)
        {
            var employeeShare = deductions
                .Where(d => d.Def.ComputedKind == ComputedComponent.ProvidentFund)
                .Sum(d => d.Amount);
            var employerShare = employerCost;
            if (employeeShare > 0 || employerShare > 0)
            {
                ledger.Add(new EmployeeLedgerEntry
                {
                    BranchId = branchId,
                    EmployeeId = employee.Id,
                    Kind = LedgerKind.ProvidentFund,
                    OnDate = periodEnd,
                    Narration = $"Contribution for {period:MMMM yyyy}",
                    EmployeeShareTaka = employeeShare,
                    EmployerShareTaka = employerShare,
                    RecordedAt = clock.GetUtcNow(),
                    RecordedBy = run.GeneratedBy,
                });
            }
        }

        var withheld = deductions
            .Where(d => d.Def.ComputedKind == ComputedComponent.IncomeTax)
            .Sum(d => d.Amount);
        if (withheld > 0)
        {
            ledger.Add(new EmployeeLedgerEntry
            {
                BranchId = branchId,
                EmployeeId = employee.Id,
                Kind = LedgerKind.Tax,
                OnDate = periodEnd,
                Narration = $"Tax deducted at source for {period:MMMM yyyy}",
                EmployeeShareTaka = withheld,
                RecordedAt = clock.GetUtcNow(),
                RecordedBy = run.GeneratedBy,
            });
        }

        // The negative-net floor: never hand someone a payslip that owes money. The shortfall is
        // carried, not forgiven, and the loan ledger is where it lands.
        if (net < policy.MinimumNetPayTaka)
        {
            line.CarriedShortfallTaka = policy.MinimumNetPayTaka - net;
            net = policy.MinimumNetPayTaka;
        }

        // --- salary hold (spec 0057, G46). The line is computed in full and MARKED; the money is
        // simply not disbursed.
        //
        // Zeroing the net was the first attempt and it was wrong twice over. It breaks
        // ck_payroll_line_net, which requires the arithmetic to add up — and the constraint is
        // right, because the person did earn it. A hold withholds payment; it does not un-earn a
        // month's work, and a payslip showing zero would tell the employee something false about
        // what they are owed. The disbursement batch skips held lines instead.
        if (hold is not null)
        {
            line.Held = true;
            line.HoldReason = hold.Reason;
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

        return new BuiltLine(line, lines, exceptions, recoveries, ledger);

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

    /// <summary>Adds a sentence to a line's note without losing whatever was already there.</summary>
    private static string Append(string? note, string sentence)
        => string.IsNullOrWhiteSpace(note) ? sentence : $"{note} {sentence}";

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

        // Spec 0057: the comparison is built here rather than on demand, so the variance tab opens
        // on a fixed set of figures. Rebuilding it later against a run that has since moved would
        // quietly change what an approver had already looked at.
        await DisbursementService.BuildVarianceAsync(hr, branchId, runId, ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.payroll.review", "hr.payroll_run",
            run.Id, after: new { run.RunNo, run.ExceptionCount }, tier: 1);
    }

    /// <summary>
    /// §9's Variance Reviewed, between exceptions and approval (spec 0057, G47). The module used to
    /// go straight from one to the other, so a run where somebody's pay tripled was approved exactly
    /// as fast as a run where nothing moved.
    /// <para>
    /// Refused while any line outside the employer's tolerance is still unexplained. The tolerance
    /// is the employer's — a small employer may reasonably want every change explained, a large one
    /// would drown.
    /// </para>
    /// </summary>
    public async Task ReviewVarianceAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId,
        long actorId, string actorName, CancellationToken ct = default)
    {
        var run = await LoadAsync(hr, runId, ct);
        Require(run, PayrollRunState.ExceptionsReviewed, "variance-reviewed");

        var periodEnd = run.Period.AddMonths(1).AddDays(-1);
        var policy = await policies.PayrollAsync(hr, branchId, periodEnd, ct);
        var tolerance = policy?.VarianceTolerationBp ?? 2_000;

        var outstanding = await DisbursementService.OutstandingAsync(hr, runId, tolerance, ct);
        if (outstanding.Count > 0)
        {
            throw new HrException(
                $"{outstanding.Count} pay change{(outstanding.Count == 1 ? "" : "s")} outside the "
                + $"{tolerance / 100m:0.##}% tolerance still needs a reason. Explain them on the "
                + "variance tab before approving.");
        }

        run.State = PayrollRunState.VarianceReviewed;
        run.VarianceReviewedAt = clock.GetUtcNow();
        run.VarianceReviewedBy = actorId;

        audit.Append(kernel, branchId, actorId, actorName, "hr.payroll.variance_review",
            "hr.payroll_run", run.Id, after: new { run.RunNo, tolerance }, tier: 1);
    }

    /// <summary>§12: the lock is approved by Accounts Manager / MD, through the kernel engine.</summary>
    public async Task<RaiseResult> RequestApprovalAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long runId,
        long actorId, string actorName, string requesterRole, CancellationToken ct = default)
    {
        var run = await LoadAsync(hr, runId, ct);
        // Spec 0057: variance comes first. §9 puts it between exceptions and approval precisely so
        // that nobody can approve a month they have not compared with the last one.
        Require(run, PayrollRunState.VarianceReviewed, "sent for approval");

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
        Require(run, PayrollRunState.VarianceReviewed, "approved");

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

        await RecordShortfallsAsync(hr, run, sign: -1, actorId,
            $"Minimum-net-pay shortfall carried from {run.RunNo}", ct);

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
                // The shortfall mirrors with everything else, or ck_payroll_line_net refuses the
                // row: the identity is net = gross − deductions + shortfall, and negating two of
                // the three terms while dropping the fourth does not satisfy it. Left out until
                // spec 0052, which means a run carrying any floored line could be locked (0039
                // fixed that half, AUD-M16-04) and then never reversed — the one correction hard
                // rule 4 allows was the one it refused.
                CarriedShortfallTaka = -l.CarriedShortfallTaka,
                EmployerCostTaka = -l.EmployerCostTaka,
                Note = $"Reversal of {original.RunNo}: {reason}",
            });
        }

        // The shortfall the original booked as a debt is given back, as a mirroring entry rather
        // than a deletion — the ledger is append-only and hard rule 4 has no delete in it.
        await RecordShortfallsAsync(hr, original, sign: +1, actorId,
            $"Shortfall reversed by {reversal.RunNo}: {reason}", ct, runId: reversal.Id);

        audit.Append(kernel, branchId, actorId, actorName, "hr.payroll.reverse", "hr.payroll_run",
            reversal.Id, before: new { original.RunNo, original.TotalNetTaka },
            after: new { reversal.RunNo, reason }, tier: 1);

        return reversal;
    }

    /// <summary>
    /// Writes one <see cref="EmployeeLedgerEntry"/> per line the minimum-net-pay floor lifted
    /// (spec 0052 WP2).
    /// <para>
    /// <see cref="BuildJournalAsync"/> debits <em>Employee Advances</em> for these — an asset, money
    /// the employer is owed. Nothing recorded that debt before 0052: the ledger tables had no writer
    /// at all, so the journal asserted a receivable the system had no record of and would never
    /// collect. A negative <see cref="EmployeeLedgerEntry.EmployeeShareTaka"/> debits the member,
    /// per that column's own contract.
    /// </para>
    /// <para>
    /// Written at <b>lock</b>, not at generate: a generated run is a draft that may be regenerated
    /// or cancelled, and a draft must not move an employee's ledger.
    /// </para>
    /// </summary>
    private async Task RecordShortfallsAsync(
        HrDbContext hr, PayrollRun run, int sign, long actorId, string narration,
        CancellationToken ct, long? runId = null)
    {
        var floored = await hr.PayrollLines.AsNoTracking()
            .Where(l => l.RunId == run.Id && l.CarriedShortfallTaka != 0)
            .Select(l => new { l.EmployeeId, l.CarriedShortfallTaka })
            .ToListAsync(ct);

        foreach (var line in floored)
            hr.LedgerEntries.Add(new EmployeeLedgerEntry
            {
                BranchId = run.BranchId,
                EmployeeId = line.EmployeeId,
                Kind = LedgerKind.Advance,
                OnDate = run.Period,
                Narration = narration,
                EmployeeShareTaka = sign * line.CarriedShortfallTaka,
                PayrollRunId = runId ?? run.Id,
                RecordedAt = clock.GetUtcNow(),
                RecordedBy = actorId,
            });
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

        // Posting is when the figures become the employee's: issue one numbered payslip per line
        // (PRD §5 M16 [M] "pay slips", AUD-M16-09). A reversal run mirrors money, not documents —
        // the original payslip stays on record and the reversal run explains itself.
        if (run.Kind != PayrollRunKind.Reversal)
        {
            var lines = await hr.PayrollLines.AsNoTracking()
                .Where(l => l.RunId == run.Id)
                .Select(l => new { l.Id, l.EmployeeId })
                .ToListAsync(ct);
            var issuedAt = clock.GetUtcNow();
            foreach (var line in lines)
            {
                var (_, slipNo) = await numbers.IssueAsync(
                    kernel, branchId, "payslip", fiscal.FiscalYearOf(run.Period), "PS-{fy}-{n:D5}", ct);
                hr.Payslips.Add(new Payslip
                {
                    BranchId = branchId,
                    PayrollLineId = line.Id,
                    EmployeeId = line.EmployeeId,
                    PayslipNo = slipNo,
                    IssuedAt = issuedAt,
                    IssuedBy = actorId,
                });
            }
        }

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

        // The algebra, so nobody unbalances it again (AUD-M16-04). A floored line satisfies
        // net = gross − deductions + shortfall (ck_payroll_line_net). The shortfall is money the
        // employer pays out beyond what the period earned — an advance recoverable from the
        // employee, so it is a DEBIT (an asset), and the full net stays payable:
        //   debit  = gross + shortfall
        //   credit = deductions + net = deductions + (gross − deductions + shortfall)
        // Both sides equal gross + shortfall. The previous shape credited −shortfall AND paid
        // net − shortfall, counting it twice; every run with one floored employee refused to lock.
        var journalLines = new List<PayrollJournalLine>
        {
            new("Salary & Wages", $"Payroll {run.Period:MMMM yyyy} gross", gross, 0, null),
        };
        if (shortfall != 0)
            journalLines.Add(new("Employee Advances", "Carried shortfall (recoverable)", shortfall, 0, null));
        if (deductions != 0)
            journalLines.Add(new("Payroll Deductions Payable", "Deductions withheld", 0, deductions, null));
        journalLines.Add(new("Net Salary Payable", "Net payable to employees", 0, net, null));

        return new PayrollJournal(run.Id, run.BranchId, run.Period, run.RunNo, journalLines);
    }

    private static void Require(PayrollRun run, string expected, string verb)
    {
        if (run.State != expected)
            throw new HrException(
                $"{run.RunNo} is {run.State.Replace('_', ' ')} — only a "
                + $"{expected.Replace('_', ' ')} run can be {verb}.");
    }

    /// <summary>
    /// Loads a run for a state transition, under a row lock (spec 0052 WP3).
    /// <para>
    /// The lock is the guard on the state machine, and until 0052 there was none — no
    /// <c>FOR UPDATE</c> anywhere in this module, and no concurrency token on the run. Two
    /// operators pressing Lock together both read <c>approved</c>, both proceeded, and both wrote
    /// a <c>hr.payroll.lock</c> audit row: an audit trail asserting something that did not happen,
    /// against hard rule 4. Postgres serialises them here, and the loser re-reads the state the
    /// winner wrote, so <see cref="Require"/> refuses it in a sentence instead of a database error.
    /// </para>
    /// <para>
    /// The lock is taken by id alone; the entity still comes from the branch-filtered set, so a run
    /// in another branch is locked briefly and then reported as not existing — never returned.
    /// </para>
    /// </summary>
    private static async Task<PayrollRun> LoadAsync(HrDbContext hr, long runId, CancellationToken ct)
    {
        var locked = await hr.Database.SqlQuery<long>($"""
            SELECT id AS "Value" FROM hr.payroll_run WHERE id = {runId} FOR UPDATE
            """).ToListAsync(ct);
        if (locked.Count == 0) throw new HrException("That payroll run no longer exists.");

        return await hr.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
               ?? throw new HrException("That payroll run no longer exists.");
    }

    /// <summary>Days in the period that are not holidays. Weekly offs vary per employee, so they are
    /// not netted here — the convention exists to give proration a stable denominator, not to
    /// re-derive attendance.</summary>
    private static async Task<int> WorkingDaysAsync(
        HrDbContext hr, long branchId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        // AUD-M16-06: only this branch's calendars count — another branch's holiday must not
        // shrink this branch's denominator and inflate its day rate.
        var holidays = await hr.Holidays.AsNoTracking()
            .Where(h => h.OnDate >= from && h.OnDate <= to && hr.HolidayCalendars.Any(
                c => c.Id == h.CalendarId && c.BranchId == branchId && c.Active))
            .CountAsync(ct);
        return Math.Max(1, (to.DayNumber - from.DayNumber + 1) - holidays);
    }

    private static int MonthsBetween(DateOnly from, DateOnly to)
        => ((to.Year - from.Year) * 12) + to.Month - from.Month - (to.Day < from.Day ? 1 : 0);
}
