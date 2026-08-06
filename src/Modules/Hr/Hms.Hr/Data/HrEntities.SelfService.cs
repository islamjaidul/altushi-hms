namespace Hms.Hr.Data;

// Spec 0059 — the last phase. Four specs built a great deal of capability and gave all of it to HR;
// this is the employee's and the manager's own door onto it, plus the leave year that nothing has
// ever rolled, plus the [S]/[C] set §17 put at the end.

// ---- profile change requests (G54) ------------------------------------------------------------

/// <summary>
/// The employee proposes a correction to their own record; HR approves it (§7.10, G54). Until now HR
/// edited everything, including a phone number the employee is the only person who actually knows.
/// <para>
/// The fields are deliberately a short list: contact, address, emergency contact, bank. Nobody
/// proposes their own joining date or their own grade.
/// </para>
/// </summary>
public class ProfileChangeRequest
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }

    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? PresentAddress { get; set; }
    public string? EmergencyContact { get; set; }
    public string? BankName { get; set; }
    public string? BankBranch { get; set; }
    public string? BankAccountNo { get; set; }
    public string? BankRoutingNo { get; set; }

    public string State { get; set; } = RequestState.Raised;
    public string? DecisionNote { get; set; }
    public long? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    public DateTimeOffset RaisedAt { get; set; }
    public long RaisedBy { get; set; }
    public required string RaisedByName { get; set; }
}

// ---- the leave year (G33, G34, G35) -------------------------------------------------------------

public static class LeaveCloseState
{
    public const string Proposed = "proposed";
    public const string Previewed = "previewed";
    public const string Committed = "committed";
    public const string Cancelled = "cancelled";

    public static string Label(string state) => state switch
    {
        Proposed => "Proposed",
        Previewed => "Previewed",
        Committed => "Committed",
        Cancelled => "Cancelled",
        _ => state,
    };
}

/// <summary>
/// One leave year closing (§7.6, G33). "Never partially applied" is the requirement, so the preview
/// is stored and the commit walks it in one transaction — a close that half-ran would leave some
/// people with next year's entitlement and some without, and no way to tell which.
/// </summary>
public class LeaveYearClose
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    /// <summary>The year being closed, as its start date.</summary>
    public DateOnly ClosingYear { get; set; }
    public DateOnly OpeningYear { get; set; }
    public string State { get; set; } = LeaveCloseState.Proposed;

    public int EmployeeCount { get; set; }
    public int AccruedBp { get; set; }
    public int CarriedBp { get; set; }
    public int LapsedBp { get; set; }
    public int EncashedBp { get; set; }

    public DateTimeOffset? PreviewedAt { get; set; }
    public DateTimeOffset? CommittedAt { get; set; }
    public long? CommittedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

/// <summary>One employee's one leave type in a close. Written at preview, applied at commit.</summary>
public class LeaveCloseLine
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long CloseId { get; set; }
    public long EmployeeId { get; set; }
    public long LeaveTypeId { get; set; }

    public int ClosingBalanceBp { get; set; }
    public int AccrueBp { get; set; }
    public int CarryBp { get; set; }
    public int LapseBp { get; set; }
    public int EncashBp { get; set; }
    public string? Basis { get; set; }
    public bool Applied { get; set; }
}

// ---- the [S] set: appraisal, training, discipline, assets ----------------------------------------

public static class AppraisalState
{
    public const string Open = "open";
    public const string SelfRated = "self_rated";
    public const string ManagerRated = "manager_rated";
    public const string Closed = "closed";

    public static string Label(string s) => s switch
    {
        Open => "Open",
        SelfRated => "Self-rated",
        ManagerRated => "Manager-rated",
        Closed => "Closed",
        _ => s,
    };
}

