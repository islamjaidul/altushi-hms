using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

/// <summary>
/// Hiring. §7's operators are non-technical and slow typists, so the form asks for the minimum that
/// makes payroll possible — name, joining date, where they sit — and leaves everything else to the
/// employee's own page afterwards.
/// </summary>
[Authorize(Policy = HrPerm.EmployeeManage)]
public class EmployeeNewModel(IHrTx tx, EmployeeService employees) : HmsPageModel
{
    [BindProperty] public string FullName { get; set; } = "";
    [BindProperty] public string? Phone { get; set; }
    [BindProperty] public string? NationalId { get; set; }
    [BindProperty] public string JoinedOn { get; set; } = "";
    [BindProperty] public string? DateOfBirth { get; set; }
    [BindProperty] public long OrgUnitId { get; set; }
    [BindProperty] public long DesignationId { get; set; }
    [BindProperty] public long GradeId { get; set; }

    public List<SelectListItem> Units { get; private set; } = [];
    public List<SelectListItem> Designations { get; private set; } = [];
    public List<SelectListItem> Grades { get; private set; } = [];
    public bool MastersMissing { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!FlexibleDate.TryParse(JoinedOn, out var joined))
        {
            await LoadAsync();
            Fail("Enter the joining date as dd/mm/yyyy — service length and payroll depend on it.");
            return Page();
        }

        FlexibleDate.TryParse(DateOfBirth ?? "", out var dob);

        try
        {
            var id = await tx.RunAsync(async s => (await employees.HireAsync(
                s.Hr, s.Kernel, BranchId,
                new Employee
                {
                    EmployeeCode = "",            // issued in-transaction by NumberSeriesService
                    FullName = FullName.Trim(),
                    Phone = string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(),
                    NationalId = string.IsNullOrWhiteSpace(NationalId) ? null : NationalId.Trim(),
                    JoinedOn = joined,
                    DateOfBirth = dob == default ? null : dob,
                },
                OrgUnitId, DesignationId, GradeId, ActorId, ActorName)).Id);

            Toast($"{FullName.Trim()} is on the payroll — set their pay next", "person_add");
            return Redirect($"/hr/employees/{id}");
        }
        catch (HrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        Units = await s.Hr.OrgUnits.AsNoTracking()
            .Where(u => u.BranchId == BranchId && u.Active).OrderBy(u => u.Name)
            .Select(u => new SelectListItem(u.Name, u.Id.ToString())).ToListAsync();
        Designations = await s.Hr.Designations.AsNoTracking()
            .Where(d => d.BranchId == BranchId && d.Active).OrderBy(d => d.Name)
            .Select(d => new SelectListItem(d.Name, d.Id.ToString())).ToListAsync();
        Grades = await s.Hr.Grades.AsNoTracking()
            .Where(g => g.BranchId == BranchId && g.Active).OrderBy(g => g.Rank)
            .Select(g => new SelectListItem(g.Name, g.Id.ToString())).ToListAsync();

        // Hiring into an org structure that does not exist yet produces an unassignable employee,
        // so say so plainly rather than showing three empty dropdowns.
        MastersMissing = Units.Count == 0 || Designations.Count == 0 || Grades.Count == 0;
    });
}
