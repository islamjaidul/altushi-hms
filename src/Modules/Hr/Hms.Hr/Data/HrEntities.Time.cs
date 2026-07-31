namespace Hms.Hr.Data;

// Attendance. §13 I8 wants a live ZKTeco-class feed, but §9A.3 excludes it because the devices are
// not bought — so Wave A ships file import plus manual entry behind an adapter, and the raw punch
// table is shaped for a device feed to drop into unchanged.

/// <summary>A published roster for one org unit over one period.</summary>
public class Roster
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long OrgUnitId { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public bool Published { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long CreatedBy { get; set; }
}

/// <summary>Which shift an employee is rostered to on a date. Absent row = not rostered.</summary>
public class RosterEntry
{
    public long Id { get; set; }
    public long RosterId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly OnDate { get; set; }
    public long ShiftId { get; set; }
    public bool WeeklyOff { get; set; }
}

public static class PunchSource
{
    public const string Device = "device";
    public const string Import = "import";
    public const string Manual = "manual";
}

/// <summary>
/// A raw punch. Immutable by design: a wrong punch is corrected by an
/// <see cref="AttendanceCorrection"/> that names a reason, never by editing the device's word for
/// what happened. UNIQUE(employee, device, punched_at) is what makes re-importing the same export
/// file a no-op instead of a duplicated day.
/// </summary>
public class Punch
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public required string DeviceId { get; set; }
    public DateTimeOffset PunchedAt { get; set; }
    public required string Source { get; set; }        // PunchSource
    public string? Direction { get; set; }             // in | out | null when the device doesn't say
    public long? ImportBatchId { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public static class AttendanceStatus
{
    public const string Present = "present";
    public const string Absent = "absent";
    public const string WeeklyOff = "weekly_off";
    public const string Holiday = "holiday";
    public const string OnLeave = "on_leave";
    public const string Errand = "errand";
    /// <summary>Punched in but never out (or the reverse): needs a human before payroll can run.</summary>
    public const string Incomplete = "incomplete";
}

/// <summary>
/// One employee-day, derived from punches against the rostered shift. UNIQUE(employee, on_date) is
/// the idempotency anchor — the same role <c>ipd.bed_day</c> plays for bed charges, and for the same
/// reason: a concurrent double-derive loses on the index rather than on luck.
/// <para>
/// <see cref="OnDate"/> is the <em>shift's</em> date, not the punch's calendar date. A night shift
/// starting 22:00 Tuesday and ending 06:00 Wednesday is one Tuesday row.
/// </para>
/// </summary>
public class AttendanceDay
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long EmployeeId { get; set; }
    public DateOnly OnDate { get; set; }
    public long? ShiftId { get; set; }
    public required string Status { get; set; }        // AttendanceStatus
    public DateTimeOffset? FirstIn { get; set; }
    public DateTimeOffset? LastOut { get; set; }
    public int WorkedMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyOutMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    /// <summary>Fraction of a payable day in basis points (10000 = full day) — integers only.</summary>
    public int PayableFractionBp { get; set; } = 10000;
    public long? LeaveApplicationId { get; set; }
    public bool Corrected { get; set; }
    public DateTimeOffset DerivedAt { get; set; }
}

/// <summary>
/// An audited amendment to a derived day. §5 M16 requires review/correction "with reason"; US16.3
/// requires the raw punches to stay visible so a "the machine didn't take my punch" dispute is
/// settled by looking rather than by arguing.
/// </summary>
public class AttendanceCorrection
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public long AttendanceDayId { get; set; }
    public required string FromStatus { get; set; }
    public required string ToStatus { get; set; }
    public int? FromWorkedMinutes { get; set; }
    public int? ToWorkedMinutes { get; set; }
    public required string Reason { get; set; }
    /// <summary>Set when the day belonged to an already-locked payroll run — becomes arrears.</summary>
    public long? ArrearsForRunId { get; set; }
    public bool ArrearsSettled { get; set; }
    public DateTimeOffset CorrectedAt { get; set; }
    public long CorrectedBy { get; set; }
    public required string CorrectedByName { get; set; }
}

/// <summary>A batch of imported punches, so an import can be reported on and reversed as a unit.</summary>
public class PunchImportBatch
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string FileName { get; set; }
    public int RowsRead { get; set; }
    public int RowsAccepted { get; set; }
    public int RowsDuplicate { get; set; }
    public int RowsRejected { get; set; }
    public string? RejectionsJson { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public long ImportedBy { get; set; }
}
