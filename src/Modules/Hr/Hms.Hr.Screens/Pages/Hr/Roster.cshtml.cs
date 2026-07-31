using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

public sealed record RosterCell(long EmployeeId, DateOnly OnDate, string? ShiftCode, bool WeeklyOff);
public sealed record RosterPerson(long Id, string Code, string Name);

/// <summary>
/// The roster board (§5 M16 [M] — a 24/7 employer runs rotating shifts). One week at a time, because
/// §7's operators work on 1366×768 and a month-wide grid is unreadable there.
/// </summary>
[Authorize(Policy = HrPerm.RosterManage)]
public class RosterModel(IHrTx tx) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? WeekOf { get; set; }
    [BindProperty(SupportsGet = true)] public long? OrgUnitId { get; set; }

    public DateOnly WeekStart { get; private set; }
    public IReadOnlyList<DateOnly> Days { get; private set; } = [];
    public IReadOnlyList<RosterPerson> People { get; private set; } = [];
    public IReadOnlyList<RosterCell> Cells { get; private set; } = [];
    public IReadOnlyList<Shift> Shifts { get; private set; } = [];
    public IReadOnlyList<OrgUnit> Units { get; private set; } = [];

    public string? ShiftFor(long employeeId, DateOnly day)
        => Cells.FirstOrDefault(c => c.EmployeeId == employeeId && c.OnDate == day) is { } cell
            ? cell.WeeklyOff ? "Off" : cell.ShiftCode
            : null;

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAssignAsync(long employeeId, string onDate, long shiftId)
    {
        if (!FlexibleDate.TryParse(onDate, out var day))
        {
            await LoadAsync();
            Fail("That date could not be read.");
            return Page();
        }

        await tx.RunAsync(async s =>
        {
            // One roster row per employee per day — UNIQUE(employee_id, on_date) makes assigning
            // twice an update rather than a duplicate.
            var entry = await s.Hr.RosterEntries
                .FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.OnDate == day);

            var roster = await s.Hr.Rosters
                .FirstOrDefaultAsync(r => r.BranchId == BranchId && r.FromDate <= day && r.ToDate >= day);
            if (roster is null)
            {
                roster = new Roster
                {
                    BranchId = BranchId,
                    OrgUnitId = OrgUnitId ?? 0,
                    FromDate = WeekStart,
                    ToDate = WeekStart.AddDays(6),
                    Published = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = ActorId,
                };
                s.Hr.Rosters.Add(roster);
                await s.Hr.SaveChangesAsync();
            }

            if (entry is null)
            {
                s.Hr.RosterEntries.Add(new RosterEntry
                {
                    RosterId = roster.Id,
                    EmployeeId = employeeId,
                    OnDate = day,
                    ShiftId = shiftId == 0 ? 0 : shiftId,
                    WeeklyOff = shiftId == 0,
                });
            }
            else
            {
                entry.ShiftId = shiftId;
                entry.WeeklyOff = shiftId == 0;
            }
        });

        return Redirect($"/hr/roster?WeekOf={WeekStart:dd MMM yyyy}&OrgUnitId={OrgUnitId}");
    }

    private async Task LoadAsync()
    {
        var anchor = FlexibleDate.TryParse(WeekOf ?? "", out var d) && d != default
            ? d
            : DateOnly.FromDateTime(DateTime.UtcNow.AddHours(6));

        // Bangladeshi weeks are commonly read Saturday-first; the roster grid follows the data's
        // weekly-off pattern rather than assuming, so we simply anchor on the requested day.
        WeekStart = anchor.AddDays(-(int)anchor.DayOfWeek);
        Days = Enumerable.Range(0, 7).Select(i => WeekStart.AddDays(i)).ToList();
        var weekEnd = WeekStart.AddDays(6);

        await tx.RunAsync(async s =>
        {
            Units = await s.Hr.OrgUnits.AsNoTracking()
                .Where(u => u.BranchId == BranchId && u.Active).OrderBy(u => u.Name).ToListAsync();
            Shifts = await s.Hr.Shifts.AsNoTracking()
                .Where(x => x.BranchId == BranchId && x.Active).OrderBy(x => x.StartsAt).ToListAsync();

            var assignmentQuery = s.Hr.Assignments.AsNoTracking()
                .Where(a => a.BranchId == BranchId && a.EffectiveTo == null);
            if (OrgUnitId is { } unit and > 0)
                assignmentQuery = assignmentQuery.Where(a => a.OrgUnitId == unit);

            var employeeIds = await assignmentQuery.Select(a => a.EmployeeId).ToListAsync();

            People = await s.Hr.Employees.AsNoTracking()
                .Where(e => employeeIds.Contains(e.Id) && e.SeparatedOn == null)
                .OrderBy(e => e.EmployeeCode)
                .Select(e => new RosterPerson(e.Id, e.EmployeeCode, e.FullName))
                .Take(60)
                .ToListAsync();

            var ids = People.Select(p => p.Id).ToList();
            var entries = await s.Hr.RosterEntries.AsNoTracking()
                .Where(r => ids.Contains(r.EmployeeId) && r.OnDate >= WeekStart && r.OnDate <= weekEnd)
                .ToListAsync();
            var shiftCodes = Shifts.ToDictionary(x => x.Id, x => x.Code);

            Cells = entries.Select(e => new RosterCell(
                e.EmployeeId, e.OnDate,
                e.WeeklyOff ? null : shiftCodes.GetValueOrDefault(e.ShiftId),
                e.WeeklyOff)).ToList();
        });
    }
}
