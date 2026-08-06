namespace Hms.Hr.Data;

// Spec 0056 — the employment between the hire and the payslip: what kind of engagement it is, when
// probation falls due, who the family and nominees are, which documents expire, and how it ends.
//
// ADR-0027's rule holds throughout: probation length, notice period and gratuity days-per-year are
// EMPLOYER configuration carrying an effective date. Not one Bangladeshi statutory number appears in
// this file, and the tables ship empty — "not configured" is a stated answer, an invented default is
// not (module PRD D2, N-HR11).

/// <summary>
/// What kind of engagement this is (module PRD §7.1, G12). Distinct from
/// <see cref="EmploymentStatus"/>: a <see cref="Permanent"/> post is normally held on
/// <c>probation</c> status for its first months. The type is the contract; the status is where the
/// person has got to within it.
/// </summary>
public static class EmploymentType
{
    public const string Permanent = "permanent";
    public const string Probationary = "probationary";
    /// <summary>Fixed term. The only type for which an end date is required.</summary>
    public const string Contract = "contract";
    public const string PartTime = "part_time";
    public const string Intern = "intern";
    public const string DailyWage = "daily_wage";
    public const string Consultant = "consultant";

    public static readonly string[] All =
        [Permanent, Probationary, Contract, PartTime, Intern, DailyWage, Consultant];

    public static string Label(string type) => type switch
    {
        Permanent => "Permanent",
        Probationary => "Probationary",
        Contract => "Contract",
        PartTime => "Part-time",
        Intern => "Intern",
        DailyWage => "Daily wage",
        Consultant => "Consultant on payroll",
        _ => type,
    };

    /// <summary>§7.1: "contract (with an end date)". The end date is what makes it a contract.</summary>
    public static bool NeedsEndDate(string type) => type == Contract;
}

/// <summary>
/// The employer's employment rules — the three numbers the probation and settlement flows would
/// otherwise have to invent. Effective-dated like every other policy row, and every field means
/// "not configured" when it is zero or null.
/// </summary>
public class EmploymentPolicy
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Months of probation from joining. 0 = the employer sets each due date by hand.</summary>
    public int ProbationMonths { get; set; }

    /// <summary>Days of notice the employment requires. 0 = not configured; no adjustment computes.</summary>
    public int NoticePeriodDays { get; set; }

    /// <summary>Whether unserved notice is adjusted on the final settlement, in either direction.</summary>
    public bool AdjustNoticePay { get; set; }

    /// <summary>Whether accrued leave is encashed at settlement, and of which type.</summary>
    public bool EncashLeaveOnSettlement { get; set; }
    public long? EncashableLeaveTypeId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

// ---- the personal file ---------------------------------------------------------------------------

public static class DependantRelation
{
    public const string Spouse = "spouse";
    public const string Son = "son";
    public const string Daughter = "daughter";
    public const string Father = "father";
    public const string Mother = "mother";
    public const string Brother = "brother";
    public const string Sister = "sister";
    public const string Other = "other";

    public static readonly string[] All = [Spouse, Son, Daughter, Father, Mother, Brother, Sister, Other];
}

/// <summary>What a nomination is for. PF and gratuity are separately nominated in practice.</summary>
public static class NomineePurpose
{
    public const string ProvidentFund = "pf";
    public const string Gratuity = "gratuity";
    public const string Both = "both";

    public static readonly string[] All = [ProvidentFund, Gratuity, Both];
}

