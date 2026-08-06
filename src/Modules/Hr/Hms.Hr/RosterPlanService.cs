using Hms.Hr.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr;

/// <summary>One day of a coverage view: what a shift needs against what it has.</summary>
public sealed record CoverageCell(
    DateOnly OnDate, long ShiftId, string ShiftName, int Required, int Rostered, int OnLeave)
{
    /// <summary>People actually available: rostered, less anyone on approved leave that day.</summary>
    public int Available => Rostered - OnLeave;

    public int Short => Math.Max(0, Required - Available);

    public bool IsShort => Required > 0 && Available < Required;
}

/// <summary>
/// Building a roster in fewer than 1,200 clicks (module PRD §7.5, G27).
/// <para>
/// <c>Roster</c> and <c>RosterEntry</c> shipped with the module; the screen assigned one employee to
/// one day. §5 M16 says "24/7 rotating shifts", and a 40-nurse month built one cell at a time is why
/// the real roster in a Bangladeshi hospital is a piece of paper on a wall.
/// </para>
/// <para>
/// Every write here <b>skips a day that already has an entry</b> rather than overwriting it, and says
/// how many it skipped. A pattern applied over a hand-made exception must not silently undo the
/// exception — that is exactly the day somebody swapped for a wedding.
/// </para>
/// </summary>
public sealed class RosterPlanService(AuditWriter audit)
{
    /// <summary>What a bulk write did, in the words the screen reports back.</summary>
    public sealed record PlanResult(int Written, int Skipped, int Days, int People)
    {
        public string Words =>
            $"{Written} day{(Written == 1 ? "" : "s")} rostered across {People} "
            + $"employee{(People == 1 ? "" : "s")}"
            + (Skipped > 0 ? $"; {Skipped} left alone because a shift was already set" : "");
    }

    /// <summary>
    /// Expands a cycle over a range. Pure, so the arithmetic that decides which of forty nurses
    /// works which night is pinned without a database.
    /// <para>
    /// Each employee starts the cycle offset by their position in the list, which is what makes a
    /// rotation a rotation: give six people a "6 morning, 1 off, 6 evening, 1 off" cycle and they
    /// cover the fortnight between them instead of all working mornings together.
    /// </para>
    /// </summary>
    public static IReadOnlyList<(long EmployeeId, DateOnly OnDate, long? ShiftId)> Expand(
        IReadOnlyList<long> employeeIds,
        IReadOnlyList<(long? ShiftId, int Days)> steps,
        DateOnly from, DateOnly to)
    {
        var plan = new List<(long, DateOnly, long?)>();
        if (employeeIds.Count == 0 || steps.Count == 0 || to < from) return plan;

        // The cycle flattened to one entry per day, so the offset arithmetic is a simple index.
        var cycle = new List<long?>();
        foreach (var (shiftId, days) in steps)
            for (var d = 0; d < Math.Max(1, days); d++)
                cycle.Add(shiftId);

        var length = cycle.Count;
        var span = to.DayNumber - from.DayNumber + 1;

        for (var person = 0; person < employeeIds.Count; person++)
        {
            // Stagger by whole steps rather than single days: two nurses one day apart on a
            // six-day block is not a rotation, it is the same block twice.
            var offset = StepOffset(steps, person) % length;

            for (var day = 0; day < span; day++)
            {
                var shift = cycle[(offset + day) % length];
                plan.Add((employeeIds[person], from.AddDays(day), shift));
            }
        }

        return plan;
    }

    private static int StepOffset(IReadOnlyList<(long? ShiftId, int Days)> steps, int person)
    {
        var offset = 0;
        for (var i = 0; i < person % steps.Count; i++) offset += Math.Max(1, steps[i].Days);
        return offset;
    }

    /// <summary>Applies a stored pattern to a set of people over a range.</summary>
    public async Task<PlanResult> ApplyPatternAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long rosterId, long patternId,
        IReadOnlyList<long> employeeIds, DateOnly from, DateOnly to,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            throw new HrException("Choose at least one person to roster.");
        if (to < from)
            throw new HrException("The end of the range comes after the start.");
        if (to.DayNumber - from.DayNumber > 366)
            throw new HrException("A roster is built a period at a time — 366 days is the limit.");

