using System.ComponentModel.DataAnnotations;
using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Time;
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
public class PoliciesModel(IHrTx tx, PolicyResolver policies, AuditWriter audit) : HmsPageModel
{
    public IReadOnlyList<SetupItem> Structure { get; private set; } = [];
    public IReadOnlyList<SetupItem> PayRules { get; private set; } = [];
    public bool PayrollPossible { get; private set; }

    [BindProperty] public string DayCount { get; set; } = DayCountConvention.CalendarDays;
    [BindProperty] public long MinimumNetPay { get; set; }

    // ---- the six rule tables the engine reads and, until spec 0039 WP3, nothing wrote
    // (AUD-M16-01). Rates are basis points, matching the Masters screen's convention
    // ("4000 = 40%") — the placeholder on each input says so in the operator's terms.

    [BindProperty, Range(0, 30_000)] public int PerAbsentDayBp { get; set; } = 10_000;
    [BindProperty, Range(0, 30_000)] public int PerLeaveWithoutPayDayBp { get; set; } = 10_000;

    [BindProperty, Range(0, 240)] public int GraceMinutes { get; set; }
    [BindProperty, Range(0, 31)] public int FreeLateCountPerMonth { get; set; }
    [BindProperty, Range(0, 30_000)] public int DeductionPerLateBp { get; set; }

    [BindProperty, Range(0, 480)] public int ThresholdMinutes { get; set; }
    [BindProperty, Range(0, 50_000)] public long MultiplierBp { get; set; } = 20_000;
    [BindProperty, Range(0, 44_640)] public int MaxMinutesPerMonth { get; set; }
    [BindProperty] public bool BankInsteadOfPay { get; set; }
    // spec 0058
    [BindProperty] public bool RequireApproval { get; set; }
    [BindProperty, Range(0, 3650)] public int CompOffExpiryDays { get; set; }
    [BindProperty, Range(0, 6)] public int WeekStartsOn { get; set; } = (int)DayOfWeek.Saturday;

    [BindProperty, Range(0, 10_000)] public long EmployeeShareBp { get; set; }
    [BindProperty, Range(0, 10_000)] public long EmployerShareBp { get; set; }
    [BindProperty, Range(0, 120)] public int EligibilityMonths { get; set; }
    [BindProperty, Money] public long? MonthlyCapTaka { get; set; }

    [BindProperty, Range(0, 240)] public int MinimumServiceMonths { get; set; }
    [BindProperty, Range(0, 1_000_000)] public int DaysPerYearBp { get; set; }

    /// <summary>Tax bands, positional. No annotation — list elements are validated in the
    /// service (the input gate must not judge a range on a row the form never posted).</summary>
    [BindProperty] public List<long?> UpToTaka { get; set; } = [];
    [BindProperty] public List<long?> RateBp { get; set; } = [];

    // ---- spec 0056: employment rules (probation length, notice, settlement treatment)
    [BindProperty, Range(0, 60)] public int ProbationMonths { get; set; }
    [BindProperty, Range(0, 365)] public int NoticePeriodDays { get; set; }
    [BindProperty] public bool AdjustNoticePay { get; set; }
    [BindProperty] public bool EncashLeaveOnSettlement { get; set; }
    [BindProperty] public long? EncashableLeaveTypeId { get; set; }

    public bool HasEmploymentPolicy { get; private set; }
    public IReadOnlyList<LeaveType> LeaveTypes { get; private set; } = [];

    public bool HasDeductionRule { get; private set; }
    public bool HasGraceRule { get; private set; }
    public bool HasOvertimeRule { get; private set; }
    public bool HasPf { get; private set; }
    public bool HasGratuity { get; private set; }
    public IReadOnlyList<TaxSlab> CurrentSlabs { get; private set; } = [];

    /// <summary>Opening the screen fills the form with what is actually in force.</summary>
    public async Task OnGetAsync() => await LoadAsync(fillForm: true);

    public Task<IActionResult> OnPostSaveDeductionRuleAsync() => SaveRuleAsync(
        "Absence deduction saved",
        (s, today) => policies.SetDeductionAsync(
            s.Hr, s.Kernel, BranchId, today, PerAbsentDayBp, PerLeaveWithoutPayDayBp,
            ActorId, ActorName));

    public Task<IActionResult> OnPostSaveGraceTimeAsync() => SaveRuleAsync(
        "Grace time rule saved",
        (s, today) => policies.SetGraceTimeAsync(
            s.Hr, s.Kernel, BranchId, today, GraceMinutes, FreeLateCountPerMonth,
            DeductionPerLateBp, ActorId, ActorName));

    public Task<IActionResult> OnPostSaveOvertimeRuleAsync() => SaveRuleAsync(
        "Overtime rule saved",
        (s, today) => policies.SetOvertimeAsync(
            s.Hr, s.Kernel, BranchId, today, ThresholdMinutes, MultiplierBp,
            MaxMinutesPerMonth, BankInsteadOfPay, ActorId, ActorName,
            RequireApproval, CompOffExpiryDays));

