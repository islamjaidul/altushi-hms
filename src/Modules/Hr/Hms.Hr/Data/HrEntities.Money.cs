namespace Hms.Hr.Data;

// Spec 0057 — what happens to the money after the payslip is computed: it is held, reviewed,
// approved, recovered against, and finally paid to somebody by some method.
//
// ADR-0027 still holds. Bonus basis, increment percentage, variance tolerance and PF interest are
// all employer configuration carrying an effective date. Nothing in this file knows a Bangladeshi
// number, and the tables ship empty.

/// <summary>How an employee is actually paid. Drives the disbursement split (§5A-15, G45).</summary>
public static class PaymentMethod
{
    public const string Bank = "bank";
    public const string Cash = "cash";
    public const string Cheque = "cheque";

    public static readonly string[] All = [Bank, Cash, Cheque];

    public static string Label(string method) => method switch
    {
        Bank => "Bank transfer",
        Cash => "Cash",
        Cheque => "Cheque",
        _ => method,
    };
}

// ---- salary hold (G46) ---------------------------------------------------------------------------

/// <summary>
/// One employee's salary withheld, with a reason (§7.7, G46).
/// <para>
/// A held person is <b>never silently absent</b> from a run: the line is still computed, still
/// carries its gross and its deductions, and is marked held with a zero net. Omitting them would
/// make the sheet's headcount lie, and the reason nobody was paid would be invisible.
/// </para>
/// </summary>
public class SalaryHold
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public required string Reason { get; set; }
    public DateTimeOffset HeldAt { get; set; }
    public long HeldBy { get; set; }
    public required string HeldByName { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public long? ReleasedBy { get; set; }
    public string? ReleaseReason { get; set; }
}

// ---- variance review (G47, §9) --------------------------------------------------------------------

/// <summary>
/// One employee's pay compared with their previous run. §9 puts <b>Variance Reviewed</b> between
/// Exceptions Reviewed and Approved, and the module went straight from one to the other — so a run
/// where somebody's pay tripled was approved exactly as fast as a run where nothing moved.
/// </summary>
public class VarianceNote
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long RunId { get; set; }
    public long EmployeeId { get; set; }
    public long PreviousNetTaka { get; set; }
    public long CurrentNetTaka { get; set; }
    /// <summary>Change in basis points of the previous net. 10000 = doubled.</summary>
    public int DeltaBp { get; set; }
    /// <summary>Set only where the change exceeded the employer's tolerance and had to be explained.</summary>
    public string? Reason { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public long? ReviewedBy { get; set; }
    public string? ReviewedByName { get; set; }
}

// ---- bonus (G38) ------------------------------------------------------------------------------------

public static class BonusKind
{
    public const string Festival = "festival";
    public const string Performance = "performance";
    public const string AdHoc = "ad_hoc";

    public static readonly string[] All = [Festival, Performance, AdHoc];

    public static string Label(string kind) => kind switch
    {
        Festival => "Festival bonus",
        Performance => "Performance bonus",
        AdHoc => "Ad-hoc bonus",
        _ => kind,
    };
}

public static class BonusState
{
    public const string Drafted = "drafted";
    public const string Reviewed = "reviewed";
    public const string Approved = "approved";
    public const string Paid = "paid";
    public const string Cancelled = "cancelled";

    public static readonly string[] Order = [Drafted, Reviewed, Approved, Paid];

    public static string Label(string state) => state switch
    {
        Drafted => "Drafted",
        Reviewed => "Eligibility reviewed",
        Approved => "Approved",
        Paid => "Paid",
        Cancelled => "Cancelled",
        _ => state,
    };
}

/// <summary>
/// A bonus sheet (§7.7, G38). Eligibility and basis are the employer's: a festival bonus of one
/// month's basic after one year of service is a common Bangladeshi arrangement and is <b>not</b>
/// shipped as a default — the employer states both, and the sheet records what they stated.
/// </summary>
public class BonusSheet
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string SheetNo { get; set; }
    public required string Kind { get; set; }              // BonusKind
    public required string Title { get; set; }
    /// <summary>The month this bonus belongs to, for reporting and for attaching it to a run.</summary>
    public DateOnly Period { get; set; }
    public string State { get; set; } = BonusState.Drafted;

    /// <summary>Component the bonus is a percentage of. Null means a flat amount for everyone.</summary>
    public long? BasedOnComponentId { get; set; }
    /// <summary>Percentage of the base, in basis points. 10000 = one month of the base.</summary>
    public long PercentBp { get; set; }
    /// <summary>Used when no base component is chosen.</summary>
    public long FlatAmountTaka { get; set; }

    /// <summary>Completed months of service required. 0 = everyone.</summary>
    public int MinimumServiceMonths { get; set; }
    /// <summary>Comma-separated <see cref="EmploymentType"/> values; empty means every type.</summary>
    public string EligibleTypes { get; set; } = "";

    public long TotalTaka { get; set; }
    public int EligibleCount { get; set; }
    public int ExcludedCount { get; set; }

    public long? ApprovalRequestId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public long? ApprovedBy { get; set; }
    /// <summary>The run that paid it. A bonus is never paid outside payroll.</summary>
    public long? PaidRunId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

