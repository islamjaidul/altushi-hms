namespace Hms.Hr.Data;

// Leave. §11's state machine is Applied → Recommended(dept head) → Approved/Rejected(HR) → Availed.
// §5A-16 asks for a three-tier chain instead. Rather than pick one and be wrong for half the
// customers, the tier count is a policy number and both shapes are configuration (noted for the PM
// in spec 0034: the PRD contradicts itself here).

public static class LeaveApplicationState
{
    public const string Applied = "applied";
    public const string Recommended = "recommended";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Availed = "availed";
    public const string Cancelled = "cancelled";
}

/// <summary>
/// How much of a leave type an employee gets and under what conditions — all employer numbers.
/// We ship none: Bangladesh Labour Act entitlements are not recorded in any document this project
/// can cite, and rule 3 forbids asserting a regulation we have not verified (ADR-0027, P26).
/// </summary>
public class LeavePolicy
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long LeaveTypeId { get; set; }
    /// <summary>Null applies to everyone; set to scope a policy to one grade.</summary>
    public long? GradeId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Days granted per leave year, in basis points of a day (10000 = 1 day).</summary>
    public int AnnualEntitlementBp { get; set; }
    /// <summary>Earned-leave accrual per worked month, in basis points of a day.</summary>
    public int AccrualPerMonthBp { get; set; }
    public int MaxCarryForwardBp { get; set; }
    public bool Encashable { get; set; }
    /// <summary>Whether weekly offs and holidays inside a leave span are counted. BD practice varies.</summary>
    public bool CountsSandwichedDays { get; set; }
    public int MinNoticeDays { get; set; }
    public int MaxConsecutiveDays { get; set; }
    public bool RequiresAttachment { get; set; }
    /// <summary>How many approval tiers this leave walks — resolves the §11 vs §5A-16 contradiction as data.</summary>
    public int ApprovalTiers { get; set; } = 2;
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>
/// An employee's balance of one leave type for one leave year. Opening/accrued/availed/encashed are
/// kept separately so a balance can always be explained rather than merely asserted.
/// </summary>
public class LeaveBalance
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public long LeaveTypeId { get; set; }
    public int LeaveYear { get; set; }
    public int OpeningBp { get; set; }
    public int AccruedBp { get; set; }
    public int AvailedBp { get; set; }
    public int EncashedBp { get; set; }
    public int AdjustmentBp { get; set; }
    public int AvailableBp => OpeningBp + AccruedBp + AdjustmentBp - AvailedBp - EncashedBp;
    public uint Version { get; set; }   // xmin concurrency token (ADR-0015)
}

/// <summary>One request, walking §11's states. Approvals ride the M21 engine (ADR: approval policy rows).</summary>
public class LeaveApplication
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string ApplicationNo { get; set; }
    public long EmployeeId { get; set; }
    public long LeaveTypeId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    /// <summary>Days requested in basis points — half-days are 5000, not a bool.</summary>
    public int DaysBp { get; set; }
    public required string Reason { get; set; }
    public string? AttachmentPath { get; set; }
    public string State { get; set; } = LeaveApplicationState.Applied;

    public long? RecommendedBy { get; set; }
    public DateTimeOffset? RecommendedAt { get; set; }
    public long? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    /// <summary>Set when the chain routes through the kernel approval engine (delegation, escalation).</summary>
    public long? ApprovalRequestId { get; set; }

    public DateTimeOffset AppliedAt { get; set; }
    public long AppliedBy { get; set; }
    public required string AppliedByName { get; set; }
}

/// <summary>Leave sold back for cash. Lands on the salary sheet as an earning component.</summary>
public class LeaveEncashment
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public long LeaveTypeId { get; set; }
    public int LeaveYear { get; set; }
    public int DaysBp { get; set; }
    public long AmountTaka { get; set; }
    public long? PayrollRunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}
