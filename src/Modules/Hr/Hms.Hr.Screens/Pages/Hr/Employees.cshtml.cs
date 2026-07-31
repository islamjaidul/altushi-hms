using Hms.Hr.Data;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Ui.Pages.Hr;

public sealed record EmployeeRow(
    long Id, string Code, string Name, string? Unit, string? Designation,
    DateOnly JoinedOn, string Status, string? Phone);

/// <summary>
/// The employee directory. Guarded by <c>hr.read</c>, which deliberately does not include pay —
/// a department head browsing the directory sees who works here, not what they earn.
/// </summary>
[Authorize(Policy = HrPerm.Read)]
public class EmployeesModel(IHrTx tx) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public bool IncludeLeavers { get; set; }

    public IReadOnlyList<EmployeeRow> Rows { get; private set; } = [];
    public int Total { get; private set; }

    public async Task OnGetAsync()
    {
        var term = Q?.Trim();

        await tx.RunAsync(async s =>
        {
            var query = s.Hr.Employees.AsNoTracking().Where(e => e.BranchId == BranchId);
            if (!IncludeLeavers) query = query.Where(e => e.SeparatedOn == null);
            if (!string.IsNullOrEmpty(term))
                query = query.Where(e =>
                    EF.Functions.ILike(e.FullName, $"%{term}%")
                    || EF.Functions.ILike(e.EmployeeCode, $"%{term}%")
                    || (e.Phone != null && EF.Functions.ILike(e.Phone, $"%{term}%")));

            Total = await query.CountAsync();

            var employees = await query.OrderBy(e => e.EmployeeCode).Take(200).ToListAsync();
            var ids = employees.Select(e => e.Id).ToList();

            // Cross-table joins happen in memory, not in SQL: the assignment's unit and designation
            // are separate tables and the house rule forbids relying on navigation properties.
            var assignments = await s.Hr.Assignments.AsNoTracking()
                .Where(a => ids.Contains(a.EmployeeId) && a.EffectiveTo == null)
                .ToListAsync();
            var units = await s.Hr.OrgUnits.AsNoTracking()
                .Where(u => u.BranchId == BranchId).ToDictionaryAsync(u => u.Id, u => u.Name);
            var designations = await s.Hr.Designations.AsNoTracking()
                .Where(d => d.BranchId == BranchId).ToDictionaryAsync(d => d.Id, d => d.Name);

            Rows = employees.Select(e =>
            {
                var a = assignments.FirstOrDefault(x => x.EmployeeId == e.Id);
                return new EmployeeRow(
                    e.Id, e.EmployeeCode, e.FullName,
                    a is null ? null : units.GetValueOrDefault(a.OrgUnitId),
                    a is null ? null : designations.GetValueOrDefault(a.DesignationId),
                    e.JoinedOn, e.Status, e.Phone);
            }).ToList();
        });
    }
}
