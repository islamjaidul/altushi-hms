using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>One row of a punch file, whatever the device or spreadsheet it came from.</summary>
public sealed record PunchRow(string EmployeeCode, DateTimeOffset PunchedAt, string? Direction);

/// <summary>
/// Where punches come from. §13 I8 wants a live ZKTeco-class feed but §9A.3 excludes it — the devices
/// are not bought. So the adapter exists, file import and manual entry implement it now, and a device
/// poller becomes an implementation later without the rest of the module noticing.
/// </summary>
public interface IPunchSource
{
    string Name { get; }

    IAsyncEnumerable<PunchRow> ReadAsync(Stream content, CancellationToken ct = default);
}

public sealed record ImportResult(long BatchId, int Read, int Accepted, int Duplicate, int Rejected);

/// <summary>
/// Attendance: raw punches in, one derived day per employee out.
/// <para>
/// The two rules worth stating. First, a day belongs to its <em>shift</em>, not to the calendar date
/// of the punch — a night shift starting 22:00 Tuesday is a Tuesday row even though half its punches
/// happened on Wednesday. Second, deriving a day is idempotent: UNIQUE(employee, on_date) means a
/// re-derive updates rather than duplicates, so importing the same file twice changes nothing.
/// </para>
/// </summary>
public sealed class AttendanceService(AuditWriter audit, TimeProvider clock)
{
    /// <summary>Punch files are operator uploads; cap them so a stray gigabyte cannot exhaust a 3 GB box.</summary>
    public const int MaxImportBytes = 8 * 1024 * 1024;