/// <summary>
/// One person on a bonus sheet — including the people who did <b>not</b> qualify. An excluded row
/// with a stated reason is the difference between "she was not eligible" and "she was forgotten".
/// </summary>
public class BonusLine
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long SheetId { get; set; }
    public long EmployeeId { get; set; }
    public bool Eligible { get; set; }
    public long AmountTaka { get; set; }
    public string? Basis { get; set; }
    /// <summary>Why this person gets nothing. Required when <see cref="Eligible"/> is false.</summary>
    public string? ExclusionReason { get; set; }
}

// ---- increment and promotion (G39, G40) -------------------------------------------------------------

public static class CompensationRunKind
{
    public const string Increment = "increment";
    /// <summary>Designation and grade move with the pay, in one action (G40).</summary>
    public const string Promotion = "promotion";

    public static readonly string[] All = [Increment, Promotion];

    public static string Label(string kind) => kind == Promotion ? "Promotion" : "Increment";
}

public static class CompensationRunState
{
    public const string Proposed = "proposed";
    public const string Previewed = "previewed";
    public const string Approved = "approved";
    public const string Applied = "applied";
    public const string Cancelled = "cancelled";

    public static readonly string[] Order = [Proposed, Previewed, Approved, Applied];

    public static string Label(string state) => state switch
    {
        Proposed => "Proposed",
        Previewed => "Previewed",
        Approved => "Approved",
        Applied => "Applied",
        Cancelled => "Cancelled",
        _ => state,
    };
}

/// <summary>
/// §9's increment run, carrying promotion too. A promotion <em>is</em> an increment that also moves
/// the designation and the grade; two tables would need a rule about which is authoritative when
/// both change someone's pay on the same date, and there is no good answer to that question.
/// </summary>
public class CompensationRun
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string RunNo { get; set; }
    public required string Kind { get; set; }              // CompensationRunKind
    public required string Title { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string State { get; set; } = CompensationRunState.Proposed;

    // ---- the cohort. Null means "no filter on this axis".
    public long? OrgUnitId { get; set; }
    public long? GradeId { get; set; }
    public long? DesignationId { get; set; }
    /// <summary>Completed months of service required to be included.</summary>
    public int MinimumServiceMonths { get; set; }

    // ---- the policy. Percentage of current gross, or a flat addition.
    public long PercentBp { get; set; }
    public long FlatAmountTaka { get; set; }

    /// <summary>Promotion only: where everybody in the cohort moves to.</summary>
    public long? NewDesignationId { get; set; }
    public long? NewGradeId { get; set; }

    public int AffectedCount { get; set; }
    public long TotalIncreaseTaka { get; set; }

    public long? ApprovalRequestId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public long? AppliedBy { get; set; }
    public int LettersIssued { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

/// <summary>One person's before and after. Written at preview, so approval sees exactly what applies.</summary>
public class CompensationLine
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long RunId { get; set; }
    public long EmployeeId { get; set; }
    public long OldGrossTaka { get; set; }
    public long NewGrossTaka { get; set; }
    public long? OldDesignationId { get; set; }
    public long? NewDesignationId { get; set; }
    public long? OldGradeId { get; set; }
    public long? NewGradeId { get; set; }
    public string? Basis { get; set; }
    public bool Applied { get; set; }
    public string? Skipped { get; set; }
}

// ---- disbursement (G45) --------------------------------------------------------------------------

public static class DisbursementState
{
    public const string Prepared = "prepared";
    public const string PartlyPaid = "partly_paid";
    public const string Paid = "paid";

    public static string Label(string state) => state switch
    {
        Prepared => "Prepared",
        PartlyPaid => "Partly paid",
        Paid => "Paid",
        _ => state,
    };
}

/// <summary>
/// A run's money, split by how it is actually handed over (§5A-15, G45). One batch per run: the
/// question "has March been paid" must have one answer.
/// </summary>
public class Disbursement
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long RunId { get; set; }
    public required string BatchNo { get; set; }
    public string State { get; set; } = DisbursementState.Prepared;
    public DateOnly ValueDate { get; set; }
    public long TotalTaka { get; set; }
    public long BankTaka { get; set; }
    public long CashTaka { get; set; }
    public long ChequeTaka { get; set; }
    public int LineCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

/// <summary>
/// One person's payment. Bank details are <b>snapshotted</b>: a bank file reproduced next year must
/// show the account it actually paid, not the one the employee has since changed to — the same rule
/// a payslip follows when it snapshots component names.
/// </summary>
public class DisbursementLine
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long DisbursementId { get; set; }
    public long PayrollLineId { get; set; }
    public long EmployeeId { get; set; }
    public required string EmployeeCode { get; set; }
    public required string EmployeeName { get; set; }
    public required string Method { get; set; }            // PaymentMethod
    public long AmountTaka { get; set; }
    public string? BankName { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankRoutingNo { get; set; }
    /// <summary>Cheque number, transfer reference, or the cash sheet's row — whatever proves it.</summary>
    public string? Reference { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public long? PaidBy { get; set; }
    public string? PaidByName { get; set; }
}
