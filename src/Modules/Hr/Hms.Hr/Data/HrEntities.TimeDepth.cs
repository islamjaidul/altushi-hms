namespace Hms.Hr.Data;

// Spec 0058 — the surface around the attendance engine. The derivation was always good; what was
// missing was everything an operator does with it: approving the overtime, banking it, correcting a
// day at the employee's own request, building a month's roster in fewer than 1,200 clicks, and
// knowing that the clock on the third floor stopped reporting on Tuesday.
//
// ADR-0027 still holds. The grace period, the late policy, the OT multiplier, the comp-off expiry
// window and the coverage requirement are all employer configuration.

// ---- devices (G22, §13 I8) ----------------------------------------------------------------------

public static class DeviceHealth
{
    public const string Healthy = "healthy";
    public const string Late = "late";
    public const string Silent = "silent";
    public const string NeverHeard = "never_heard";

    public static string Label(string health) => health switch
    {
        Healthy => "Reporting",
        Late => "Late",
        Silent => "Silent",
        NeverHeard => "Never reported",
        _ => health,
    };
}

/// <summary>
/// A punch source that is a machine (§13 I8, G22).
/// <para>
/// The <b>protocol</b> is deliberately absent. §9A.3 excluded the live feed because nobody has held
/// the hardware, and hard rule 3 forbids asserting a vendor protocol this project cannot verify. What
/// ships is the registry, the health surface, and an authenticated endpoint a device or a site agent
/// posts punches to. A vendor-specific poller is a spec written with a device on the desk.
/// </para>
/// </summary>
public class AttendanceDevice
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Location { get; set; }
    public string? SerialNo { get; set; }
    /// <summary>Which site this clock stands at, when the employer has more than one.</summary>
    public long? WorkLocationId { get; set; }

    /// <summary>
    /// How often this device is expected to report, in minutes. Silence beyond twice this is the
    /// difference between "quiet night shift" and "the clock is unplugged".
    /// </summary>
    public int ExpectedIntervalMinutes { get; set; } = 60;

    /// <summary>Hashed, never stored in the clear — the same treatment any credential gets.</summary>
    public string? PushKeyHash { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }
    public int LastBatchPunches { get; set; }
    public bool Active { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }

    /// <summary>
    /// How this device reads right now. Pure, so the register, the command centre and the alert all
    /// say the same words about the same silence.
    /// </summary>
    public static string HealthOf(DateTimeOffset? lastSeen, int expectedMinutes, DateTimeOffset now)
    {
        if (lastSeen is not { } seen) return DeviceHealth.NeverHeard;

        var quiet = now - seen;
        var expected = TimeSpan.FromMinutes(Math.Max(1, expectedMinutes));
        if (quiet <= expected) return DeviceHealth.Healthy;
        return quiet <= expected * 2 ? DeviceHealth.Late : DeviceHealth.Silent;
    }
}

// ---- requests (G23, G24, G25, G28, G29) -----------------------------------------------------------

/// <summary>
/// The one state machine every employee-raised request in this module shares. §9 gives each its own
/// list; they are the same four words, and four near-identical state machines would drift.
/// </summary>
public static class RequestState
{
    public const string Raised = "raised";
    public const string Recommended = "recommended";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Cancelled = "cancelled";
    /// <summary>Approved and then acted on — the correction written, the comp-off taken.</summary>
    public const string Applied = "applied";

    public static string Label(string state) => state switch
    {
        Raised => "Raised",
        Recommended => "Recommended",
        Approved => "Approved",
        Rejected => "Rejected",
        Cancelled => "Cancelled",
        Applied => "Applied",
        _ => state,
    };

    public static string Tone(string state) => state switch
    {
        Approved or Applied => "good",
        Rejected or Cancelled => "bad",
        _ => "warn",
    };
}

/// <summary>
/// The employee's own correction request (§7.4, G23). Its approval calls the <b>same</b>
/// <c>AttendanceService.CorrectAsync</c> the HR screen calls — a second write path would be a second
/// place for the arrears rule and the audit tier to drift.
/// </summary>
public class RegularizationRequest
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly OnDate { get; set; }

    /// <summary>What the employee says the day should be — an <see cref="AttendanceStatus"/>.</summary>
    public required string RequestedStatus { get; set; }
    public DateTimeOffset? RequestedIn { get; set; }
    public DateTimeOffset? RequestedOut { get; set; }
    /// <summary>Required. It becomes the correction's reason, which is what an auditor reads.</summary>
    public required string Reason { get; set; }

    public string State { get; set; } = RequestState.Raised;
    public string? DecisionNote { get; set; }
    public long? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    /// <summary>The correction the approval produced. Null until then.</summary>
    public long? CorrectionId { get; set; }

    public DateTimeOffset RaisedAt { get; set; }
    public long RaisedBy { get; set; }
    public required string RaisedByName { get; set; }
}

public static class OvertimeApprovalKind
{
    /// <summary>Asked for before the hours are worked — the employer's preferred shape.</summary>
    public const string Pre = "pre";
    /// <summary>Approved afterwards from the exception list, because the night happened.</summary>
    public const string Post = "post";
}

