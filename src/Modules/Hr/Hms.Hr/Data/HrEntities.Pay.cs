namespace Hms.Hr.Data;

// Payroll. §11: Generated → Exceptions Reviewed → Approved⚿ → Locked → Posted to Accounts.
// Hard rule 4 governs everything below: a locked run is never edited. A mistake found afterwards is
// a reversal run that references the original, and a late attendance correction becomes an arrears
// line in the next run. Money is long whole taka throughout (C3) — there is no decimal in this file.

public static class PayrollRunState
{
    public const string Generated = "generated";
    public const string ExceptionsReviewed = "exceptions_reviewed";
    public const string Approved = "approved";
    public const string Locked = "locked";
    public const string Posted = "posted";
    public const string Cancelled = "cancelled";
}

public static class PayrollRunKind
{
    public const string Regular = "regular";
    /// <summary>Undoes a locked run in full, referencing it. The only way to "change" a locked run.</summary>
    public const string Reversal = "reversal";
    /// <summary>Off-cycle run for arrears, final settlement or a missed joiner.</summary>
    public const string Supplementary = "supplementary";
}

/// <summary>
/// One payroll period for one branch. UNIQUE(branch, period, kind, sequence) plus guarded state
/// transitions is what stops two HR officers double-running a month.
/// </summary>
public class PayrollRun
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string RunNo { get; set; }
    /// <summary>First day of the payroll month. DateOnly, so "which month" is never ambiguous.</summary>
    public DateOnly Period { get; set; }
    public required string Kind { get; set; }          // PayrollRunKind
    /// <summary>Distinguishes repeated supplementary runs in one period.</summary>
    public int Sequence { get; set; } = 1;
    public string State { get; set; } = PayrollRunState.Generated;

    public long? ReversalOfRunId { get; set; }
    /// <summary>The kernel approval request that gated the lock (§12: HR Officer → Accounts Manager / MD).</summary>
    public long? ApprovalRequestId { get; set; }

    public int EmployeeCount { get; set; }
    public long TotalGrossTaka { get; set; }
    public long TotalDeductionTaka { get; set; }
    public long TotalNetTaka { get; set; }
    public long TotalEmployerCostTaka { get; set; }

    /// <summary>Unresolved attendance exceptions at generation — US16.1's "pre-listed for my review".</summary>
    public int ExceptionCount { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
    public long GeneratedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public long? ReviewedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTimeOffset? LockedAt { get; set; }
    public long? LockedBy { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public long? PostedBy { get; set; }
    /// <summary>The journal handed to M15, stored even when no ledger exists to receive it yet.</summary>
    public string? JournalJson { get; set; }
}

/// <summary>
/// One employee's pay for one run. <see cref="PayStructureId"/> and <see cref="PolicyStampJson"/>
/// pin exactly which effective-dated rows produced these figures, so reproduction never depends on
/// today's resolution logic still agreeing with the past (hard rule 5, ADR-0027).
/// </summary>
public class PayrollLine
{
    public long Id { get; set; }
    public long RunId { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public required string EmployeeCode { get; set; }
    public required string EmployeeName { get; set; }
    public long? OrgUnitId { get; set; }
    public long? DesignationId { get; set; }
    public long? GradeId { get; set; }
    public long? PayStructureId { get; set; }
    public string? PolicyStampJson { get; set; }

    /// <summary>Days in the period this employee was actually on the payroll (mid-month join/exit).</summary>
    public int PeriodDays { get; set; }
    public int PayableDaysBp { get; set; }
    public int PresentDaysBp { get; set; }
    public int AbsentDaysBp { get; set; }
    public int LeaveDaysBp { get; set; }
    /// <summary>
    /// The unpaid subset of <see cref="LeaveDaysBp"/> — days on a leave type whose
    /// <c>Paid</c> flag is false (spec 0052). Deliberately a subset rather than a sibling count:
    /// runs locked before 0052 recorded every leave day in <see cref="LeaveDaysBp"/>, and changing
    /// what that column means would make two runs disagree about the same word.
    /// </summary>
    public int LeaveWithoutPayDaysBp { get; set; }
    public int LateCount { get; set; }
    public int OvertimeMinutes { get; set; }

    public long GrossEarningsTaka { get; set; }
    public long TotalDeductionsTaka { get; set; }
    public long NetPayTaka { get; set; }
    public long EmployerCostTaka { get; set; }
    /// <summary>Set when deductions would have driven net below the floor; carried on the loan ledger.</summary>
    public long CarriedShortfallTaka { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// One component on one payslip. The component's code and name are snapshotted because renaming a
/// component next year must not rewrite last year's payslip.
/// </summary>
public class PayrollComponentLine
{
    public long Id { get; set; }
    public long PayrollLineId { get; set; }
    public long RunId { get; set; }
    public long ComponentId { get; set; }
    public required string ComponentCode { get; set; }
    public required string ComponentName { get; set; }
    public required string Kind { get; set; }          // PayComponentKind
    public long AmountTaka { get; set; }
    public int DisplayOrder { get; set; }
    /// <summary>Why this number: "12 OT minutes @ rule 3", "loan #7 installment 4/12", "arrears for run 22".</summary>
    public string? Basis { get; set; }
    public long? SourceRefId { get; set; }
}

/// <summary>An issued payslip. Numbered so it can be referred to; owned by the employee for access.</summary>
public class Payslip
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long PayrollLineId { get; set; }
    public long EmployeeId { get; set; }
    public required string PayslipNo { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public long IssuedBy { get; set; }
}

// ---- Wave C money ledgers are declared here so the schema they hang off is stable, but the screens
// that drive them belong to spec 0038. A table with no UI is inert; a table added later is a
// migration against live payroll data.

public static class LoanState
{
    public const string Requested = "requested";
    public const string Approved = "approved";
    public const string Disbursed = "disbursed";
    public const string Closed = "closed";
    public const string WrittenOff = "written_off";
}

public class Loan
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string LoanNo { get; set; }
    public long EmployeeId { get; set; }
    public required string Kind { get; set; }          // loan | advance
    public long PrincipalTaka { get; set; }
    public long InstallmentTaka { get; set; }
    public int InstallmentCount { get; set; }
    public DateOnly StartPeriod { get; set; }
    public string State { get; set; } = LoanState.Requested;
    public long RecoveredTaka { get; set; }
    /// <summary>Unrecovered installments deferred by the negative-net-pay floor.</summary>
    public long CarriedTaka { get; set; }
    public long? ApprovalRequestId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

public class LoanInstallment
{
    public long Id { get; set; }
    public long LoanId { get; set; }
    public long? PayrollRunId { get; set; }
    public DateOnly Period { get; set; }
    public long AmountTaka { get; set; }
    public bool Deferred { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public static class LedgerKind
{
    public const string ProvidentFund = "pf";
    public const string Welfare = "welfare";
    public const string Tax = "tax";

    /// <summary>
    /// Money paid out beyond what the period earned, recoverable from the employee — today only
    /// the minimum-net-pay shortfall (spec 0052 WP2). The payroll journal debits it as an asset,
    /// so a row here is what makes that debit true rather than an assertion about a debt nothing
    /// records.
    /// </summary>
    public const string Advance = "advance";
}

/// <summary>
/// Append-only member ledger for PF, welfare and withheld tax (§3.4). One table with a kind, because
/// all three are the same shape and three near-identical tables would drift.
/// </summary>
public class EmployeeLedgerEntry
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public required string Kind { get; set; }          // LedgerKind
    public DateOnly OnDate { get; set; }
    public required string Narration { get; set; }
    /// <summary>Employee-side movement. Positive credits the member, negative debits.</summary>
    public long EmployeeShareTaka { get; set; }
    public long EmployerShareTaka { get; set; }
    public long? PayrollRunId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long RecordedBy { get; set; }
}