/// <summary>
/// A family member, and possibly a nominee for PF or gratuity (§7.1, G14). Shares are basis points
/// and must total 10000 per purpose — <c>hr.pf_policy</c> and <c>hr.gratuity_rule</c> exist to pay
/// money to someone, and until this table there was nobody to name.
/// <para>
/// Never deleted and never edited in place: a nomination is a legal instruction, so a change
/// supersedes (hard rule 4's shape applied to people rather than money).
/// </para>
/// </summary>
public class EmployeeDependant
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }

    public required string Name { get; set; }
    public required string Relation { get; set; }        // DependantRelation
    public DateOnly? DateOfBirth { get; set; }
    public string? Phone { get; set; }
    public string? NationalId { get; set; }
    public string? Address { get; set; }

    public bool IsNominee { get; set; }
    /// <summary>Null unless <see cref="IsNominee"/>. One of <see cref="NomineePurpose"/>.</summary>
    public string? Purpose { get; set; }
    /// <summary>Basis points of the benefit. Shares for a purpose must total 10000.</summary>
    public int ShareBp { get; set; }

    /// <summary>The row this one replaces, so the previous instruction stays readable.</summary>
    public long? SupersedesId { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
    public long? SupersededBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

/// <summary>
/// Document kinds (§7.1, G21). <see cref="Licence"/> carries G16's professional registration: one
/// table, one expiry engine, one register — a second near-identical table for licences would drift
/// from this one within a phase.
/// </summary>
public static class DocumentKind
{
    public const string NationalId = "nid";
    public const string Passport = "passport";
    public const string BirthCertificate = "birth_certificate";
    public const string Contract = "contract";
    public const string Certificate = "certificate";
    /// <summary>Professional registration — BMDC, BNMC, pharmacy council and the like. `[hospital]`</summary>
    public const string Licence = "licence";
    public const string Photograph = "photograph";
    public const string Other = "other";

    public static readonly string[] All =
        [NationalId, Passport, BirthCertificate, Contract, Certificate, Licence, Photograph, Other];

    public static string Label(string kind) => kind switch
    {
        NationalId => "National ID",
        Passport => "Passport",
        BirthCertificate => "Birth certificate",
        Contract => "Contract",
        Certificate => "Certificate",
        Licence => "Professional licence",
        Photograph => "Photograph",
        Other => "Other",
        _ => kind,
    };
}

/// <summary>
/// One typed document on an employee's file, with an expiry the module can alert on. A renewal is a
/// new row pointing at the one it supersedes, so "when did this licence last lapse" stays answerable.
/// <para>
/// An expired document <b>never blocks anything</b> (§7.1, G16): it warns, loudly, on the register,
/// the command centre and the employee's record.
/// </para>
/// </summary>
public class EmployeeDocument
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }

    public required string Kind { get; set; }            // DocumentKind
    public required string Title { get; set; }
    public string? Number { get; set; }
    /// <summary>The council, board or authority that issued it. Required for a licence.</summary>
    public string? IssuingBody { get; set; }
    public DateOnly? IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? FilePath { get; set; }
    public string? Note { get; set; }

    public long? SupersedesId { get; set; }
    public DateTimeOffset? SupersededAt { get; set; }
    public long? SupersededBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

// ---- separation, clearance and final settlement ---------------------------------------------------

public static class SeparationKind
{
    public const string Resignation = "resignation";
    public const string Termination = "termination";
    public const string Retirement = "retirement";
    public const string ContractEnd = "contract_end";
    public const string Death = "death";

    public static readonly string[] All = [Resignation, Termination, Retirement, ContractEnd, Death];

    public static string Label(string kind) => kind switch
    {
        Resignation => "Resignation",
        Termination => "Termination",
        Retirement => "Retirement",
        ContractEnd => "End of contract",
        Death => "Death in service",
        _ => kind,
    };

    /// <summary>
    /// Who ended it, which is what decides the direction of the notice-pay adjustment (§7.1's
    /// "either way"): an employer who cuts the notice short pays in lieu; an employee who does
    /// has it recovered.
    /// </summary>
    public static bool EmployerInitiated(string kind) => kind is Termination or ContractEnd;
}

/// <summary>Module PRD §9's final-settlement states, in order. A separation carries them.</summary>
public static class SeparationState
{
    public const string Initiated = "initiated";
    public const string InClearance = "in_clearance";
    public const string Computed = "computed";
    public const string Approved = "approved";
    public const string Paid = "paid";
    public const string DocumentsIssued = "documents_issued";
    public const string Cancelled = "cancelled";

    /// <summary>The one forward path. Anything not in here is refused with a sentence.</summary>
    public static readonly string[] Order =
        [Initiated, InClearance, Computed, Approved, Paid, DocumentsIssued];

    public static string Label(string state) => state switch
    {
        Initiated => "Initiated",
        InClearance => "In clearance",
        Computed => "Computed",
        Approved => "Approved",
        Paid => "Paid",
        DocumentsIssued => "Documents issued",
        Cancelled => "Cancelled",
        _ => state,
    };
}

