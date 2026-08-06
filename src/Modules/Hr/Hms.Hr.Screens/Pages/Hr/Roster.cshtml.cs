using Hms.Hr.Data;
using Hms.Kernel.Audit;
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
public class RosterModel(IHrTx tx, AuditWriter audit) : HmsPageModel
{
    /// <summary>Rows per board. Seven selects each, so this is a page-weight budget (§16).</summary>
    private const int PageSize = 60;

    [BindProperty(SupportsGet = true)] public string? WeekOf { get; set; }
    [BindProperty(SupportsGet = true)] public long? OrgUnitId { get; set; }

    public DateOnly WeekStart { get; private set; }
    public IReadOnlyList<DateOnly> Days { get; private set; } = [];
    public IReadOnlyList<RosterPerson> People { get; private set; } = [];
    public IReadOnlyList<RosterCell> Cells { get; private set; } = [];
    public IReadOnlyList<Shift> Shifts { get; private set; } = [];
    public IReadOnlyList<OrgUnit> Units { get; private set; } = [];

    /// <summary>How many people the filter matched, against how many the board is showing.</summary>
    public int Total { get; private set; }
    public bool Truncated => Total > People.Count;

    public string? ShiftFor(long employeeId, DateOnly day)
        => Cells.FirstOrDefault(c => c.EmployeeId == employeeId && c.OnDate == day) is { } cell
            ? cell.WeeklyOff ? "Off" : cell.ShiftCode
            : null;

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAssignAsync(long employeeId, string onDate, long shiftId)
    {
        // Load first. Without it WeekStart is default(DateOnly), which Npgsql stores as -infinity:
        // every assignment wrote a roster header spanning a range no real date falls inside, so
        // the "reuse the week's roster" lookup below could never match one and a fresh orphan was
        // inserted per click. It also redirected the operator to the week of January year 1 (0037).
        await LoadAsync();

        if (!FlexibleDate.TryParse(onDate, out var day))
        {
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

            // Spec 0055: rostering wrote nothing to the audit trail, so "who put me on nights"
            // had no documentary answer. The before is the shift the cell held, where it held one.
            long? beforeShift = entry?.ShiftId;

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

            audit.Append(s.Kernel, BranchId, ActorId, ActorName,
                "hr.roster.assign", "hr.roster_entry", employeeId,
                before: beforeShift is null ? null : new { ShiftId = beforeShift, OnDate = day },
                after: new { ShiftId = shiftId, OnDate = day, WeeklyOff = shiftId == 0 }, tier: 2);
        });

        return Redirect($"/hr/roster?WeekOf={WeekStart:dd MMM yyyy}&OrgUnitId={OrgUnitId}");
    }

    private async Task LoadAsync()
    {
        var anchor = FlexibleDate.TryParse(WeekOf ?? "", out var d) && d != default
            ? d
            : Dhaka.DateOf(DateTimeOffset.UtcNow);

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

            var roster = s.Hr.Employees.AsNoTracking()
                .Where(e => employeeIds.Contains(e.Id) && e.SeparatedOn == null);

            // A board of 60 is a rendering limit, not an editorial one — a hundred rows of seven
            // selects is a 587 KB page on a §16 box. It used to truncate in silence, so the last
            // forty people simply could not be rostered and nobody was told (0037). Say the number.
            Total = await roster.CountAsync();
            People = await roster
                .OrderBy(e => e.EmployeeCode)
                .Select(e => new RosterPerson(e.Id, e.EmployeeCode, e.FullName))
                .Take(PageSize)
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
