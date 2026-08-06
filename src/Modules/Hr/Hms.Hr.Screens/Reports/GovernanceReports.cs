using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Reports;

internal static class Governance
{
    /// <summary>
    /// Every M16 write, as the kernel recorded it. Audit rows live in <c>kernel.audit_event</c>, not
    /// per module, so HR's slice is the entity/action prefix.
    /// </summary>
    internal static IQueryable<Hms.Kernel.Data.AuditEvent> HrEvents(ReportContext ctx)
    {
        var (start, end) = ctx.Period.Range.Instants();
        return ctx.Scope.Kernel.AuditEvents.AsNoTracking()
            .Where(e => e.BranchId == ctx.BranchId && e.At >= start && e.At < end
                        && (e.Entity.StartsWith("hr.") || e.Action.StartsWith("hr.")));
    }

    /// <summary>
    /// "hr.employee.separate" → "Employee separated". The log is read by an administrator settling a
    /// dispute, not by a developer reading verbs.
    /// </summary>
    internal static string ActionWords(string action)
    {
        var parts = action.Split('.');
        if (parts.Length < 2) return action;

        var subject = parts.Length >= 3 ? parts[1] : parts[0];
        var verb = parts[^1];

        var subjectWord = subject switch
        {
            "employee" => "Employee",
            "attendance" => "Attendance",
            "leave" => "Leave",
            "payroll" => "Payroll",
            "policy" => "Policy",
            "roster" => "Roster",
            "master" => "Master data",
            "report" => "Report",
            _ => Capitalise(subject),
        };

        var verbWord = verb switch
        {
            "hire" => "hired",
            "separate" => "separated",
            "set" => "set",
            "correct" => "corrected",
            "import" => "imported",
            "generate" => "generated",
            "review" => "reviewed",
            "approve" => "approved",
            "reject" => "rejected",
            "lock" => "locked",
            "post" => "posted",
            "reverse" => "reversed",
            "apply" => "applied for",
            "recommend" => "recommended",
            "avail" => "availed",
            "cancel" => "cancelled",
            "create" => "created",
            "rename" => "renamed",
            "retire" => "retired",
            "publish" => "published",
            "read" => "read",
            _ => verb.Replace('_', ' '),
        };

        return $"{subjectWord} {verbWord}";
    }

    private static string Capitalise(string s)
        => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..].Replace('_', ' ');

    /// <summary>
    /// A one-line rendering of what changed. Most rows carry only <c>After</c> — the writer records a
    /// before/after pair on assignment, separation and attendance correction, and an after-only
    /// snapshot elsewhere. Saying "recorded" rather than inventing a diff is the honest option.
    /// </summary>
    internal static string Change(string? before, string? after)
    {
        if (before is { Length: > 0 } && after is { Length: > 0 }) return $"{Clip(before)} → {Clip(after)}";
        if (after is { Length: > 0 }) return Clip(after);
        if (before is { Length: > 0 }) return $"was {Clip(before)}";
        return "—";
    }

    private static string Clip(string json)
    {
        var s = json.Replace("\"", "").Replace("{", "").Replace("}", "").Trim();
        return s.Length <= 160 ? s : s[..160] + "…";
    }
}

/// <summary>
/// §13 Governance / §7.14 — "Every HR write, any period, by anyone" (G3, US16.15).
/// <para>
/// Read-only by construction. Nothing in <c>kernel.audit_event</c> is editable by anyone: the table
/// is append-only and enforced by grants, not merely by convention (hard rule 4).
/// </para>
/// </summary>
public sealed class ActivityLogReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "activity-log",
        Name = "Activity log",
        Question = "Every change made in HR in this period, and who made it",
        Category = ReportCategory.Governance,
        SalaryBearing = false,
        PermissionOverride = "hr.audit.view",
        DefaultGrain = PeriodGrain.Day,
        Landscape = true,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        var query = Governance.HrEvents(ctx);

        if (ctx.Filters.EmployeeId is { } employeeId)
            query = query.Where(e => e.Entity == "hr.employee" && e.EntityId == employeeId);

        if (ctx.Raw.One("actor") is { } actorRaw && long.TryParse(actorRaw, out var actorId))
            query = query.Where(e => e.ActorId == actorId);

        if (ctx.Raw.One("action") is { Length: > 0 } action)
            query = query.Where(e => e.Action.StartsWith(action));

        var events = await query
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .Take(ctx.MaxRows + 1)
            .ToListAsync(ct);

        if (events.Count == 0)
        {
            return ReportTable.Empty(
                $"Nothing was changed in HR between {PeriodWords.Range(ctx.Period.Range)}. "
                + "Every write is recorded here as it happens — an empty period means an idle one, "
                + "not a missing log.");
        }

        var matched = events.Count;
        if (matched > ctx.MaxRows) events = [.. events.Take(ctx.MaxRows)];

        var rows = events.Select(e => new ReportRow(
        [
            new ReportCell(Ui.DateTimeShort(e.At)),
            new ReportCell(e.ActorNameSnapshot,
                Href: ctx.Drill("activity-log", ("actor", e.ActorId.ToString()))),
            new ReportCell(Governance.ActionWords(e.Action)),
            new ReportCell(e.Entity),
            new ReportCell(e.EntityId == 0 ? "—" : $"#{e.EntityId}",
                Href: e.Entity == "hr.employee" && e.EntityId != 0 ? ctx.Employee(e.EntityId) : null),
            new ReportCell(Governance.Change(e.Before, e.After)),
        ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("When"), new("Who"), new("What"), new("Record"),
                new("Id", Sortable: false), new("Detail", Sortable: false),
            ],
            Rows = rows,
            MatchedRows = matched,
        };
    }
}

/// <summary>§13 Governance — "One person's change history" (§7.14 per-employee audit).</summary>
public sealed class EmployeeAuditReport : IHrReport
{
    public ReportDescriptor Descriptor { get; } = new()
    {
        Key = "employee-audit",
        Name = "Per-employee audit",
        Question = "One person's change history, from their record",
        Category = ReportCategory.Governance,
        SalaryBearing = false,
        PermissionOverride = "hr.audit.view",
        DefaultGrain = PeriodGrain.Year,
    };

    public async Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct)
    {
        if (ctx.Filters.EmployeeId is not { } employeeId)
        {
            return ReportTable.Empty(
                "This report is about one person. Open it from an employee's record, or add an "
                + "employee filter, and it will show every change made to them in the period.");
        }

        var events = await Governance.HrEvents(ctx)
            .Where(e => e.Entity == "hr.employee" && e.EntityId == employeeId)
            .OrderByDescending(e => e.At)
            .Take(ctx.MaxRows)
            .ToListAsync(ct);

        var name = await ctx.Scope.Hr.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct);

        if (events.Count == 0)
        {
            return ReportTable.Empty(
                $"No change was recorded against {name ?? "this employee"} between "
                + $"{PeriodWords.Range(ctx.Period.Range)}. Widen the period to see their whole history.");
        }

        var rows = events.Select(e => new ReportRow(
        [
            new ReportCell(Ui.DateTimeShort(e.At)),
            new ReportCell(e.ActorNameSnapshot),
            new ReportCell(Governance.ActionWords(e.Action)),
            new ReportCell(Governance.Change(e.Before, e.After)),
        ])).ToList();

        return new ReportTable
        {
            Columns =
            [
                new("When"), new("Who"), new("What"), new("Detail", Sortable: false),
            ],
            Rows = rows,
            MatchedRows = events.Count,
        };
    }
}
