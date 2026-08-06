using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Pages.Hr;

/// <summary>One thing that happened to a person, in the one stream that is their service record.</summary>
public sealed record TimelineEntry(
    DateOnly On,
    string Category,
    string Title,
    string Detail,
    string? Reason,
    string RecordedBy,
    string? Href);

/// <summary>
/// The employee timeline (module PRD §15, US16.6, D5) — "the profile screen is a summary; the
/// timeline is the truth".
/// <para>
/// Nothing new is captured for this. <c>hr.employment_event</c> is already append-only and
/// attributed, and assignment, pay-structure, leave, attendance-correction and payroll history all
/// carry dates. The record existed; it was rendered as four tables nobody could read as one life.
/// </para>
/// <para>
/// <b>Salary-bearing entries are hidden entirely without salary read, and their absence is stated</b>
/// (D6, §15.3). A record silently missing its compensation history reads as complete when it is not,
/// which is the more dangerous of the two failures.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.Read)]
public class TimelineModel(IHrTx tx, IPeriodCalendarSource calendars, TimeProvider clock) : HmsPageModel
{
    public const string Employment = "Employment";
    public const string Compensation = "Compensation";
    public const string TimeCategory = "Time";
    public const string LeaveCategory = "Leave";
    public const string MoneyOwed = "Money owed";

    public static readonly IReadOnlyList<string> Categories =
        [Employment, Compensation, TimeCategory, LeaveCategory, MoneyOwed];

    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    public Employee? Person { get; private set; }

    public Period Period { get; private set; } = null!;

    public PeriodBar Bar { get; private set; } = null!;

    public IReadOnlyList<TimelineEntry> Entries { get; private set; } = [];

    /// <summary>How many compensation entries this reader is not being shown. Stated, never silent.</summary>
    public int HiddenCount { get; private set; }

    public IReadOnlySet<string> Selected { get; private set; } = new HashSet<string>();

    public string CurrentAssignment { get; private set; } = "—";

    public bool CanSeePay => Can(HrPerm.Claim.SalaryRead);