    public Task<IActionResult> OnPostSavePfPolicyAsync() => SaveRuleAsync(
        "Provident fund policy saved",
        (s, today) => policies.SetPfAsync(
            s.Hr, s.Kernel, BranchId, today, EmployeeShareBp, EmployerShareBp,
            EligibilityMonths, MonthlyCapTaka, ActorId, ActorName));

    public Task<IActionResult> OnPostSaveGratuityRuleAsync() => SaveRuleAsync(
        "Gratuity rule saved",
        (s, today) => policies.SetGratuityAsync(
            s.Hr, s.Kernel, BranchId, today, MinimumServiceMonths, DaysPerYearBp,
            ActorId, ActorName));

    // spec 0056: the three numbers probation and settlement would otherwise have to invent.
    public Task<IActionResult> OnPostSaveEmploymentPolicyAsync() => SaveRuleAsync(
        "Employment rules saved",
        (s, today) => policies.SetEmploymentPolicyAsync(
            s.Hr, s.Kernel, BranchId, today, ProbationMonths, NoticePeriodDays, AdjustNoticePay,
            EncashLeaveOnSettlement, EncashableLeaveTypeId, ActorId, ActorName));

    public Task<IActionResult> OnPostSaveTaxSlabsAsync() => SaveRuleAsync(
        "Income tax table saved",
        (s, today) =>
        {
            var bands = new List<(long UpToTaka, long RateBp)>();
            for (var i = 0; i < Math.Max(UpToTaka.Count, RateBp.Count); i++)
            {
                var up = i < UpToTaka.Count ? UpToTaka[i] : null;
                var rate = i < RateBp.Count ? RateBp[i] : null;
                if (up is null && rate is null) continue;          // an empty grid row
                bands.Add((up ?? 0, rate ?? 0));
            }
            return policies.SetTaxSlabsAsync(
                s.Hr, s.Kernel, BranchId, today, bands, ActorId, ActorName);
        });

