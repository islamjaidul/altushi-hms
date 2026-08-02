using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>
/// Resolves the effective-dated policy rows that were in force on a given date (ADR-0027) — the same
/// job <c>RateResolver</c> does for prices, and for the same reason: hard rule 5 says a historical
/// document must reproduce its historical numbers.
/// <para>
/// Every method here can legitimately return null. An employer who has not configured a tax table
/// has no tax table, and the honest response is a payroll run that refuses to compute income tax and
/// says which policy is missing — not one that quietly computes zero, and certainly not one that
/// falls back on a rate we invented.
/// </para>
/// <para>
/// The <c>Set*</c> methods are the write path the audit found missing (AUD-M16-01): six policy
/// tables read by the engine and written by nothing. Each write effective-dates in the shape
/// <c>EmployeeService.SetPayAsync</c> established — the open row closes the day before, the new one
/// opens, and the database's exclusion constraints refuse an overlap outright. Saving twice on the
/// same day amends that day's row in place (the operator correcting a typo), exactly as the payroll
/// policy screen already does. Every write is tier-1 audited with the actor's id and name.
/// </para>
/// </summary>
public sealed class PolicyResolver(AuditWriter audit, TimeProvider clock)
{
    public Task<PayrollPolicy?> PayrollAsync(HrDbContext hr, long branchId, DateOnly on, CancellationToken ct = default)
        => hr.PayrollPolicies.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.EffectiveFrom <= on
                        && (p.EffectiveTo == null || p.EffectiveTo >= on))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    public Task<PfPolicy?> PfAsync(HrDbContext hr, long branchId, DateOnly on, CancellationToken ct = default)
        => hr.PfPolicies.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.EffectiveFrom <= on
                        && (p.EffectiveTo == null || p.EffectiveTo >= on))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    public Task<DeductionRule?> DeductionAsync(HrDbContext hr, long branchId, DateOnly on, CancellationToken ct = default)
        => hr.DeductionRules.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.EffectiveFrom <= on
                        && (p.EffectiveTo == null || p.EffectiveTo >= on))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    public Task<GraceTimeRule?> GraceTimeAsync(HrDbContext hr, long branchId, DateOnly on, CancellationToken ct = default)
        => hr.GraceTimeRules.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.EffectiveFrom <= on
                        && (p.EffectiveTo == null || p.EffectiveTo >= on))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    public Task<HolidayPayPolicy?> HolidayPayAsync(HrDbContext hr, long branchId, DateOnly on, CancellationToken ct = default)
        => hr.HolidayPayPolicies.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.EffectiveFrom <= on
                        && (p.EffectiveTo == null || p.EffectiveTo >= on))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    /// <summary>Grade-specific rule wins over the branch-wide one; neither is invented if absent.</summary>
    public async Task<OvertimeRule?> OvertimeAsync(
        HrDbContext hr, long branchId, long? gradeId, DateOnly on, CancellationToken ct = default)
    {
        var candidates = await hr.OvertimeRules.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.EffectiveFrom <= on
                        && (p.EffectiveTo == null || p.EffectiveTo >= on)
                        && (p.GradeId == null || p.GradeId == gradeId))
            .OrderByDescending(p => p.GradeId != null)          // specific before general
            .ThenByDescending(p => p.EffectiveFrom)
            .ToListAsync(ct);
        return candidates.FirstOrDefault();
    }

    public async Task<IReadOnlyList<TaxSlab>> TaxSlabsAsync(
        HrDbContext hr, long branchId, DateOnly on, string? category = null, CancellationToken ct = default)
    {
        var effectiveFrom = await hr.TaxSlabs.AsNoTracking()
            .Where(s => s.BranchId == branchId && s.EffectiveFrom <= on
                        && (s.EffectiveTo == null || s.EffectiveTo >= on)
                        && s.Category == category)
            .MaxAsync(s => (DateOnly?)s.EffectiveFrom, ct);

        if (effectiveFrom is null) return [];

        return await hr.TaxSlabs.AsNoTracking()
            .Where(s => s.BranchId == branchId && s.EffectiveFrom == effectiveFrom && s.Category == category)
            .OrderBy(s => s.Ordinal)
            .ToListAsync(ct);
    }

    public async Task<LeavePolicy?> LeaveAsync(
        HrDbContext hr, long branchId, long leaveTypeId, long? gradeId, DateOnly on,
        CancellationToken ct = default)
    {
        var candidates = await hr.LeavePolicies.AsNoTracking()
            .Where(p => p.BranchId == branchId && p.LeaveTypeId == leaveTypeId
                        && p.EffectiveFrom <= on && (p.EffectiveTo == null || p.EffectiveTo >= on)
                        && (p.GradeId == null || p.GradeId == gradeId))
            .OrderByDescending(p => p.GradeId != null)
            .ThenByDescending(p => p.EffectiveFrom)
            .ToListAsync(ct);
        return candidates.FirstOrDefault();
    }

    public Task<EmployeePayStructure?> PayStructureAsync(
        HrDbContext hr, long employeeId, DateOnly on, CancellationToken ct = default)
        => hr.PayStructures.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.EffectiveFrom <= on
                        && (p.EffectiveTo == null || p.EffectiveTo >= on))
            .OrderByDescending(p => p.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    public Task<EmployeeAssignment?> AssignmentAsync(
        HrDbContext hr, long employeeId, DateOnly on, CancellationToken ct = default)
        => hr.Assignments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.EffectiveFrom <= on
                        && (a.EffectiveTo == null || a.EffectiveTo >= on))
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    // ------------------------------------------------------------------ the write path

    public async Task<DeductionRule> SetDeductionAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, DateOnly effectiveFrom,
        int perAbsentDayBp, int perLeaveWithoutPayDayBp,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (perAbsentDayBp is < 0 or > 30_000 || perLeaveWithoutPayDayBp is < 0 or > 30_000)
            throw new HrException("A per-day deduction is basis points of a day rate: 0 to 30000 (10000 = one full day).");

        var row = await OpenOrNewAsync(hr.DeductionRules, r => r.BranchId == branchId,
            r => r.EffectiveFrom, r => r.EffectiveTo, (r, d) => r.EffectiveTo = d, effectiveFrom,
            () => new DeductionRule
            {
                BranchId = branchId, EffectiveFrom = effectiveFrom,
                CreatedAt = clock.GetUtcNow(), CreatedBy = actorId,
            }, hr.DeductionRules.Add, ct);
        row.PerAbsentDayBp = perAbsentDayBp;
        row.PerLeaveWithoutPayDayBp = perLeaveWithoutPayDayBp;
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.policy.deduction.set",
            "hr.deduction_rule", row.Id,
            after: new { effectiveFrom, perAbsentDayBp, perLeaveWithoutPayDayBp }, tier: 1);
        return row;
    }

    public async Task<GraceTimeRule> SetGraceTimeAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, DateOnly effectiveFrom,
        int graceMinutes, int freeLateCountPerMonth, int deductionPerLateBp,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (graceMinutes is < 0 or > 240)
            throw new HrException("Grace minutes must be between 0 and 240.");
        if (freeLateCountPerMonth is < 0 or > 31)
            throw new HrException("Free late arrivals per month must be between 0 and 31.");
        if (deductionPerLateBp is < 0 or > 30_000)
            throw new HrException("The per-late deduction is basis points of a day rate: 0 to 30000.");

        var row = await OpenOrNewAsync(hr.GraceTimeRules, r => r.BranchId == branchId,
            r => r.EffectiveFrom, r => r.EffectiveTo, (r, d) => r.EffectiveTo = d, effectiveFrom,
            () => new GraceTimeRule
            {
                BranchId = branchId, EffectiveFrom = effectiveFrom,
                CreatedAt = clock.GetUtcNow(), CreatedBy = actorId,
            }, hr.GraceTimeRules.Add, ct);
        row.GraceMinutes = graceMinutes;
        row.FreeLateCountPerMonth = freeLateCountPerMonth;
        row.DeductionPerLateBp = deductionPerLateBp;
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.policy.grace.set",
            "hr.grace_time_rule", row.Id,
            after: new { effectiveFrom, graceMinutes, freeLateCountPerMonth, deductionPerLateBp }, tier: 1);
        return row;
    }

    /// <summary>Writes the branch-wide rule (GradeId null). Grade-specific rules are §5A-16 scope.</summary>
    public async Task<OvertimeRule> SetOvertimeAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, DateOnly effectiveFrom,
        int thresholdMinutes, long multiplierBp, int maxMinutesPerMonth, bool bankInsteadOfPay,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (thresholdMinutes is < 0 or > 480)
            throw new HrException("The overtime threshold must be between 0 and 480 minutes.");
        if (multiplierBp is < 0 or > 50_000)
            throw new HrException("The overtime multiplier is basis points: 0 to 50000 (20000 = 2.0x).");
        if (maxMinutesPerMonth is < 0 or > 44_640)
            throw new HrException("The monthly overtime cap must be between 0 (no cap) and 44640 minutes.");

        // No exclusion constraint spans the nullable grade scope, so the overlap is closed here:
        // the open branch-wide rule ends the day before the new one starts.
        var open = await hr.OvertimeRules
            .Where(r => r.BranchId == branchId && r.GradeId == null && r.EffectiveTo == null)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        OvertimeRule row;
        if (open is not null && open.EffectiveFrom == effectiveFrom)
        {
            row = open;
        }
        else
        {
            if (open is not null)
            {
                if (open.EffectiveFrom > effectiveFrom)
                    throw new HrException(
                        "An overtime rule already starts later — change its dates instead of adding another.");
                open.EffectiveTo = effectiveFrom.AddDays(-1);
            }
            row = new OvertimeRule
            {
                BranchId = branchId, EffectiveFrom = effectiveFrom,
                CreatedAt = clock.GetUtcNow(), CreatedBy = actorId,
            };
            hr.OvertimeRules.Add(row);
        }
        row.ThresholdMinutes = thresholdMinutes;
        row.MultiplierBp = multiplierBp;
        row.MaxMinutesPerMonth = maxMinutesPerMonth;
        row.BankInsteadOfPay = bankInsteadOfPay;
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.policy.overtime.set",
            "hr.overtime_rule", row.Id,
            after: new { effectiveFrom, thresholdMinutes, multiplierBp, maxMinutesPerMonth, bankInsteadOfPay },
            tier: 1);
        return row;
    }

    public async Task<PfPolicy> SetPfAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, DateOnly effectiveFrom,
        long employeeShareBp, long employerShareBp, int eligibilityMonths, long? monthlyCapTaka,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (employeeShareBp is < 0 or > 10_000 || employerShareBp is < 0 or > 10_000)
            throw new HrException("A provident-fund share is basis points: 0 to 10000 (800 = 8%).");
        if (eligibilityMonths is < 0 or > 120)
            throw new HrException("Eligibility months must be between 0 and 120.");
        if (monthlyCapTaka is < 0)
            throw new HrException("The monthly cap cannot be negative.");

        var row = await OpenOrNewAsync(hr.PfPolicies, r => r.BranchId == branchId,
            r => r.EffectiveFrom, r => r.EffectiveTo, (r, d) => r.EffectiveTo = d, effectiveFrom,
            () => new PfPolicy
            {
                BranchId = branchId, EffectiveFrom = effectiveFrom,
                CreatedAt = clock.GetUtcNow(), CreatedBy = actorId,
            }, hr.PfPolicies.Add, ct);
        row.EmployeeShareBp = employeeShareBp;
        row.EmployerShareBp = employerShareBp;
        row.EligibilityMonths = eligibilityMonths;
        row.MonthlyCapTaka = monthlyCapTaka;
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.policy.pf.set",
            "hr.pf_policy", row.Id,
            after: new { effectiveFrom, employeeShareBp, employerShareBp, eligibilityMonths, monthlyCapTaka },
            tier: 1);
        return row;
    }

    public async Task<GratuityRule> SetGratuityAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, DateOnly effectiveFrom,
        int minimumServiceMonths, int daysPerYearBp,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (minimumServiceMonths is < 0 or > 240)
            throw new HrException("Minimum service months must be between 0 and 240.");
        if (daysPerYearBp is < 0 or > 1_000_000)
            throw new HrException("Days per year is basis points of a day: 0 to 1000000 (300000 = 30 days).");

        var row = await OpenOrNewAsync(hr.GratuityRules, r => r.BranchId == branchId,
            r => r.EffectiveFrom, r => r.EffectiveTo, (r, d) => r.EffectiveTo = d, effectiveFrom,
            () => new GratuityRule
            {
                BranchId = branchId, EffectiveFrom = effectiveFrom,
                CreatedAt = clock.GetUtcNow(), CreatedBy = actorId,
            }, hr.GratuityRules.Add, ct);
        row.MinimumServiceMonths = minimumServiceMonths;
        row.DaysPerYearBp = daysPerYearBp;
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.policy.gratuity.set",
            "hr.gratuity_rule", row.Id,
            after: new { effectiveFrom, minimumServiceMonths, daysPerYearBp }, tier: 1);
        return row;
    }

    /// <summary>
    /// Replaces the whole progressive band set from a date. Bands are a set, not single rows —
    /// the resolver loads every band sharing the newest effective date, so a partial write would
    /// silently orphan the rest of the table. Ceilings must ascend; a final band with
    /// <c>upToTaka = 0</c> means "everything above".
    /// </summary>
    public async Task SetTaxSlabsAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, DateOnly effectiveFrom,
        IReadOnlyList<(long UpToTaka, long RateBp)> bands,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (bands.Count == 0)
            throw new HrException("A tax table needs at least one band.");
        for (var i = 0; i < bands.Count; i++)
        {
            if (bands[i].RateBp is < 0 or > 10_000)
                throw new HrException("A tax rate is basis points: 0 to 10000 (500 = 5%).");
            if (bands[i].UpToTaka < 0)
                throw new HrException("A band ceiling cannot be negative.");
            if (bands[i].UpToTaka == 0 && i != bands.Count - 1)
                throw new HrException("Only the last band may leave its ceiling open (0).");
            if (i > 0 && bands[i].UpToTaka != 0 && bands[i].UpToTaka <= bands[i - 1].UpToTaka)
                throw new HrException("Band ceilings must increase from one band to the next.");
        }

        // Same-day resave is the operator correcting a typo: this day's set is replaced. Any
        // older set is closed the day before, so history keeps resolving what it resolved.
        var sameDay = await hr.TaxSlabs
            .Where(s => s.BranchId == branchId && s.EffectiveFrom == effectiveFrom && s.Category == null)
            .ToListAsync(ct);
        hr.TaxSlabs.RemoveRange(sameDay);

        var open = await hr.TaxSlabs
            .Where(s => s.BranchId == branchId && s.Category == null
                        && s.EffectiveFrom < effectiveFrom && s.EffectiveTo == null)
            .ToListAsync(ct);
        foreach (var s in open) s.EffectiveTo = effectiveFrom.AddDays(-1);

        for (var i = 0; i < bands.Count; i++)
            hr.TaxSlabs.Add(new TaxSlab
            {
                BranchId = branchId,
                EffectiveFrom = effectiveFrom,
                Ordinal = i + 1,
                UpToTaka = bands[i].UpToTaka,
                RateBp = bands[i].RateBp,
                CreatedAt = clock.GetUtcNow(),
                CreatedBy = actorId,
            });
        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, "hr.policy.tax.set",
            "hr.tax_slab", 0,
            after: new { effectiveFrom, bands = bands.Select(b => new { b.UpToTaka, b.RateBp }) }, tier: 1);
    }

    /// <summary>
    /// The one effective-dating shape all single-row-per-date policies share: same-day save amends
    /// in place, a future-dated row refuses, otherwise the open row closes the day before and a new
    /// one opens. The GiST exclusion constraints remain the final arbiter of overlap.
    /// </summary>
    private static async Task<T> OpenOrNewAsync<T>(
        IQueryable<T> set, System.Linq.Expressions.Expression<Func<T, bool>> scope,
        Func<T, DateOnly> from, Func<T, DateOnly?> to, Action<T, DateOnly?> close,
        DateOnly effectiveFrom, Func<T> create, Func<T, Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<T>> add,
        CancellationToken ct) where T : class
    {
        var candidates = await set.Where(scope).ToListAsync(ct);
        var open = candidates.Where(r => to(r) == null).OrderByDescending(from).FirstOrDefault();

        if (open is not null && from(open) == effectiveFrom) return open;
        if (open is not null)
        {
            if (from(open) > effectiveFrom)
                throw new HrException(
                    "A rule already starts later — change its dates instead of adding another.");
            close(open, effectiveFrom.AddDays(-1));
        }
        var row = create();
        add(row);
        return row;
    }
}
