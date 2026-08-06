using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell.Reporting;
using Microsoft.EntityFrameworkCore;

namespace Hms.Hr.Screens.Reports;

/// <summary>
/// A report. One method, one return type, and nothing else.
/// <para>
/// A report class carries <b>no</b> permission code, no period code, no export code, no Razor and no
/// DI beyond the scope it is handed. Everything the module PRD §12.4 asks for — the header, the
/// chips, sorting, the empty state, the row count, the truncation notice, permission, export, print,
/// drill-through and the salary-access audit — lives in the one page that renders these, where a
/// report author cannot bypass it. That is what keeps sixty reports one product rather than sixty
/// screens (D3).
/// </para>
/// </summary>
public interface IHrReport
{
    ReportDescriptor Descriptor { get; }

    Task<ReportTable> BuildAsync(ReportContext ctx, CancellationToken ct);
}

/// <summary>Caps that keep a report answerable on the deployment ceiling (N-HR1, N-HR2).</summary>
public static class ReportLimits
{
    /// <summary>
    /// Rows a screen will render. Beyond this the table says "showing N of M" rather than trying —
    /// at 1,000 employees a five-year range is 1.8M attendance days, which is an out-of-memory
    /// failure, not a slow page.
    /// </summary>
    public const int MaxRows = 5_000;
}

/// <summary>
/// What a report gets: the branch, the resolved period, the filters, and a live
/// <see cref="HrScope"/> inside the page's transaction.
/// </summary>
public sealed record ReportContext
{
    public required long BranchId { get; init; }

    public required Period Period { get; init; }

    public required HrReportFilters Filters { get; init; }

    public required FilterSet Raw { get; init; }

    public required HrScope Scope { get; init; }

    public required IReadOnlyList<KeyValuePair<string, string>> PeriodPairs { get; init; }

    public string? Sort { get; init; }

    public bool SortDesc { get; init; }

    public int MaxRows { get; init; } = ReportLimits.MaxRows;

    /// <summary>Whether the reader may see pay. Reports must not read salary tables when false.</summary>
    public bool CanSeeSalary { get; init; }

    /// <summary>
    /// The only way to build a link out of a report, and therefore the reason §12.4.5 cannot be
    /// forgotten: it always carries the period and the live filters, so a drill-through lands on the
    /// same period it came from. An architecture test forbids a report composing a report URL itself.
    /// </summary>
    public string Drill(string reportKey, params (string Key, string Value)[] extra)
    {
        var pairs = PeriodPairs
            .Concat(Raw.AsQuery())
            .Concat(extra.Select(e => new KeyValuePair<string, string>(e.Key, e.Value)));

        return "/hr/reports/" + Uri.EscapeDataString(reportKey) + "?" + string.Join('&',
            pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
    }

    /// <summary>A link to one employee's record, keeping the period so the timeline opens on it.</summary>
    public string Employee(long employeeId)
    {
        var pairs = PeriodPairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}");
        return $"/hr/employee/{employeeId}?" + string.Join('&', pairs);
    }
}

/// <summary>
/// The filter vocabulary every HR report shares (§12.4.2). Shell owns the URL bag; this owns what
/// the keys mean and what to call them on a chip.
/// </summary>
public sealed record HrReportFilters
{
    public IReadOnlyList<long> OrgUnitIds { get; init; } = [];

    public long? DesignationId { get; init; }

    public long? GradeId { get; init; }

    public long? EmployeeId { get; init; }

    /// <summary>An <see cref="EmploymentStatus"/> value, or "active" / "separated".</summary>
    public string? Status { get; init; }

    public static HrReportFilters From(FilterSet raw) => new()
    {
        OrgUnitIds = raw.Ids("unit"),
        DesignationId = raw.Id("designation"),
        GradeId = raw.Id("grade"),
        EmployeeId = raw.Id("employee"),
        Status = raw.One("status"),
    };