/// <summary>
/// Overtime, approved (§5A-16, G24). Until now OT minutes were derived and paid and nobody signed.
/// </summary>
public class OvertimeRequest
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly OnDate { get; set; }
    public required string Kind { get; set; }              // OvertimeApprovalKind
    public int Minutes { get; set; }
    public string? Reason { get; set; }
    public string State { get; set; } = RequestState.Raised;
    /// <summary>Set on approval: banked rather than paid, per the rule in force.</summary>
    public bool Banked { get; set; }

    public long? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    public DateTimeOffset RaisedAt { get; set; }
    public long RaisedBy { get; set; }
    public required string RaisedByName { get; set; }
}

public static class OvertimeBankKind
{
    /// <summary>Minutes banked instead of paid.</summary>
    public const string Credit = "credit";
    /// <summary>Minutes spent as comp-off.</summary>
    public const string Debit = "debit";
    /// <summary>Minutes that ran out of time, per the employer's window.</summary>
    public const string Expiry = "expiry";
}

/// <summary>
/// The OT bank (§5A-16, G25). Append-only: a balance is the sum of its movements, never a column
/// somebody updates — the same discipline the member ledger follows, and for the same reason.
/// <para>
/// <c>OvertimeRule.BankInsteadOfPay</c> shipped with the module, was written by the Policies screen,
/// and was read by nothing. This is what reads it.
/// </para>
/// </summary>
public class OvertimeBankEntry
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public required string Kind { get; set; }              // OvertimeBankKind
    public DateOnly OnDate { get; set; }
    /// <summary>Always positive; <see cref="Kind"/> carries the direction.</summary>
    public int Minutes { get; set; }
    public required string Narration { get; set; }
    /// <summary>When a credit lapses. Null on a debit or an expiry.</summary>
    public DateOnly? ExpiresOn { get; set; }
    public long? OvertimeRequestId { get; set; }
    public long? CompOffRequestId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public long RecordedBy { get; set; }
}

/// <summary>A day off taken against banked overtime (§5A-16, G25).</summary>
public class CompOffRequest
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly OnDate { get; set; }
    /// <summary>What the day costs the bank. A half day is half a standard shift.</summary>
    public int Minutes { get; set; }
    public string? Reason { get; set; }
    public string State { get; set; } = RequestState.Raised;

    public long? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    public DateTimeOffset RaisedAt { get; set; }
    public long RaisedBy { get; set; }
    public required string RaisedByName { get; set; }
}

public static class ShortLeaveKind
{
    public const string ShortLeave = "short_leave";
    public const string OutdoorDuty = "outdoor_duty";

    public static readonly string[] All = [ShortLeave, OutdoorDuty];

    public static string Label(string kind) =>
        kind == OutdoorDuty ? "Outdoor duty" : "Short leave";
}

/// <summary>
/// An hourly absence attendance honours without consuming a leave day (§7.4, G29). Lands on
/// <c>AttendanceStatus.Errand</c> — a status that shipped with the module and had no producer.
/// </summary>
public class ShortLeaveRequest
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly OnDate { get; set; }
    public required string Kind { get; set; }              // ShortLeaveKind
    public TimeOnly FromTime { get; set; }
    public TimeOnly ToTime { get; set; }
    public required string Reason { get; set; }
    public string State { get; set; } = RequestState.Raised;

    public long? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    public DateTimeOffset RaisedAt { get; set; }
    public long RaisedBy { get; set; }
    public required string RaisedByName { get; set; }

    public int Minutes => Math.Max(0, (int)(ToTime - FromTime).TotalMinutes);
}

/// <summary>Two people trading a shift, with a manager's sign-off (§7.5, G28).</summary>
public class ShiftSwapRequest
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long RequesterEmployeeId { get; set; }
    public long CounterpartEmployeeId { get; set; }
    public DateOnly RequesterDate { get; set; }
    public DateOnly CounterpartDate { get; set; }
    public string? Reason { get; set; }
    public string State { get; set; } = RequestState.Raised;

    public long? DecidedBy { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    public DateTimeOffset RaisedAt { get; set; }
    public long RaisedBy { get; set; }
    public required string RaisedByName { get; set; }
}

// ---- roster patterns and coverage (G27, §7.5) --------------------------------------------------------

/// <summary>
/// A rotating cycle — "6 days morning, 1 off, 6 evening, 1 off" — applied to a team over a period
/// (§5 M16's "24/7 rotating shifts", G27). Building a 40-nurse month one cell at a time is 1,200
/// clicks, which is why nobody does it and the roster is a piece of paper on a wall.
/// </summary>
public class RosterPattern
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>
/// One day of a cycle. A null <see cref="ShiftId"/> is a rest day, which is what makes a pattern a
/// pattern rather than a list of shifts.
/// </summary>
public class RosterPatternStep
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long PatternId { get; set; }
    /// <summary>0-based position in the cycle.</summary>
    public int Ordinal { get; set; }
    public long? ShiftId { get; set; }
    /// <summary>How many consecutive days this step covers. 6 morning days is one step, not six.</summary>
    public int Days { get; set; } = 1;
}

/// <summary>
/// How many people a shift needs, per unit per weekday (§7.5's coverage). Zero configured means the
/// employer has no view, and coverage reports what is rostered without claiming a shortfall.
/// </summary>
public class ShiftRequirement
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long OrgUnitId { get; set; }
    public long ShiftId { get; set; }
    /// <summary>Null means every day of the week.</summary>
    public DayOfWeek? Weekday { get; set; }
    public int RequiredHeadcount { get; set; }
}
