using Hms.Hr.Data;
using Hms.Kernel.Money;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record SetupItem(string Name, int Count, string Why, string? Link);

/// <summary>
/// The configuration a payroll run depends on (ADR-0027). This screen exists because the product
/// deliberately ships <b>empty</b>: no tax slabs, no PF rates, no leave entitlements. Bangladesh
/// Labour Act entitlements and NBR slabs appear in no document this project can cite, and inventing
/// them would be worse than asking. So the screen's job is to say plainly what is missing and why
/// payroll will refuse until it is filled in.
/// </summary>
[Authorize(Policy = HrPerm.PolicyManage)]
public class PoliciesModel(IHrTx tx) : HmsPageModel
{
    public IReadOnlyList<SetupItem> Structure { get; private set; } = [];
    public IReadOnlyList<SetupItem> PayRules { get; private set; } = [];
    public bool PayrollPossible { get; private set; }

    [BindProperty] public string DayCount { get; set; } = DayCountConvention.CalendarDays;
    [BindProperty] public long MinimumNetPay { get; set; }

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>
    /// Creates the one policy a run cannot start without. Everything else can legitimately be absent
    /// — an employer with no provident fund simply has no PF rows.
    /// </summary>
    public async Task<IActionResult> OnPostSavePayrollPolicyAsync()
    {
        if (MinimumNetPay < 0)
        {
            await LoadAsync();
            Fail("The minimum net pay cannot be negative.");
            return Page();
        }

        await tx.RunAsync(async s =>
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6));
            var open = await s.Hr.PayrollPolicies
                .Where(p => p.BranchId == BranchId && p.EffectiveTo == null)
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefaultAsync();

            // Effective-dating, not overwriting: last year's salary sheet must still resolve last
            // year's convention. The database refuses an overlap outright.
            if (open is not null)
            {
                if (open.EffectiveFrom >= today)
                    throw new HrException(
                        "A policy already starts today or later — change its dates instead of adding another.");
                open.EffectiveTo = today.AddDays(-1);
            }

            s.Hr.PayrollPolicies.Add(new PayrollPolicy
            {
                BranchId = BranchId,
                EffectiveFrom = today,
                DayCountConvention = DayCount,
                MinimumNetPayTaka = MinimumNetPay,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = ActorId,
            });
        });

        Toast("Payroll policy saved — it applies from today onward", "rule");
        return Redirect("/hr/policies");
    }

    private async Task LoadAsync() => await tx.RunAsync(async s =>
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6));

        var units = await s.Hr.OrgUnits.CountAsync(x => x.BranchId == BranchId && x.Active);
        var designations = await s.Hr.Designations.CountAsync(x => x.BranchId == BranchId && x.Active);
        var grades = await s.Hr.Grades.CountAsync(x => x.BranchId == BranchId && x.Active);
        var shifts = await s.Hr.Shifts.CountAsync(x => x.BranchId == BranchId && x.Active);
        var leaveTypes = await s.Hr.LeaveTypes.CountAsync(x => x.BranchId == BranchId && x.Active);
        var components = await s.Hr.PayComponents.CountAsync(x => x.BranchId == BranchId && x.Active);

        Structure =
        [
            new("Units", units, "Departments or sections. Employees are placed in one.", null),
            new("Designations", designations, "Job titles.", null),
            new("Grades", grades, "Pay bands. Pay scales hang off these, effective-dated.", null),
            new("Shifts", shifts, "Working windows. Night shifts pair punches across midnight.", "/hr/roster"),
            new("Leave types", leaveTypes, "Whatever this employer offers — we ship none.", "/hr/leave"),
            new("Pay components", components, "The lines on a payslip: basic, allowances, deductions.", null),
        ];

        var payrollPolicy = await s.Hr.PayrollPolicies.CountAsync(
            x => x.BranchId == BranchId && x.EffectiveFrom <= today
                 && (x.EffectiveTo == null || x.EffectiveTo >= today));
        var taxSlabs = await s.Hr.TaxSlabs.CountAsync(x => x.BranchId == BranchId);
        var pf = await s.Hr.PfPolicies.CountAsync(x => x.BranchId == BranchId);
        var overtime = await s.Hr.OvertimeRules.CountAsync(x => x.BranchId == BranchId);
        var deduction = await s.Hr.DeductionRules.CountAsync(x => x.BranchId == BranchId);
        var grace = await s.Hr.GraceTimeRules.CountAsync(x => x.BranchId == BranchId);

        PayRules =
        [
            new("Payroll policy", payrollPolicy,
                "Proration convention and the minimum net pay. Payroll refuses without this.", null),
            new("Income tax slabs", taxSlabs,
                "Your own bands. None are shipped — we do not assert NBR rates we cannot verify.", null),
            new("Provident fund", pf, "Employee and employer shares, if you run one.", null),
            new("Overtime rule", overtime, "Multiplier and threshold, or bank the minutes instead.", null),
            new("Absence deduction", deduction, "What an unpaid day costs.", null),
            new("Grace time", grace, "Late tolerance before a deduction applies.", null),
        ];

        PayrollPossible = payrollPolicy > 0 && components > 0;
    });
}