    public async Task<ImportResult> ImportAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, string fileName,
        IPunchSource source, Stream content, long actorId, string actorName,
        CancellationToken ct = default)
    {
        if (content.CanSeek && content.Length > MaxImportBytes)
            throw new HrException($"That file is larger than {MaxImportBytes / (1024 * 1024)} MB — split it and import again.");

        var batch = new PunchImportBatch
        {
            BranchId = branchId,
            FileName = fileName,
            ImportedAt = clock.GetUtcNow(),
            ImportedBy = actorId,
        };
        hr.PunchImportBatches.Add(batch);
        await hr.SaveChangesAsync(ct);

        var byCode = await hr.Employees.AsNoTracking()
            .Where(e => e.BranchId == branchId)
            .ToDictionaryAsync(e => e.EmployeeCode, e => e.Id, ct);

        int read = 0, accepted = 0, duplicate = 0, rejected = 0;
        var rejections = new List<object>();
        var seen = new HashSet<(long, DateTimeOffset)>();

        await foreach (var row in source.ReadAsync(content, ct))
        {
            read++;
            if (!byCode.TryGetValue(row.EmployeeCode, out var employeeId))
            {
                rejected++;
                if (rejections.Count < 200)
                    rejections.Add(new { row.EmployeeCode, row.PunchedAt, Reason = "No employee with that code" });
                continue;
            }

            // Within-file duplicates, then across-file: the unique index is the real guard, this
            // just lets us report honestly instead of surfacing a constraint violation.
            if (!seen.Add((employeeId, row.PunchedAt))) { duplicate++; continue; }

            var exists = await hr.Punches.AnyAsync(
                p => p.EmployeeId == employeeId && p.DeviceId == source.Name && p.PunchedAt == row.PunchedAt, ct);
            if (exists) { duplicate++; continue; }

            hr.Punches.Add(new Punch
            {
                BranchId = branchId,
                EmployeeId = employeeId,
                DeviceId = source.Name,
                PunchedAt = row.PunchedAt,
                Source = PunchSource.Import,
                Direction = row.Direction,
                ImportBatchId = batch.Id,
                RecordedAt = clock.GetUtcNow(),
            });
            accepted++;
        }

        batch.RowsRead = read;
        batch.RowsAccepted = accepted;
        batch.RowsDuplicate = duplicate;
        batch.RowsRejected = rejected;
        batch.RejectionsJson = System.Text.Json.JsonSerializer.Serialize(rejections);

        audit.Append(kernel, branchId, actorId, actorName, "hr.attendance.import", "hr.punch_import_batch",
            batch.Id, after: new { fileName, read, accepted, duplicate, rejected }, tier: 2);

        return new ImportResult(batch.Id, read, accepted, duplicate, rejected);
    }

    /// <summary>
    /// Derives every day an import batch touched. The set is each accepted punch's Dhaka date,
    /// plus the previous date wherever the roster says that date's shift crosses midnight — a
    /// 06:05 out-punch on Wednesday belongs to Tuesday's night shift, and Tuesday must be the row
    /// that gets it (the <see cref="AttendanceDay.OnDate"/> contract).
    /// </summary>
    public async Task<int> DeriveImportedDaysAsync(
        HrDbContext hr, long branchId, long batchId, CancellationToken ct = default)
    {
        var punches = await hr.Punches.AsNoTracking()
            .Where(p => p.ImportBatchId == batchId)
            .Select(p => new { p.EmployeeId, p.PunchedAt })
            .ToListAsync(ct);

        var targets = new SortedSet<(long EmployeeId, DateOnly Date)>(
            punches.Select(p => (p.EmployeeId, DhakaDate(p.PunchedAt))));

        var previous = targets
            .Select(t => (t.EmployeeId, Date: t.Date.AddDays(-1)))
            .Where(t => !targets.Contains(t))
            .ToList();
        if (previous.Count > 0)
        {
            var employeeIds = previous.Select(x => x.EmployeeId).Distinct().ToList();
            var dates = previous.Select(x => x.Date).Distinct().ToList();
            var nightRostered = await hr.RosterEntries.AsNoTracking()
                .Where(r => employeeIds.Contains(r.EmployeeId) && dates.Contains(r.OnDate) && !r.WeeklyOff)
                .Join(hr.Shifts.AsNoTracking().Where(s => s.EndsNextDay),
                      r => r.ShiftId, s => s.Id, (r, s) => new { r.EmployeeId, r.OnDate })
                .ToListAsync(ct);
            foreach (var n in nightRostered)
                if (previous.Contains((n.EmployeeId, n.OnDate)))
                    targets.Add((n.EmployeeId, n.OnDate));
        }

        foreach (var (employeeId, date) in targets)
            await DeriveDayAsync(hr, branchId, employeeId, date, ct);

        return targets.Count;
    }

    /// <summary>
    /// Derives (or re-derives) one employee's day from punches, the roster and the calendar.
    /// Idempotent by <c>UNIQUE(employee_id, on_date)</c>.
    /// </summary>
    public async Task<AttendanceDay> DeriveDayAsync(
        HrDbContext hr, long branchId, long employeeId, DateOnly onDate, CancellationToken ct = default)
    {
        var roster = await hr.RosterEntries.AsNoTracking()
            .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.OnDate == onDate, ct);

        Shift? shift = null;
        if (roster is not null && !roster.WeeklyOff)
            shift = await hr.Shifts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == roster.ShiftId, ct);

        var day = await hr.AttendanceDays
            .FirstOrDefaultAsync(d => d.EmployeeId == employeeId && d.OnDate == onDate, ct);
        var isNew = day is null;
        day ??= new AttendanceDay
        {
            BranchId = branchId,
            EmployeeId = employeeId,
            OnDate = onDate,
            Status = AttendanceStatus.Absent,
        };

        // A correction is a human's judgement about this day. Re-deriving must not silently undo it.
        if (day.Corrected) return day;

        var approvedLeave = await hr.LeaveApplications.AsNoTracking()
            .FirstOrDefaultAsync(l => l.EmployeeId == employeeId
                                      && l.FromDate <= onDate && l.ToDate >= onDate
                                      && (l.State == LeaveApplicationState.Approved
                                          || l.State == LeaveApplicationState.Availed), ct);

        var (from, to) = PairWindow(onDate, shift);
        var punches = await hr.Punches.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.PunchedAt >= from && p.PunchedAt <= to)
            .OrderBy(p => p.PunchedAt)
            .ToListAsync(ct);

        day.ShiftId = shift?.Id;
        day.FirstIn = punches.Count > 0 ? punches[0].PunchedAt : null;
        day.LastOut = punches.Count > 1 ? punches[^1].PunchedAt : null;
        day.LateMinutes = 0;
        day.EarlyOutMinutes = 0;
        day.OvertimeMinutes = 0;
        day.WorkedMinutes = 0;
        day.LeaveApplicationId = approvedLeave?.Id;
        day.DerivedAt = clock.GetUtcNow();

        if (punches.Count == 0)
        {
            day.Status = approvedLeave is not null ? AttendanceStatus.OnLeave
                : roster is { WeeklyOff: true } ? AttendanceStatus.WeeklyOff
                : AttendanceStatus.Absent;
            day.PayableFractionBp = day.Status == AttendanceStatus.Absent ? 0 : 10000;
        }
        else if (punches.Count == 1)
        {
            // One punch is not a working day; it is a question for a human (US16.1's exception list).
            day.Status = AttendanceStatus.Incomplete;
            day.PayableFractionBp = 0;
        }
        else
        {
            day.Status = AttendanceStatus.Present;
            day.PayableFractionBp = 10000;
            var worked = (int)(punches[^1].PunchedAt - punches[0].PunchedAt).TotalMinutes;

            if (shift is not null)
            {
                worked -= shift.BreakMinutes;
                var start = ShiftStart(onDate, shift);
                var end = ShiftEnd(onDate, shift);
                day.LateMinutes = Math.Max(0, (int)(punches[0].PunchedAt - start).TotalMinutes);
                day.EarlyOutMinutes = Math.Max(0, (int)(end - punches[^1].PunchedAt).TotalMinutes);
                day.OvertimeMinutes = Math.Max(0, (int)(punches[^1].PunchedAt - end).TotalMinutes);
            }

            day.WorkedMinutes = Math.Max(0, worked);
        }

        if (isNew) hr.AttendanceDays.Add(day);
        return day;
    }

    /// <summary>
    /// Amends a derived day, with a mandatory reason (§5 M16). When the day belongs to a period whose
    /// payroll run is already locked, the correction is flagged for arrears rather than rewriting the
    /// run — hard rule 4 says a locked run is never edited.
    /// </summary>
    public async Task<AttendanceCorrection> CorrectAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long attendanceDayId,
        string toStatus, int? toWorkedMinutes, string reason,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new HrException("A correction needs a reason — it is what makes the change reviewable.");

        var day = await hr.AttendanceDays.FirstOrDefaultAsync(d => d.Id == attendanceDayId, ct)
                  ?? throw new HrException("That attendance day no longer exists.");

        var lockedRun = await hr.PayrollRuns.AsNoTracking()
            .Where(r => r.BranchId == branchId
                        && r.Period.Year == day.OnDate.Year && r.Period.Month == day.OnDate.Month
                        && (r.State == PayrollRunState.Locked || r.State == PayrollRunState.Posted))
            .FirstOrDefaultAsync(ct);

        var correction = new AttendanceCorrection
        {
            BranchId = branchId,
            AttendanceDayId = day.Id,
            FromStatus = day.Status,
            ToStatus = toStatus,
            FromWorkedMinutes = day.WorkedMinutes,
            ToWorkedMinutes = toWorkedMinutes,
            Reason = reason,
            ArrearsForRunId = lockedRun?.Id,
            CorrectedAt = clock.GetUtcNow(),
            CorrectedBy = actorId,
            CorrectedByName = actorName,
        };
        hr.AttendanceCorrections.Add(correction);

        var before = new { day.Status, day.WorkedMinutes, day.PayableFractionBp };
        day.Status = toStatus;
        if (toWorkedMinutes is { } m) day.WorkedMinutes = m;
        day.PayableFractionBp = toStatus is AttendanceStatus.Absent or AttendanceStatus.Incomplete ? 0 : 10000;
        day.Corrected = true;

        audit.Append(kernel, branchId, actorId, actorName, "hr.attendance.correct", "hr.attendance_day",
            day.Id, before: before,
            after: new { day.Status, day.WorkedMinutes, reason, ArrearsFor = lockedRun?.RunNo }, tier: 1);

        return correction;
    }

    /// <summary>Days a payroll run must not silently price: someone has to look at these first.</summary>
    public static Task<List<AttendanceDay>> ExceptionsAsync(
        HrDbContext hr, long branchId, DateOnly from, DateOnly to, CancellationToken ct = default)
        => hr.AttendanceDays.AsNoTracking()
            .Where(d => d.BranchId == branchId && d.OnDate >= from && d.OnDate <= to
                        && (d.Status == AttendanceStatus.Incomplete || d.Status == AttendanceStatus.Absent))
            .OrderBy(d => d.OnDate)
            .ToListAsync(ct);

    // A punch belongs to the shift whose window it falls in, not to whatever calendar day it landed
    // on. Without a roster we fall back to the Dhaka calendar day.
    private static (DateTimeOffset From, DateTimeOffset To) PairWindow(DateOnly onDate, Shift? shift)
    {
        if (shift is null)
        {
            var midnight = Ui(onDate);
            return (midnight, midnight.AddDays(1).AddTicks(-1));
        }

        var start = ShiftStart(onDate, shift);
        var end = ShiftEnd(onDate, shift);
        return (start.AddMinutes(-shift.PairToleranceMinutes), end.AddMinutes(shift.PairToleranceMinutes));
    }

    private static DateTimeOffset ShiftStart(DateOnly onDate, Shift shift)
        => Ui(onDate).Add(shift.StartsAt.ToTimeSpan());

    private static DateTimeOffset ShiftEnd(DateOnly onDate, Shift shift)
        => Ui(onDate).AddDays(shift.EndsNextDay ? 1 : 0).Add(shift.EndsAt.ToTimeSpan());

    /// <summary>Dhaka midnight of a date, as UTC. Asia/Dhaka has no DST, so the offset is constant.</summary>
    private static DateTimeOffset Ui(DateOnly d)
        => new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(6)).ToUniversalTime();

    /// <summary>The Dhaka calendar date a punch landed on.</summary>
    private static DateOnly DhakaDate(DateTimeOffset at)
        => DateOnly.FromDateTime(at.ToOffset(TimeSpan.FromHours(6)).DateTime);
}