/// <summary>
/// A light appraisal (§7.14 `[S]`, G60): a period, a self rating, a manager rating, an outcome.
/// <para>
/// The <b>scale is the employer's</b> — this ships no opinion about whether performance runs 1–5 or
/// A–E, and no competency framework. A rating is an integer between the employer's own bounds and a
/// paragraph of words.
/// </para>
/// </summary>
public class Appraisal
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public required string Cycle { get; set; }
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public string State { get; set; } = AppraisalState.Open;

    public int? SelfRating { get; set; }
    public string? SelfComment { get; set; }
    public DateTimeOffset? SelfRatedAt { get; set; }

    public int? ManagerRating { get; set; }
    public string? ManagerComment { get; set; }
    public long? ManagerId { get; set; }
    public string? ManagerName { get; set; }
    public DateTimeOffset? ManagerRatedAt { get; set; }

    public string? Outcome { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>
/// Training attended and certification held, with expiry (§7.14 `[S]`, G59). `[hospital]`: the
/// question is "who is out of compliance", which is the same expiry engine spec 0056 built for
/// documents, applied to a different noun.
/// </summary>
public class TrainingRecord
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public required string Title { get; set; }
    public string? Provider { get; set; }
    public DateOnly CompletedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public string? CertificateNo { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

public static class DisciplinaryKind
{
    public const string ShowCause = "show_cause";
    public const string Warning = "warning";
    public const string Suspension = "suspension";
    public const string Resolution = "resolution";

    public static readonly string[] All = [ShowCause, Warning, Suspension, Resolution];

    public static string Label(string kind) => kind switch
    {
        ShowCause => "Show-cause notice",
        Warning => "Warning",
        Suspension => "Suspension",
        Resolution => "Resolution",
        _ => kind,
    };
}

/// <summary>
/// A disciplinary action (§7.14 `[S]`, G61). <b>Permission-restricted and hidden entirely without
/// it</b> — not greyed out, not summarised, absent. §11 gives it its own claim precisely so that a
/// manager browsing a record cannot infer that something exists which they may not read.
/// </summary>
public class DisciplinaryAction
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public required string Kind { get; set; }              // DisciplinaryKind
    public DateOnly OnDate { get; set; }
    public required string Subject { get; set; }
    public string? Detail { get; set; }
    public string? EmployeeResponse { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>Points at the show-cause a warning answers, so the sequence reads as a sequence.</summary>
    public long? SupersedesId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

/// <summary>What the employer owns and lends out (§7.1 `[S]`, G17).</summary>
public class Asset
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Category { get; set; }
    public string? SerialNo { get; set; }
    public long ValueTaka { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>
/// One asset in one person's hands. Unreturned assets surface at clearance, which is the whole
/// reason §7.1 asks for this — spec 0056's clearance carries a recoverable-dues figure and until now
/// nothing told it what was outstanding.
/// </summary>
public class AssetIssue
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long AssetId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly IssuedOn { get; set; }
    public long IssuedBy { get; set; }
    public required string IssuedByName { get; set; }
    public DateOnly? ReturnedOn { get; set; }
    public long? ReturnedTo { get; set; }
    public string? Condition { get; set; }
    /// <summary>Set when it comes back damaged or not at all. Feeds the clearance checklist.</summary>
    public long RecoverableTaka { get; set; }
    public string? Note { get; set; }
}

// ---- the [C] set: notices, expenses ---------------------------------------------------------------

/// <summary>An employer announcement (§7.10 `[C]`, G55).</summary>
public class Notice
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Title { get; set; }
    public required string Body { get; set; }
    public DateOnly PublishFrom { get; set; }
    public DateOnly? PublishTo { get; set; }
    /// <summary>Whether an employee has to say they have read it.</summary>
    public bool RequiresAcknowledgement { get; set; }
    /// <summary>Null means everybody.</summary>
    public long? OrgUnitId { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

/// <summary>One person saying they read one notice. Append-only; there is no un-reading.</summary>
public class NoticeAcknowledgement
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long NoticeId { get; set; }
    public long EmployeeId { get; set; }
    public DateTimeOffset AcknowledgedAt { get; set; }
}

/// <summary>
/// An expense claim (§7.9 `[C]`, G56). The claim, its approval and its state live here; wiring the
/// reimbursement into a payroll run as a component is a payroll change, and that phase is closed.
/// </summary>
public class ExpenseClaim
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public required string ClaimNo { get; set; }
    public DateOnly IncurredOn { get; set; }
    public required string Category { get; set; }
    public required string Description { get; set; }
    public long AmountTaka { get; set; }
    public string? AttachmentPath { get; set; }
    public string State { get; set; } = RequestState.Raised;

    public long? ApprovedAmountTaka { get; set; }
    public long? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    /// <summary>How it was actually reimbursed, once it was: a run, petty cash, or a note.</summary>
    public string? ReimbursedVia { get; set; }
    public DateTimeOffset? ReimbursedAt { get; set; }

    public DateTimeOffset RaisedAt { get; set; }
    public long RaisedBy { get; set; }
    public required string RaisedByName { get; set; }
}

// ---- delegation and saved views (G36, G5) ------------------------------------------------------------

/// <summary>
/// An approver handing their queue to somebody for a date range (§7.6 `[S]`, G36). The chain used to
/// stop dead when the approver went on leave, which is the one week it most needs not to.
/// <para>
/// The kernel already has <c>kernel.approval_delegation</c> for its own typed requests. This is the
/// HR-side equivalent for the request kinds that do not go through the approval engine — leave,
/// regularization, OT, comp-off, swap — and it deliberately does not try to be the same table.
/// </para>
/// </summary>
public class ApproverDelegation
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long FromEmployeeId { get; set; }
    public long ToEmployeeId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public string? Reason { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
    public required string CreatedByName { get; set; }
}

/// <summary>
/// A report, its period and its filters, saved under a name (§12.4 `[S]`, G5 — deferred from 0055
/// until the catalogue had settled).
/// <para>
/// The query string is stored verbatim rather than parsed into columns: the period standard and the
/// filter vocabulary both already own that grammar, and a second parser would be a second thing to
/// keep in step with them.
/// </para>
/// </summary>
public class SavedReportView
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string ReportKey { get; set; }
    public required string Name { get; set; }
    public required string QueryString { get; set; }
    /// <summary>Null means private to its owner; set means the whole branch can see it.</summary>
    public bool Shared { get; set; }
    public long OwnerUserId { get; set; }
    public required string OwnerName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