/// <summary>
/// One employment ending. §9 gives Employment and Final settlement two overlapping state lists; they
/// are one row here, because "computed" is a fact about a separation and two rows would need a rule
/// about which is authoritative.
/// <para>
/// The employee row moves to <c>on_notice</c> at initiation and to its terminal status on the last
/// working day — a person serving notice has not left, and the module already had a status that
/// says so and nothing that set it.
/// </para>
/// </summary>
public class Separation
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }

    public required string Kind { get; set; }            // SeparationKind
    public string State { get; set; } = SeparationState.Initiated;

    /// <summary>The day notice was served or the decision taken.</summary>
    public DateOnly NoticeOn { get; set; }
    /// <summary>Notice the employment required, snapshotted from the policy in force. 0 = none configured.</summary>
    public int NoticeDays { get; set; }
    public DateOnly IntendedLastWorkingDay { get; set; }
    /// <summary>What actually happened. Drives every settlement figure.</summary>
    public DateOnly LastWorkingDay { get; set; }
    /// <summary>When the relieving takes effect — often after the last working day, when clearance ends.</summary>
    public DateOnly? RelievingOn { get; set; }

    public string? Reason { get; set; }
    /// <summary>§7.1 `[S]` — restricted to holders of hr.settlement.manage.</summary>
    public string? ExitInterviewNote { get; set; }

    public DateTimeOffset? ComputedAt { get; set; }
    public long? ComputedBy { get; set; }
    /// <summary>Sum of the settlement lines at the moment of computation. Recomputing replaces both.</summary>
    public long NetPayableTaka { get; set; }
    /// <summary>Why a figure is missing — an unconfigured gratuity rule states itself here (D2).</summary>
    public string? ComputationNotes { get; set; }

    /// <summary>kernel.approval_request — §12's HR Officer → Accounts Manager → MD.</summary>
    public long? ApprovalRequestId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public long? ApprovedBy { get; set; }

    /// <summary>The supplementary run that paid it. A settlement is never paid outside payroll.</summary>
    public long? PaidRunId { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? DocumentsIssuedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

/// <summary>The departments §7.1 names. Seeded onto every separation at initiation.</summary>
public static class ClearanceDepartment
{
    public const string Hr = "hr";
    public const string Accounts = "accounts";
    public const string It = "it";
    public const string Store = "store";
    /// <summary>Routed to the head of the employee's own org unit, so it has an owner.</summary>
    public const string DepartmentHead = "department_head";

    public static readonly string[] All = [Hr, Accounts, It, Store, DepartmentHead];

    public static string Label(string d) => d switch
    {
        Hr => "HR",
        Accounts => "Accounts",
        It => "IT",
        Store => "Store",
        DepartmentHead => "Department head",
        _ => d,
    };
}

public static class ClearanceStatus
{
    public const string Pending = "pending";
    public const string Cleared = "cleared";
    /// <summary>Cleared, but with something to recover — an unreturned asset, an outstanding advance.</summary>
    public const string ClearedWithDues = "cleared_with_dues";
    public const string Blocked = "blocked";
}

/// <summary>One department's sign-off on one separation. Attributable, per §7.1.</summary>
public class ClearanceItem
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long SeparationId { get; set; }

    public required string Department { get; set; }      // ClearanceDepartment
    /// <summary>Set for the department-head item: the unit whose head signs it.</summary>
    public long? OrgUnitId { get; set; }
    public string Status { get; set; } = ClearanceStatus.Pending;
    public string? Note { get; set; }
    /// <summary>Recoverable from the settlement — an unreturned laptop, an unsettled canteen bill.</summary>
    public long DuesTaka { get; set; }

    public long? SignedOffBy { get; set; }
    public string? SignedOffByName { get; set; }
    public DateTimeOffset? SignedOffAt { get; set; }
}

/// <summary>Where a settlement figure came from. Each maps to one arm of §7.1's list.</summary>
public static class SettlementSource
{
    public const string UnpaidSalary = "unpaid_salary";
    public const string LeaveEncashment = "leave_encashment";
    public const string Gratuity = "gratuity";
    public const string NoticePay = "notice_pay";
    public const string LoanRecovery = "loan_recovery";
    public const string ClearanceDues = "clearance_dues";
    public const string Manual = "manual";
}

/// <summary>
/// One line of a final settlement statement. <see cref="Basis"/> is the "why this number" string —
/// the same discipline <c>PayrollComponentLine</c> already carries, and the reason the statement can
/// be handed to a departing employee and defended.
/// </summary>
public class SettlementLine
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long SeparationId { get; set; }

    public int Ordinal { get; set; }
    public required string Label { get; set; }
    /// <summary>PayComponentKind.Earning or .Deduction — the statement nets one against the other.</summary>
    public required string Kind { get; set; }
    public long AmountTaka { get; set; }
    public required string Source { get; set; }          // SettlementSource
    public string? Basis { get; set; }
    /// <summary>Set on a hand-added line; a computed line is regenerated and carries none.</summary>
    public long? AddedBy { get; set; }
    public string? AddedByName { get; set; }
}

// ---- letters ---------------------------------------------------------------------------------------

