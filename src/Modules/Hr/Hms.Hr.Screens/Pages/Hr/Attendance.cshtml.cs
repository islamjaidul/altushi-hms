using System.Text;
using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record AttendanceRow(
    long Id, string Code, string Name, DateOnly OnDate, string Status,
    DateTimeOffset? FirstIn, DateTimeOffset? LastOut,
    int WorkedMinutes, int LateMinutes, int OvertimeMinutes, bool Corrected);

/// <summary>
/// Attendance review (§5 M16 [M]). The default view is the exception list, not the full register:
/// US16.1 promises the HR officer that what needs a human is pre-listed, and a screen that opens on
/// four hundred correct rows buries the six that are wrong.
/// </summary>
[Authorize(Policy = HrPerm.AttendanceReview)]
public class AttendanceModel(IHrTx tx, AttendanceService attendance) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? OnDate { get; set; }
    [BindProperty(SupportsGet = true)] public bool ShowAll { get; set; }

    public DateOnly Date { get; private set; }
    public IReadOnlyList<AttendanceRow> Rows { get; private set; } = [];
    public int ExceptionCount { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>
    /// The capture path (AUD-M16-08: `ImportAsync` and `DeriveDayAsync` had zero callers — the
    /// review screen edited data the product had no way to acquire). A device export file or
    /// pasted rows go through the same <see cref="CsvPunchSource"/>; the punches land, then every
    /// touched day is derived — pairing, night-shift spanning and break deduction included.
    /// Re-importing the same file reports duplicates and changes nothing.
    /// </summary>
    public async Task<IActionResult> OnPostImportAsync(IFormFile? punchFile, string? punchRows)
    {
        var hasFile = punchFile is { Length: > 0 };
        var hasText = !string.IsNullOrWhiteSpace(punchRows);
        if (!hasFile && !hasText)
        {
            await LoadAsync();
            Fail("Choose a punch file, or paste rows as: employee code, date time — one punch per line.");
            return Page();
        }
        if ((hasFile && punchFile!.Length > AttendanceService.MaxImportBytes)
            || (hasText && punchRows!.Length > AttendanceService.MaxImportBytes))
        {
            await LoadAsync();
            Fail($"That import is larger than {AttendanceService.MaxImportBytes / (1024 * 1024)} MB — split it and import again.");
            return Page();
        }

        try
        {
            var (result, days) = await tx.RunAsync(async s =>
            {
                await using Stream stream = hasFile
                    ? punchFile!.OpenReadStream()
                    : new MemoryStream(Encoding.UTF8.GetBytes(punchRows!));

                var import = await attendance.ImportAsync(
                    s.Hr, s.Kernel, BranchId, hasFile ? punchFile!.FileName : "pasted rows",
                    new CsvPunchSource(), stream, ActorId, ActorName);

                // The punches must be on disk before derivation reads them back per day.
                await s.Hr.SaveChangesAsync();
                var derived = await attendance.DeriveImportedDaysAsync(s.Hr, BranchId, import.BatchId);
                return (import, derived);
            });

            var summary = $"Imported {result.Read} row(s): {result.Accepted} accepted, "
                          + $"{result.Duplicate} duplicate, {result.Rejected} rejected · "
                          + $"{days} day(s) derived";
            if (result.Rejected > 0)
                summary += " — rejected rows name an unknown employee code or an unreadable line";
            Toast(summary, "fact_check");
        }
        catch (HrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }

        return Redirect($"/hr/attendance?OnDate={OnDate}&ShowAll={ShowAll}");
    }

    /// <summary>
    /// The correction path. A reason is mandatory, and if the day belongs to a locked payroll run
    /// the service records it as arrears instead of rewriting history (hard rule 4).
    /// </summary>
    public async Task<IActionResult> OnPostCorrectAsync(
        long attendanceDayId, string toStatus, string reason)
    {
        try
        {
            await tx.RunAsync(async s => await attendance.CorrectAsync(
                s.Hr, s.Kernel, BranchId, attendanceDayId, toStatus, null, reason,
                ActorId, ActorName));
            Toast("Attendance corrected — the reason is on the record", "fact_check");
        }
        catch (HrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }

        return Redirect($"/hr/attendance?OnDate={OnDate}&ShowAll={ShowAll}");
    }

    private async Task LoadAsync()
    {
        Date = FlexibleDate.TryParse(OnDate ?? "", out var d) && d != default
            ? d
            : DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6));

        await tx.RunAsync(async s =>
        {
            var query = s.Hr.AttendanceDays.AsNoTracking()
                .Where(x => x.BranchId == BranchId && x.OnDate == Date);

            ExceptionCount = await query.CountAsync(x =>
                x.Status == AttendanceStatus.Incomplete || x.Status == AttendanceStatus.Absent);

            if (!ShowAll)
                query = query.Where(x =>
                    x.Status == AttendanceStatus.Incomplete || x.Status == AttendanceStatus.Absent);

            var days = await query.OrderBy(x => x.EmployeeId).Take(500).ToListAsync();
            var ids = days.Select(x => x.EmployeeId).ToList();
            var employees = await s.Hr.Employees.AsNoTracking()
                .Where(e => ids.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => new { e.EmployeeCode, e.FullName });

            Rows = days.Select(x => new AttendanceRow(
                x.Id,
                employees.TryGetValue(x.EmployeeId, out var e) ? e.EmployeeCode : "?",
                employees.TryGetValue(x.EmployeeId, out var n) ? n.FullName : "(removed)",
                x.OnDate, x.Status, x.FirstIn, x.LastOut,
                x.WorkedMinutes, x.LateMinutes, x.OvertimeMinutes, x.Corrected)).ToList();
        });
    }
}