        var steps = await hr.RosterPatternSteps.AsNoTracking()
            .Where(s => s.PatternId == patternId)
            .OrderBy(s => s.Ordinal)
            .Select(s => new { s.ShiftId, s.Days })
            .ToListAsync(ct);
        if (steps.Count == 0)
            throw new HrException("That pattern has no steps — there is nothing to apply.");

        var plan = Expand(employeeIds, [.. steps.Select(s => (s.ShiftId, s.Days))], from, to);
        return await WriteAsync(hr, kernel, branchId, rosterId, plan, "pattern", actorId, actorName, ct);
    }

    /// <summary>Sets one shift across selected people and a date range — the bulk assign of §7.5.</summary>
    public async Task<PlanResult> BulkAssignAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long rosterId, long? shiftId,
        IReadOnlyList<long> employeeIds, DateOnly from, DateOnly to,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (employeeIds.Count == 0)
            throw new HrException("Choose at least one person to roster.");
        if (to < from)
            throw new HrException("The end of the range comes after the start.");

        var plan = new List<(long, DateOnly, long?)>();
        foreach (var employeeId in employeeIds)
            for (var d = from; d <= to; d = d.AddDays(1))
                plan.Add((employeeId, d, shiftId));

        return await WriteAsync(hr, kernel, branchId, rosterId, plan, "bulk", actorId, actorName, ct);
    }

    /// <summary>
    /// Copies a previous period forward — how a roster is actually made (§7.5). Days are mapped by
    /// their offset from the source start, so a 31-day month copied onto a 30-day one simply runs out
    /// rather than wrapping into somebody's next month.
    /// </summary>
    public async Task<PlanResult> CopyPeriodAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long rosterId,
        DateOnly sourceFrom, DateOnly sourceTo, DateOnly targetFrom,
        long actorId, string actorName, CancellationToken ct = default)
    {
        if (sourceTo < sourceFrom)
            throw new HrException("The source period ends before it starts.");

        var source = await hr.RosterEntries.AsNoTracking()
            .Where(e => e.OnDate >= sourceFrom && e.OnDate <= sourceTo)
            .Select(e => new { e.EmployeeId, e.OnDate, e.ShiftId })
            .ToListAsync(ct);

        if (source.Count == 0)
            throw new HrException(
                $"Nothing is rostered between {sourceFrom:dd MMM yyyy} and {sourceTo:dd MMM yyyy}.");

        var plan = source
            .Select(e => (e.EmployeeId,
                targetFrom.AddDays(e.OnDate.DayNumber - sourceFrom.DayNumber),
                (long?)e.ShiftId))
            .ToList();

        return await WriteAsync(hr, kernel, branchId, rosterId, plan, "copy", actorId, actorName, ct);
    }

    private async Task<PlanResult> WriteAsync(
        HrDbContext hr, KernelDbContext kernel, long branchId, long rosterId,
        IReadOnlyList<(long EmployeeId, DateOnly OnDate, long? ShiftId)> plan,
        string how, long actorId, string actorName, CancellationToken ct)
    {
        var roster = await hr.Rosters.FirstOrDefaultAsync(r => r.Id == rosterId, ct)
                     ?? throw new HrException("That roster no longer exists.");
        if (roster.Published)
            throw new HrException(
                $"The roster for {roster.FromDate:dd MMM} – {roster.ToDate:dd MMM yyyy} is "
                + "published. Amend it day by day so the change is visible to the people on it, or "
                + "start a new roster.");

        var dates = plan.Select(p => p.OnDate).ToList();
        var from = dates.Min();
        var to = dates.Max();
        var ids = plan.Select(p => p.EmployeeId).Distinct().ToList();

        // One query for everything already rostered in the window, rather than one per cell.
        var existing = (await hr.RosterEntries.AsNoTracking()
                .Where(e => ids.Contains(e.EmployeeId) && e.OnDate >= from && e.OnDate <= to)
                .Select(e => new { e.EmployeeId, e.OnDate })
                .ToListAsync(ct))
            .Select(e => (e.EmployeeId, e.OnDate))
            .ToHashSet();

        int written = 0, skipped = 0;
        foreach (var (employeeId, onDate, shiftId) in plan)
        {
            // A rest day in a pattern writes nothing: the absence of an entry IS the day off, and
            // an entry with no shift would show as an unfilled cell rather than a rest.
            if (shiftId is null) continue;

            if (!existing.Add((employeeId, onDate))) { skipped++; continue; }

            // RosterEntry carries no BranchId of its own — it is reached through its roster, which
            // does. Spec 0055's notes list it among the child tables the global filter cannot see.
            hr.RosterEntries.Add(new RosterEntry
            {
                RosterId = rosterId,
                EmployeeId = employeeId,
                OnDate = onDate,
                ShiftId = shiftId.Value,
            });
            written++;
        }

        await hr.SaveChangesAsync(ct);

        audit.Append(kernel, branchId, actorId, actorName, $"hr.roster.{how}", "hr.roster",
            rosterId, after: new { written, skipped, from, to, people = ids.Count }, tier: 2);

        return new PlanResult(written, skipped, to.DayNumber - from.DayNumber + 1, ids.Count);
    }

    // ---- coverage (§7.5) ------------------------------------------------------------------------

    /// <summary>
    /// Required against rostered, per shift per day, less anyone on approved leave. An employer who
    /// has configured no requirement gets the rostered counts and <b>no claim of a shortfall</b> —
    /// asserting one against a number nobody set would be inventing the number.
    /// </summary>
    public static async Task<IReadOnlyList<CoverageCell>> CoverageAsync(
        HrDbContext hr, long branchId, long orgUnitId, DateOnly from, DateOnly to,
        CancellationToken ct = default)
    {
        var shifts = await hr.Shifts.AsNoTracking()
            .Where(s => s.BranchId == branchId && s.Active)
            .Select(s => new { s.Id, s.Name })
            .ToListAsync(ct);
        if (shifts.Count == 0) return [];

        var requirements = await hr.ShiftRequirements.AsNoTracking()
            .Where(r => r.OrgUnitId == orgUnitId)
            .Select(r => new { r.ShiftId, r.Weekday, r.RequiredHeadcount })
            .ToListAsync(ct);

        // Who belongs to this unit, as of the end of the window.
        var members = await hr.Assignments.AsNoTracking()
            .Where(a => a.BranchId == branchId && a.OrgUnitId == orgUnitId
                        && a.EffectiveFrom <= to && (a.EffectiveTo == null || a.EffectiveTo >= from))
            .Select(a => a.EmployeeId)
            .Distinct()
            .ToListAsync(ct);
        if (members.Count == 0) return [];

        var entries = await hr.RosterEntries.AsNoTracking()
            .Where(e => members.Contains(e.EmployeeId) && e.OnDate >= from && e.OnDate <= to)
            .Select(e => new { e.EmployeeId, e.OnDate, e.ShiftId })
            .ToListAsync(ct);

        // Approved leave overlapping the window — the clash §7.5 wants shown before publishing.
        var leave = await hr.LeaveApplications.AsNoTracking()
            .Where(l => members.Contains(l.EmployeeId)
                        && l.FromDate <= to && l.ToDate >= from
                        && (l.State == LeaveApplicationState.Approved || l.State == LeaveApplicationState.Availed))
            .Select(l => new { l.EmployeeId, l.FromDate, l.ToDate })
            .ToListAsync(ct);

        var cells = new List<CoverageCell>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            foreach (var shift in shifts)
            {
                var required = requirements
                    .Where(r => r.ShiftId == shift.Id
                                && (r.Weekday == null || r.Weekday == day.DayOfWeek))
                    .Select(r => r.RequiredHeadcount)
                    .DefaultIfEmpty(0)
                    .Max();

                var rostered = entries
                    .Where(e => e.OnDate == day && e.ShiftId == shift.Id)
                    .Select(e => e.EmployeeId)
                    .ToList();

                var onLeave = rostered.Count(id => leave.Any(l =>
                    l.EmployeeId == id && l.FromDate <= day && l.ToDate >= day));

                if (required == 0 && rostered.Count == 0) continue;

                cells.Add(new CoverageCell(day, shift.Id, shift.Name, required, rostered.Count, onLeave));
            }
        }

        return cells;
    }
}
