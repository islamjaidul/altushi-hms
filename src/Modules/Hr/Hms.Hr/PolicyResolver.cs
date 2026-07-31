using Hms.Hr.Data;
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
/// </summary>
public sealed class PolicyResolver
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
}