/// <summary>The documents §5A-17 / G20 asks for. Two of them name a salary and are gated on it.</summary>
public static class LetterKind
{
    public const string Appointment = "appointment";
    public const string Confirmation = "confirmation";
    public const string ProbationExtension = "probation_extension";
    public const string Increment = "increment";
    public const string Promotion = "promotion";
    public const string Experience = "experience";
    public const string Termination = "termination";
    public const string Relieving = "relieving";
    public const string SalaryCertificate = "salary_certificate";
    public const string Noc = "noc";

    public static readonly string[] All =
    [
        Appointment, Confirmation, ProbationExtension, Increment, Promotion,
        Experience, Termination, Relieving, SalaryCertificate, Noc,
    ];

    public static string Label(string kind) => kind switch
    {
        Appointment => "Appointment letter",
        Confirmation => "Confirmation letter",
        ProbationExtension => "Probation extension letter",
        Increment => "Increment letter",
        Promotion => "Promotion letter",
        Experience => "Experience certificate",
        Termination => "Termination letter",
        Relieving => "Relieving letter",
        SalaryCertificate => "Salary certificate",
        Noc => "No-objection certificate",
        _ => kind,
    };

    /// <summary>
    /// A letter that states what someone earns. D6 makes these need <c>hr.salary.read</c> on top of
    /// the right to issue — an HR clerk who may issue an experience certificate may not be the
    /// person who tells a landlord what a nurse is paid.
    /// </summary>
    public static bool SalaryBearing(string kind) => kind is SalaryCertificate or Appointment or Increment;
}

/// <summary>
/// An employer-edited letter body with <c>{{tokens}}</c>. Templates are edited freely; issued letters
/// are frozen (see <see cref="IssuedLetter"/>), so editing one never rewrites a document already in
/// somebody's hand.
/// </summary>
public class LetterTemplate
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Kind { get; set; }            // LetterKind
    public required string Title { get; set; }
    public required string Body { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}

/// <summary>
/// A letter that was issued. Append-only, numbered, and carrying the <b>rendered</b> body rather
/// than a template reference — the same reason a payslip snapshots its component names: the document
/// must reproduce itself years later regardless of what the template says by then.
/// </summary>
public class IssuedLetter
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public required string Kind { get; set; }            // LetterKind
    public required string LetterNo { get; set; }
    public required string Title { get; set; }
    /// <summary>Frozen at issue. Never updated by anything.</summary>
    public required string Body { get; set; }
    public DateOnly IssuedOn { get; set; }
    public long? SeparationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

// ---- notification switches ---------------------------------------------------------------------

/// <summary>
/// The time-based alerts this phase delivers (§7.14, G57). Kinds appear here only once the thing
/// they alert on exists — a switch that controls nothing is a lie told by a settings screen.
/// </summary>
public static class HrNotificationKind
{
    public const string ProbationDue = "probation_due";
    public const string ContractExpiring = "contract_expiring";
    public const string DocumentExpiring = "document_expiring";
    public const string LicenceExpiring = "licence_expiring";
    public const string Birthday = "birthday";
    public const string WorkAnniversary = "work_anniversary";

    public static readonly string[] All =
        [ProbationDue, ContractExpiring, DocumentExpiring, LicenceExpiring, Birthday, WorkAnniversary];

    public static string Label(string kind) => kind switch
    {
        ProbationDue => "Probation falling due",
        ContractExpiring => "Contract ending",
        DocumentExpiring => "Document expiring",
        LicenceExpiring => "Professional licence expiring",
        Birthday => "Birthday",
        WorkAnniversary => "Work anniversary",
        _ => kind,
    };

    /// <summary>What the horizon means for each kind, in the operator's words.</summary>
    public static string Horizon(string kind) => kind switch
    {
        Birthday or WorkAnniversary => "days before the date",
        _ => "days of warning",
    };

    /// <summary>
    /// The lead time used when the employer has not configured one. This is a display horizon, not a
    /// statutory value — nothing legal turns on it and D2 is not engaged.
    /// </summary>
    public static int DefaultDaysBefore(string kind) => kind switch
    {
        Birthday or WorkAnniversary => 0,
        ProbationDue => 30,
        ContractExpiring => 60,
        _ => 45,
    };
}

/// <summary>One employer switch, per §7.14's "each independently switchable".</summary>
public class NotificationSetting
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Kind { get; set; }            // HrNotificationKind
    /// <summary>Whether a message is sent. The register and the command centre show it regardless.</summary>
    public bool Enabled { get; set; }
    /// <summary>How far ahead. Drives the register's default horizon as well as the message.</summary>
    public int DaysBefore { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}