    /// <summary>
    /// The chips, labelled with names read from the database — built inside the transaction and
    /// handed to both the screen and the export, so the two cannot describe the same run differently.
    /// </summary>
    public async Task<IReadOnlyList<FilterChip>> DescribeAsync(HrScope s, long branchId,
        FilterSet raw, string path, IReadOnlyList<KeyValuePair<string, string>> periodPairs,
        CancellationToken ct)
    {
        var chips = new List<FilterChip>();

        foreach (var unitId in OrgUnitIds)
        {
            var name = await s.Hr.OrgUnits.AsNoTracking()
                .Where(u => u.Id == unitId).Select(u => u.Name).FirstOrDefaultAsync(ct);
            chips.Add(Chip("Unit", name ?? $"#{unitId}", "unit", unitId.ToString()));
        }

        if (DesignationId is { } designationId)
        {
            var name = await s.Hr.Designations.AsNoTracking()
                .Where(d => d.Id == designationId).Select(d => d.Name).FirstOrDefaultAsync(ct);
            chips.Add(Chip("Designation", name ?? $"#{designationId}", "designation", designationId.ToString()));
        }

        if (GradeId is { } gradeId)
        {
            var name = await s.Hr.Grades.AsNoTracking()
                .Where(g => g.Id == gradeId).Select(g => g.Name).FirstOrDefaultAsync(ct);
            chips.Add(Chip("Grade", name ?? $"#{gradeId}", "grade", gradeId.ToString()));
        }

        if (EmployeeId is { } employeeId)
        {
            var name = await s.Hr.Employees.AsNoTracking()
                .Where(e => e.Id == employeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct);
            chips.Add(Chip("Employee", name ?? $"#{employeeId}", "employee", employeeId.ToString()));
        }

        if (Status is { Length: > 0 } status)
            chips.Add(Chip("Status", Words(status), "status", status));

        return chips;

        FilterChip Chip(string label, string value, string key, string raw2)
        {
            var kept = periodPairs.Concat(raw.Without(key, raw2));
            var href = path + "?" + string.Join('&',
                kept.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
            return new FilterChip($"{label}: {value}", href);
        }
    }

    private static string Words(string status) => status switch
    {
        "active" => "In service",
        "separated" => "Separated",
        EmploymentStatus.Probation => "On probation",
        EmploymentStatus.Confirmed => "Confirmed",
        EmploymentStatus.OnNotice => "On notice",
        EmploymentStatus.Resigned => "Resigned",
        EmploymentStatus.Terminated => "Terminated",
        EmploymentStatus.Retired => "Retired",
        _ => status,
    };
}

/// <summary>
/// Master names, loaded once per report rather than per row. There are no navigation properties in
/// this module by design (<c>HrDbContext</c> says so), so a report joins in memory — which is only
/// cheap if the lookup side is loaded once.
/// </summary>
public sealed class HrLookups
{
    private HrLookups()
    {
    }

    public IReadOnlyDictionary<long, string> OrgUnits { get; private init; } = new Dictionary<long, string>();

    public IReadOnlyDictionary<long, string> Designations { get; private init; } = new Dictionary<long, string>();

    public IReadOnlyDictionary<long, string> Grades { get; private init; } = new Dictionary<long, string>();

    public IReadOnlyDictionary<long, string> LeaveTypes { get; private init; } = new Dictionary<long, string>();

    public string Unit(long? id) => Name(OrgUnits, id);

    public string Designation(long? id) => Name(Designations, id);

    public string Grade(long? id) => Name(Grades, id);

    public string LeaveType(long? id) => Name(LeaveTypes, id);

    private static string Name(IReadOnlyDictionary<long, string> map, long? id)
        => id is { } key && map.TryGetValue(key, out var name) ? name : "—";

    public static async Task<HrLookups> LoadAsync(HrScope s, CancellationToken ct) => new()
    {
        // No branch predicate: HrDbContext installs a global query filter on every entity carrying a
        // BranchId (BranchScope), so these are already this branch's masters.
        OrgUnits = await s.Hr.OrgUnits.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct),
        Designations = await s.Hr.Designations.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.Name, ct),
        Grades = await s.Hr.Grades.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct),
        LeaveTypes = await s.Hr.LeaveTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct),
    };
}
