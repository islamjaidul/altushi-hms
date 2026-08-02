namespace Hms.Hr.Data;

// hr.* masters — spec 0036 (Wave A of 0034).
//
// Nothing here is clinical. P27 sells this module to employers that are not hospitals, so the org
// structure is customer-entered data: an "org unit" is a department, a section, a floor or a
// company division, whatever the customer calls it. There is no ward, no bed, no patient.

/// <summary>
/// What every master on <c>/hr/masters</c> has in common: a name you can change and an active flag
/// you can turn off. It exists so the screen's rename and retire can resolve a row once instead of
/// six times through a switch — the shape that used to throw when the id was not on the open tab.
/// </summary>
public interface IMasterRow
{
    long Id { get; }
    string Name { get; set; }
    bool Active { get; set; }
}

/// <summary>
/// A node of the customer's own org tree (department / section / division). Self-referencing so a
/// three-level structure costs no schema, and no level is assumed.
/// </summary>
public class OrgUnit : IMasterRow
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public long? ParentId { get; set; }
    /// <summary>Employee who heads this unit — the first tier of the leave chain (§5A-16g).</summary>
    public long? HeadEmployeeId { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Job title. Separate from grade: two designations can share a pay grade.</summary>
public class Designation : IMasterRow
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>Pay grade / band. The salary numbers live on <see cref="PayScale"/>, effective-dated.</summary>
public class Grade : IMasterRow
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int Rank { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// A grade's pay numbers as of a date (hard rule 5 / ADR-0027). A historical salary sheet resolves
/// the scale that was effective in its period, so re-running last March reproduces last March.
/// </summary>
public class PayScale
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long GradeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public long BasicTaka { get; set; }
    public long? MinTaka { get; set; }
    public long? MaxTaka { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

public static class PayComponentKind
{
    public const string Earning = "earning";
    public const string Deduction = "deduction";
    /// <summary>Employer-side cost (e.g. employer PF share): not paid to the employee, but posted.</summary>
    public const string EmployerContribution = "employer_contribution";
}

public static class PayComponentCalc
{
    /// <summary>A flat taka amount held on the employee's pay structure.</summary>
    public const string Fixed = "fixed";
    /// <summary>A percentage of another component (usually basic), in basis points.</summary>
    public const string PercentOf = "percent_of";
    /// <summary>Computed by the engine from attendance / policy (OT, absence, late, arrears).</summary>
    public const string Computed = "computed";
}

/// <summary>
/// A line that can appear on a payslip. Which components exist, what they are called and how they
/// are computed is the employer's decision — the engine knows the <em>shapes</em> (fixed, percentage,
/// computed) and never a Bangladeshi rate (ADR-0027).
/// </summary>
public class PayComponent : IMasterRow
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }          // PayComponentKind
    public required string CalcMethod { get; set; }    // PayComponentCalc
    /// <summary>For <see cref="PayComponentCalc.PercentOf"/>: the component the percentage applies to.</summary>
    public long? BasedOnComponentId { get; set; }
    /// <summary>Basis points (5000 = 50.00%) — integers only, no decimal money anywhere (C3).</summary>
    public long PercentBp { get; set; }
    /// <summary>Counts toward taxable income when the tax step runs.</summary>
    public bool Taxable { get; set; }
    /// <summary>Counts toward the provident-fund base.</summary>
    public bool PfApplicable { get; set; }
    /// <summary>For computed components: which engine rule fills it. See <see cref="ComputedComponent"/>.</summary>
    public string? ComputedKind { get; set; }
    public int DisplayOrder { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>The computed components the engine knows how to fill. Rates stay in policy rows.</summary>
public static class ComputedComponent
{
    public const string OvertimePay = "overtime_pay";
    public const string AbsenceDeduction = "absence_deduction";
    public const string LateDeduction = "late_deduction";
    public const string LeaveWithoutPay = "leave_without_pay";
    public const string HolidayWorkPay = "holiday_work_pay";
    public const string LoanInstallment = "loan_installment";
    public const string Arrears = "arrears";
    public const string IncomeTax = "income_tax";
    public const string ProvidentFund = "provident_fund";
}

/// <summary>A physical work location. Not a hospital branch — a site within one employer.</summary>
public class WorkLocation
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// A named working window. Night shifts are ordinary here: when <see cref="EndsNextDay"/> the pair
/// window crosses midnight, and attendance attributes the pair to the shift's own date rather than
/// to whichever calendar day the punch happened to land on.
/// </summary>
public class Shift : IMasterRow
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public TimeOnly StartsAt { get; set; }
    public TimeOnly EndsAt { get; set; }
    public bool EndsNextDay { get; set; }
    /// <summary>Minutes before start / after end that still pair to this shift.</summary>
    public int PairToleranceMinutes { get; set; } = 240;
    public int BreakMinutes { get; set; }
    /// <summary>Standard minutes credited for a full day on this shift — the proration denominator.</summary>
    public int StandardMinutes { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// Which weekdays are off. Bangladeshi employers differ — Friday only, or Friday and Saturday — so
/// this is data, per org unit or per employee, and effective-dated because employers change it.
/// </summary>
public class WeeklyOffPattern
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Name { get; set; }
    /// <summary>Bit set over <see cref="DayOfWeek"/>: bit 0 = Sunday … bit 6 = Saturday.</summary>
    public int DayMask { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public class HolidayCalendar
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Name { get; set; }
    public bool Active { get; set; } = true;
}

public class Holiday
{
    public long Id { get; set; }
    public long CalendarId { get; set; }
    public DateOnly OnDate { get; set; }
    public required string Name { get; set; }
    /// <summary>Optional: a half-day public holiday still credits a partial day.</summary>
    public bool HalfDay { get; set; }
}

public static class LeaveAccrual
{
    /// <summary>A fixed entitlement granted at the start of each leave year.</summary>
    public const string AnnualGrant = "annual_grant";
    /// <summary>Earned per worked period — the rate is a policy number, not a statute we assert.</summary>
    public const string Earned = "earned";
    /// <summary>No balance; every application is simply recorded (e.g. leave without pay).</summary>
    public const string None = "none";
}

/// <summary>
/// A kind of leave the employer offers. Deliberately unnamed by us: we ship no "casual" or "earned"
/// defaults, because Bangladesh Labour Act entitlements are not recorded anywhere we can verify
/// (ADR-0027, P26). The customer names and quantifies their own.
/// </summary>
public class LeaveType : IMasterRow
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Accrual { get; set; }       // LeaveAccrual
    /// <summary>Unpaid leave feeds the leave-without-pay deduction instead of paying the day.</summary>
    public bool Paid { get; set; } = true;
    public bool Active { get; set; } = true;
}