    private async Task<IActionResult> SaveRuleAsync(string toast, Func<HrScope, DateOnly, Task> body)
    {
        try
        {
            await tx.RunAsync(async s =>
            {
                var today = Dhaka.DateOf(DateTimeOffset.UtcNow);
                await body(s, today);
            });
        }
        catch (HrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }

        Toast($"{toast} — it applies from today onward", "rule");
        return Redirect("/hr/policies");
    }

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
                var today = Dhaka.DateOf(DateTimeOffset.UtcNow);
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
                        // Spec 0055: the other five policy tables go through PolicyResolver, which
                        // audits. This one is written here and was recorded nowhere, so a change to
                        // the minimum-net-pay floor left no trace at all.
                        audit.Append(s.Kernel, BranchId, ActorId, ActorName,
                            "hr.policy.payroll.set", "hr.payroll_policy", open.Id,
                            before: new { open.DayCountConvention, open.MinimumNetPayTaka },
                            after: new { DayCountConvention = DayCount, MinimumNetPayTaka = MinimumNetPay },
                            tier: 1);
                        open.DayCountConvention = DayCount;
                        open.MinimumNetPayTaka = MinimumNetPay;
                        open.WeekStartsOn = WeekStartsOn;
                        return;
                    }
                    if (open.EffectiveFrom > today)
                        throw new HrException(
                            "A policy already starts in the future — change its dates instead of adding another.");

                    open.EffectiveTo = today.AddDays(-1);
                }

                var added = s.Hr.PayrollPolicies.Add(new PayrollPolicy
                {
                    BranchId = BranchId,
                    EffectiveFrom = today,
                    DayCountConvention = DayCount,
                    MinimumNetPayTaka = MinimumNetPay,
                    WeekStartsOn = WeekStartsOn,
                    // Carried forward, not reset: closing one policy and opening the next must not
                    // silently lose the variance tolerance an employer already set.
                    VarianceTolerationBp = open?.VarianceTolerationBp ?? 2_000,
                    LeaveYearStartMonth = open?.LeaveYearStartMonth ?? 7,
                    RoundingResidueComponentId = open?.RoundingResidueComponentId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = ActorId,
                }).Entity;

                await s.Hr.SaveChangesAsync();
                audit.Append(s.Kernel, BranchId, ActorId, ActorName,
                    "hr.policy.payroll.set", "hr.payroll_policy", added.Id,
                    after: new { EffectiveFrom = today, DayCountConvention = DayCount, MinimumNetPayTaka = MinimumNetPay },
                    tier: 1);
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
        var today = Dhaka.DateOf(DateTimeOffset.UtcNow);

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
            WeekStartsOn = effective.WeekStartsOn;
        }
        var taxSlabs = await s.Hr.TaxSlabs.CountAsync(x => x.BranchId == BranchId);
        var pf = await s.Hr.PfPolicies.CountAsync(x => x.BranchId == BranchId);
        var overtime = await s.Hr.OvertimeRules.CountAsync(x => x.BranchId == BranchId);
        var deduction = await s.Hr.DeductionRules.CountAsync(x => x.BranchId == BranchId);
        var grace = await s.Hr.GraceTimeRules.CountAsync(x => x.BranchId == BranchId);
        var gratuity = await s.Hr.GratuityRules.CountAsync(x => x.BranchId == BranchId);
        var employment = await s.Hr.EmploymentPolicies.CountAsync(x => x.BranchId == BranchId);

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
            new("Gratuity", gratuity, "End-of-service days per completed year, if you pay one.", null),
            new("Employment rules", employment,
                "Probation length, notice period, and what a settlement does about them.", null),
        ];

        PayrollPossible = payrollPolicy > 0 && components > 0;

        // Each rule form opens showing the rule actually in force today, exactly as the payroll
        // policy form does — a screen that rendered defaults over a configured rule would invite
        // the operator to silently reset it (the spec 0037 lesson, applied to six more forms).
        var deductionRule = await policies.DeductionAsync(s.Hr, BranchId, today);
        HasDeductionRule = deductionRule is not null;
        var graceRule = await policies.GraceTimeAsync(s.Hr, BranchId, today);
        HasGraceRule = graceRule is not null;
        var overtimeRule = await policies.OvertimeAsync(s.Hr, BranchId, null, today);
        HasOvertimeRule = overtimeRule is not null;
        var pfPolicy = await policies.PfAsync(s.Hr, BranchId, today);
        HasPf = pfPolicy is not null;
        var gratuityRule = await s.Hr.GratuityRules.AsNoTracking()
            .Where(g => g.BranchId == BranchId && g.EffectiveFrom <= today
                        && (g.EffectiveTo == null || g.EffectiveTo >= today))
            .OrderByDescending(g => g.EffectiveFrom)
            .FirstOrDefaultAsync();
        HasGratuity = gratuityRule is not null;
        CurrentSlabs = await policies.TaxSlabsAsync(s.Hr, BranchId, today);

        var employmentPolicy = await SeparationService.EmploymentPolicyAsync(s.Hr, BranchId, today);
        HasEmploymentPolicy = employmentPolicy is not null;
        LeaveTypes = await s.Hr.LeaveTypes.AsNoTracking()
            .Where(t => t.BranchId == BranchId && t.Active)
            .OrderBy(t => t.Name)
            .ToListAsync();

        if (!fillForm) return;

        if (employmentPolicy is not null)
        {
            ProbationMonths = employmentPolicy.ProbationMonths;
            NoticePeriodDays = employmentPolicy.NoticePeriodDays;
            AdjustNoticePay = employmentPolicy.AdjustNoticePay;
            EncashLeaveOnSettlement = employmentPolicy.EncashLeaveOnSettlement;
            EncashableLeaveTypeId = employmentPolicy.EncashableLeaveTypeId;
        }

        if (deductionRule is not null)
        {
            PerAbsentDayBp = deductionRule.PerAbsentDayBp;
            PerLeaveWithoutPayDayBp = deductionRule.PerLeaveWithoutPayDayBp;
        }
        if (graceRule is not null)
        {
            GraceMinutes = graceRule.GraceMinutes;
            FreeLateCountPerMonth = graceRule.FreeLateCountPerMonth;
            DeductionPerLateBp = graceRule.DeductionPerLateBp;
        }
        if (overtimeRule is not null)
        {
            ThresholdMinutes = overtimeRule.ThresholdMinutes;
            MultiplierBp = overtimeRule.MultiplierBp;
            MaxMinutesPerMonth = overtimeRule.MaxMinutesPerMonth;
            BankInsteadOfPay = overtimeRule.BankInsteadOfPay;
            RequireApproval = overtimeRule.RequireApproval;
            CompOffExpiryDays = overtimeRule.CompOffExpiryDays;
        }
        if (pfPolicy is not null)
        {
            EmployeeShareBp = pfPolicy.EmployeeShareBp;
            EmployerShareBp = pfPolicy.EmployerShareBp;
            EligibilityMonths = pfPolicy.EligibilityMonths;
            MonthlyCapTaka = pfPolicy.MonthlyCapTaka;
        }
        if (gratuityRule is not null)
        {
            MinimumServiceMonths = gratuityRule.MinimumServiceMonths;
            DaysPerYearBp = gratuityRule.DaysPerYearBp;
        }
        UpToTaka = [.. CurrentSlabs.Select(x => (long?)x.UpToTaka)];
        RateBp = [.. CurrentSlabs.Select(x => (long?)x.RateBp)];
    });
}
