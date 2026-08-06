namespace Hms.Hr.Data;

// The employee record and its history. PRD §10 makes M16 the owner of Employee and M15/M14/M21 its
// readers; "user accounts may link to employees" is the AppUser.EmployeeRef column that has been
// sitting unwritten in adm.app_user since the MVP.

public static class EmploymentStatus
{
    public const string Probation = "probation";
    public const string Confirmed = "confirmed";
    public const string OnNotice = "on_notice";
    public const string Resigned = "resigned";
    public const string Terminated = "terminated";
    public const string Retired = "retired";
    // spec 0056: §9's terminal variants that had no status to land on, so both used to be recorded
    // as a resignation — which is not what either of them is.
    public const string ContractEnded = "contract_ended";
    public const string Deceased = "deceased";
}

/// <summary>
/// One person's employment. A rehire is a <em>new</em> row linked by <see cref="PersonRef"/> — never
/// a resurrected old one, because merging two employments would merge two service histories and
/// silently change gratuity and leave maths.
/// </summary>
public class Employee
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    /// <summary>Issued by NumberSeriesService. Never reused, including after termination.</summary>
    public required string EmployeeCode { get; set; }
    /// <summary>Stable identity of the human across rehires; null until a second employment exists.</summary>
    public string? PersonRef { get; set; }

    public required string FullName { get; set; }
    public string? FatherName { get; set; }
    public string? MotherName { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? Gender { get; set; }
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? PresentAddress { get; set; }
    public string? PermanentAddress { get; set; }
    public string? BloodGroup { get; set; }
    public string? EmergencyContact { get; set; }

    public DateOnly JoinedOn { get; set; }
    public DateOnly? ConfirmedOn { get; set; }
    public DateOnly? SeparatedOn { get; set; }
    public string Status { get; set; } = EmploymentStatus.Probation;

    // ---- spec 0056: what kind of engagement this is, and when it comes due ------------------------

    /// <summary>
    /// One of <see cref="Data.EmploymentType"/> (G12). Defaults to permanent because that is what
    /// every row created before spec 0056 actually is — the module could only ever make one shape of
    /// employment, so the default states a fact rather than guessing one.
    /// </summary>
    public string EmploymentType { get; set; } = Data.EmploymentType.Permanent;

    /// <summary>Required for a contract engagement; feeds the contract-expiring register.</summary>
    public DateOnly? ContractEndsOn { get; set; }

    /// <summary>
    /// When probation falls due for a decision. The date the module never had: <c>ConfirmedOn</c> and
    /// <c>EmploymentStatus.Confirmed</c> existed from the start and, before this, were written by the
    /// demo seed alone — an employee hired through the UI stayed on probation forever.
    /// </summary>
    public DateOnly? ProbationDueOn { get; set; }

    // §3.4: BEFTN needs bank routing + account on employees, doctors, referrers and suppliers.
    public string? BankName { get; set; }
    public string? BankBranch { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankRoutingNo { get; set; }
    public string? Tin { get; set; }

    /// <summary>
    /// How this person is actually paid (spec 0057, G45). Defaults to bank, which is what the
    /// existing bank columns already assume; cash and cheque are the other two rows a Bangladeshi
    /// employer's disbursement sheet has.
    /// </summary>
    public string PaymentMethod { get; set; } = Data.PaymentMethod.Bank;

    /// <summary>Links to adm.app_user.employee_ref for self-service (§12 "U (own leave)").</summary>
    public string? UserRef { get; set; }
    /// <summary>Scanned documents as [{name,path,uploadedAt}] — jsonb, per §5 M16 "document attachments".</summary>
    public string DocumentsJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>
/// Where an employee sits, as of a date. Promotion, transfer and re-designation are new rows, so the
/// org chart is reproducible for any past month — the same effective-dating rule the rate plan uses.
/// </summary>
public class EmployeeAssignment
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public long OrgUnitId { get; set; }
    public long DesignationId { get; set; }
    public long GradeId { get; set; }
    public long? WorkLocationId { get; set; }
    public long? ReportsToEmployeeId { get; set; }
    public long? WeeklyOffPatternId { get; set; }
    public long? HolidayCalendarId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>
/// An employee's pay as of a date. A payroll line pins the structure id it used, so reproduction
/// never depends on today's resolution logic still agreeing with last year's (hard rule 5).
/// </summary>
public class EmployeePayStructure
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public long? PayScaleId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? Reason { get; set; }            // joining | increment | promotion | revision
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>One component's amount within a pay structure.</summary>
public class EmployeePayComponent
{
    public long Id { get; set; }
    public long PayStructureId { get; set; }
    public long ComponentId { get; set; }
    /// <summary>Used when the component is <see cref="PayComponentCalc.Fixed"/>.</summary>
    public long AmountTaka { get; set; }
    /// <summary>Overrides the component's own rate when the employee is on a negotiated percentage.</summary>
    public long? PercentBpOverride { get; set; }
}

public static class EmploymentEventKind
{
    public const string Joined = "joined";
    public const string Confirmed = "confirmed";
    public const string Promoted = "promoted";
    public const string Transferred = "transferred";
    public const string Increment = "increment";
    public const string NoticeServed = "notice_served";
    public const string Resigned = "resigned";
    public const string Terminated = "terminated";
    public const string Retired = "retired";
    public const string Rehired = "rehired";

    // ---- spec 0056 -------------------------------------------------------------------------------
    /// <summary>Probation pushed to a new due date, with a reason. Both dates land in DetailJson.</summary>
    public const string ProbationExtended = "probation_extended";
    /// <summary>The engagement itself changed — contract made permanent, part-time made full.</summary>
    public const string TypeChanged = "type_changed";
    public const string ContractEnded = "contract_ended";
    public const string Deceased = "deceased";
    /// <summary>Clearance complete and the employment formally released.</summary>
    public const string Relieved = "relieved";
    /// <summary>The final settlement was approved and paid.</summary>
    public const string Settled = "settled";
}

/// <summary>
/// Append-only employment history. Hard rule 4's attribution requirement applied to people rather
/// than money: what happened, when, who did it, and why — never edited, never deleted.
/// </summary>
public class EmploymentEvent
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public required string Kind { get; set; }      // EmploymentEventKind
    public DateOnly OnDate { get; set; }
    public string? Note { get; set; }
    public string? DetailJson { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long RecordedBy { get; set; }
    public required string RecordedByName { get; set; }
}
