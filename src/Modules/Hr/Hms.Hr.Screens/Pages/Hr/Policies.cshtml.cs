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

    /// <summary>Opening the screen fills the form with what is actually in force.</summary>
    public async Task OnGetAsync() => await LoadAsync(fillForm: true);

    /// <summary>
    /// Creates the one policy a run cannot start without. Everything else can legitimately be absent
    /// — an employer with no provident fund simply has no PF rows.
    /// </summary>
    public async Task<IActionResult> OnPostSavePayrollPolicyAsync()
    {
        // A long that would not bind — letters, or a number too big — leaves the property at 0 and
        // the form would quietly save "no minimum". §7's operators are slow typists; a typo must
        // not become a payroll setting (spec 0037).
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            Fail("The minimum net pay has to be a whole number of taka.");
            return Page();
        }

        if (MinimumNetPay < 0)
        {
            await LoadAsync();
            Fail("The minimum net pay cannot be negative.");
            return Page();
        }

        try
        {
            await tx.RunAsync(async s =>
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6));
                var open = await s.Hr.PayrollPolicies
                    .Where(p => p.BranchId == BranchId && p.EffectiveTo == null)
                    .OrderByDescending(p => p.EffectiveFrom)
                    .FirstOrDefaultAsync();

                // Effective-dating, not overwriting: last year's salary sheet must still resolve
                // last year's convention. The database refuses an overlap outright.
                if (open is not null)
                {
                    // Saving twice in one day is the ordinary case — an operator corrects a typo in
                    // the minimum and presses Save again. Amend today's policy in place rather than
                    // opening a second one that would start on the same day. Before spec 0037 this
                    // branch threw an HrException nobody caught: a 500 on the second Save.
                    if (open.EffectiveFrom == today)
                    {
                        open.DayCountConvention = DayCount;
                        open.MinimumNetPayTaka = MinimumNetPay;
                        return;
                    }
                    if (open.EffectiveFrom > today)
                        throw new HrException(
                            "A policy already starts in the future — change its dates instead of adding another.");

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
        }
        catch (HrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }

        Toast("Payroll policy saved — it applies from today onward", "rule");
        return Redirect("/hr/policies");
    }

    private async Task LoadAsync(bool fillForm = false) => await tx.RunAsync(async s =>
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6));

        var units = await s.Hr.OrgUnits.CountAsync(x => x.BranchId == BranchId && x.Active);
        var designations = await s.Hr.Designations.CountAsync(x => x.BranchId == BranchId && x.Active);
        var grades = await s.Hr.Grades.CountAsync(x => x.BranchId == BranchId && x.Active);
        var shifts = await s.Hr.Shifts.CountAsync(x => x.BranchId == BranchId && x.Active);
        var leaveTypes = await s.Hr.LeaveTypes.CountAsync(x => x.BranchId == BranchId && x.Active);
        var components = await s.Hr.PayComponents.CountAsync(x => x.BranchId == BranchId && x.Active);

        // Every row links somewhere it can be created. Four of these used to link nowhere, which
        // made the screen a list of things the operator was told to configure with nothing to
        // configure them on (spec 0036).
        Structure =
        [
            new("Units", units, "Departments or sections. Employees are placed in one.",
                "/hr/masters?tab=units"),
            new("Designations", designations, "Job titles.",
                "/hr/masters?tab=designations"),
            new("Grades", grades, "Pay bands. Pay scales hang off these, effective-dated.",
                "/hr/masters?tab=grades"),
            new("Shifts", shifts, "Working windows. Night shifts pair punches across midnight.",
                "/hr/masters?tab=shifts"),
            new("Leave types", leaveTypes, "Whatever this employer offers — we ship none.",
                "/hr/masters?tab=leave-types"),
            new("Pay components", components, "The lines on a payslip: basic, allowances, deductions.",
                "/hr/masters?tab=components"),
        ];

        var effective = await s.Hr.PayrollPolicies.AsNoTracking()
            .Where(x => x.BranchId == BranchId && x.EffectiveFrom <= today
                        && (x.EffectiveTo == null || x.EffectiveTo >= today))
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync();
        var payrollPolicy = effective is null ? 0 : 1;

        // Show what is configured. The form used to render its defaults on every visit — 0 and
        // calendar days — so the operator could not read the current setting, and pressing Save to
        // change only the convention silently reset the minimum net pay to zero (spec 0037).
        if (fillForm && effective is not null)
        {
            DayCount = effective.DayCountConvention;
            MinimumNetPay = effective.MinimumNetPayTaka;
        }
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