    public string CategoryHref(string category)
    {
        var wanted = new HashSet<string>(Selected, StringComparer.OrdinalIgnoreCase);
        if (!wanted.Remove(category)) wanted.Add(category);

        var pairs = PeriodBinding.PeriodPairs(Period)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}")
            .Concat(wanted.Select(c => "cat=" + Uri.EscapeDataString(c)));
        return $"/hr/employees/{Id}/timeline?" + string.Join('&', pairs);
    }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var today = Dhaka.Today(clock);
        var calendar = await calendars.ForAsync(BranchId, today, ct);

        // A service record is read a year at a time, so the period defaults to a year rather than
        // the month a register would want (§15.3's year scrubber, expressed through the one control).
        Period = PeriodBinding.Resolve(Request, today, calendar, PeriodGrain.Year);
        Bar = PeriodBar.For(Period, Request, calendar, today);
        PeriodBinding.Remember(Response, Period);

        Selected = new HashSet<string>(Request.Query["cat"].Where(c => c is not null)!,
            StringComparer.OrdinalIgnoreCase);

        await tx.RunAsync(async s => await LoadAsync(s, ct), ct);

        if (Person is null) return NotFound();
        return Page();
    }

    private async Task LoadAsync(HrScope s, CancellationToken ct)
    {
        // An id is not an authorisation: the branch predicate is what stops one being used as one.
        Person = await s.Hr.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == Id && e.BranchId == BranchId, ct);
        if (Person is null) return;

        var (from, to) = (Period.Range.From, Period.Range.To);
        var lookups = await HrLookupsFor(s, ct);
        var entries = new List<TimelineEntry>();

        // ---- employment events: the append-only spine of the record
        var events = await s.Hr.EmploymentEvents.AsNoTracking()
            .Where(v => v.EmployeeId == Id && v.OnDate >= from && v.OnDate <= to)
            .ToListAsync(ct);

        entries.AddRange(events.Select(v => new TimelineEntry(
            v.OnDate, Employment, EventTitle(v.Kind), v.Note ?? "", null, v.RecordedByName, null)));

        // ---- assignments: where they sat, and why it changed
        var assignments = await s.Hr.Assignments.AsNoTracking()
            .Where(a => a.EmployeeId == Id && a.EffectiveFrom >= from && a.EffectiveFrom <= to)
            .ToListAsync(ct);

        entries.AddRange(assignments.Select(a => new TimelineEntry(
            a.EffectiveFrom, Employment, "Assignment",
            $"{lookups.Unit(a.OrgUnitId)} · {lookups.Designation(a.DesignationId)} · "
            + $"grade {lookups.Grade(a.GradeId)}",
            a.Reason,
            // The assignment row carries only a numeric CreatedBy, with no name snapshot — unlike
            // the employment event beside it. Rather than render "user 7", say plainly it is not
            // recorded here and leave the activity log as the place that knows.
            "—", null)));

        // ---- leave: every application with its outcome
        var leave = await s.Hr.LeaveApplications.AsNoTracking()
            .Where(l => l.EmployeeId == Id && l.FromDate <= to && l.ToDate >= from)
            .ToListAsync(ct);

        entries.AddRange(leave.Select(l => new TimelineEntry(
            l.FromDate, LeaveCategory,
            $"{lookups.LeaveType(l.LeaveTypeId)} — {Reports.LeaveText.StateWord(l.State)}",
            $"{Reports.LeaveText.Days(l.DaysBp)} day(s), {Ui.DateLong(l.FromDate)} to {Ui.DateLong(l.ToDate)}",
            l.DecisionNote ?? l.Reason, l.AppliedByName, null)));

        // ---- attendance corrections: what was amended and why
        var dayIds = await s.Hr.AttendanceDays.AsNoTracking()
            .Where(d => d.EmployeeId == Id && d.OnDate >= from && d.OnDate <= to && d.Corrected)
            .Select(d => new { d.Id, d.OnDate })
            .ToListAsync(ct);

        if (dayIds.Count > 0)
        {
            var ids = dayIds.Select(d => d.Id).ToList();
            var dateOf = dayIds.ToDictionary(d => d.Id, d => d.OnDate);
            var corrections = await s.Hr.AttendanceCorrections.AsNoTracking()
                .Where(c => ids.Contains(c.AttendanceDayId))
                .ToListAsync(ct);

            entries.AddRange(corrections.Select(c => new TimelineEntry(
                dateOf.GetValueOrDefault(c.AttendanceDayId, from), TimeCategory,
                "Attendance corrected",
                $"{Reports.Time.StatusWord(c.FromStatus)} → {Reports.Time.StatusWord(c.ToStatus)}",
                c.Reason, c.CorrectedByName, null)));
        }

        // ---- money owed: the ledger the employer keeps against this person
        var ledger = await s.Hr.LedgerEntries.AsNoTracking()
            .Where(l => l.EmployeeId == Id && l.OnDate >= from && l.OnDate <= to)
            .ToListAsync(ct);

        entries.AddRange(ledger.Select(l => new TimelineEntry(
            l.OnDate, MoneyOwed, l.Kind, l.Narration, null, "—", null)));

        // ---- compensation: salary-bearing, so counted before it is either shown or withheld
        var payStructures = await s.Hr.PayStructures.AsNoTracking()
            .Where(p => p.EmployeeId == Id && p.EffectiveFrom >= from && p.EffectiveFrom <= to)
            .ToListAsync(ct);

        var payslips = await s.Hr.PayrollLines.AsNoTracking()
            .Where(l => l.EmployeeId == Id)
            .Join(s.Hr.PayrollRuns.AsNoTracking().Where(r => r.Period >= from && r.Period <= to),
                l => l.RunId, r => r.Id,
                (l, r) => new { r.Period, r.RunNo, l.NetPayTaka, l.GrossEarningsTaka })
            .ToListAsync(ct);

        HiddenCount = payStructures.Count + payslips.Count;

        if (CanSeePay)
        {
            HiddenCount = 0;

            entries.AddRange(payStructures.Select(p => new TimelineEntry(
                p.EffectiveFrom, Compensation, "Pay structure set",
                p.Reason is { Length: > 0 } r ? $"reason: {r}" : "revision", p.Reason, "—", null)));

            entries.AddRange(payslips.Select(p => new TimelineEntry(
                p.Period, Compensation, $"Paid — {p.Period:MMMM yyyy}",
                $"gross {Ui.Money(p.GrossEarningsTaka)}, net {Ui.Money(p.NetPayTaka)}",
                null, "—", null)));
        }

        var filtered = Selected.Count == 0
            ? entries
            : [.. entries.Where(e => Selected.Contains(e.Category))];

        // Newest first: a query about a person is nearly always about what happened recently.
        Entries = [.. filtered.OrderByDescending(e => e.On).ThenBy(e => e.Category)];

        var current = await s.Hr.Assignments.AsNoTracking()
            .Where(a => a.EmployeeId == Id && a.EffectiveTo == null)
            .OrderByDescending(a => a.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        if (current is not null)
        {
            CurrentAssignment = $"{lookups.Unit(current.OrgUnitId)} · "
                                + $"{lookups.Designation(current.DesignationId)} · "
                                + $"grade {lookups.Grade(current.GradeId)}";
        }
    }

    private static string EventTitle(string kind) => kind switch
    {
        EmploymentEventKind.Joined => "Joined",
        EmploymentEventKind.Confirmed => "Confirmed",
        EmploymentEventKind.Promoted => "Promoted",
        EmploymentEventKind.Transferred => "Transferred",
        EmploymentEventKind.Increment => "Increment",
        EmploymentEventKind.NoticeServed => "Notice served",
        EmploymentEventKind.Resigned => "Resigned",
        EmploymentEventKind.Terminated => "Terminated",
        EmploymentEventKind.Retired => "Retired",
        EmploymentEventKind.Rehired => "Rehired",
        _ => kind.Replace('_', ' '),
    };

    private sealed record Lookups(
        IReadOnlyDictionary<long, string> Units,
        IReadOnlyDictionary<long, string> Designations,
        IReadOnlyDictionary<long, string> Grades,
        IReadOnlyDictionary<long, string> LeaveTypes)
    {
        public string Unit(long? id) => Name(Units, id);

        public string Designation(long? id) => Name(Designations, id);

        public string Grade(long? id) => Name(Grades, id);

        public string LeaveType(long? id) => Name(LeaveTypes, id);

        private static string Name(IReadOnlyDictionary<long, string> map, long? id)
            => id is { } k && map.TryGetValue(k, out var n) ? n : "—";
    }

    private static async Task<Lookups> HrLookupsFor(HrScope s, CancellationToken ct) => new(
        await s.Hr.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct),
        await s.Hr.Designations.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct),
        await s.Hr.Grades.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct),
        await s.Hr.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct));
}
