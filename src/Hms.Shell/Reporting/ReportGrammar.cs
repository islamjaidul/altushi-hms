using Hms.Kernel.Time;

namespace Hms.Shell.Reporting;

/// <summary>
/// The shape of every report in the product (module PRD §12.4, D3).
/// <para>
/// A report is a class that returns one of these. It has no Razor, no permission code, no period
/// code and no export code, because the grammar owns all four. That is what makes "an HR officer
/// who learns one report has learned all of them" a property of the build rather than of review
/// discipline — and what stops sixty reports becoming sixty inconsistent screens.
/// </para>
/// </summary>
public enum ReportAlign
{
    Left,
    Right,
    Centre,
}

public sealed record ReportColumn(
    string Header,
    ReportAlign Align = ReportAlign.Left,
    bool Sortable = true,
    bool Money = false,
    string? Key = null);

/// <summary>
/// One cell. <paramref name="Number"/> carries the underlying value so exports can write a real
/// number where the screen shows a formatted string, and <paramref name="Href"/> is the
/// drill-through — buildable only through <c>ReportContext.Drill</c>, which always folds in the
/// period and the live filters (§12.4.5, D4).
/// </summary>
public sealed record ReportCell(
    string Text,
    long? Number = null,
    string? Href = null,
    string? PillCss = null);

public sealed record ReportRow(
    IReadOnlyList<ReportCell> Cells,
    string? GroupKey = null,
    bool IsSubtotal = false);

/// <summary>
/// A rendered report. The constructor enforces §12.4.4 — an empty table must say why it is empty
/// and what to change, never a blank grid — because that is the requirement most likely to be
/// skipped by the sixtieth report author, and a runtime failure in a smoke test is cheaper than a
/// blank screen in front of a buyer.
/// </summary>
public sealed record ReportTable
{
    public required IReadOnlyList<ReportColumn> Columns { get; init; }

    public required IReadOnlyList<ReportRow> Rows { get; init; }

    /// <summary>Grand totals, rendered in the table foot and repeated on print.</summary>
    public IReadOnlyList<ReportCell>? Totals { get; init; }

    /// <summary>Why there are no rows and what to change. Required whenever Rows is empty.</summary>
    public string? EmptyReason { get; init; }

    /// <summary>How many rows matched before the display cap — so the screen can say so.</summary>
    public int MatchedRows { get; init; }

    public bool Truncated => MatchedRows > Rows.Count;

    public ReportTable()
    {
    }

    public static ReportTable Empty(string reason) => new()
    {
        Columns = [],
        Rows = [],
        EmptyReason = string.IsNullOrWhiteSpace(reason)
            ? "There is nothing to show for this selection."
            : reason,
    };

    /// <summary>
    /// The invariant, checked once the record's initialisers have run. Called by the report page
    /// before rendering; a report that returns a silent blank fails the catalog smoke test.
    /// </summary>
    public ReportTable Validated()
    {
        if (Rows.Count == 0 && string.IsNullOrWhiteSpace(EmptyReason))
        {
            throw new InvalidOperationException(
                "A report with no rows must say why it is empty and what to change (§12.4.4). "
                + "Return ReportTable.Empty(reason) rather than an empty row list.");
        }
        return this;
    }
}

/// <summary>A filter as the operator sees it: a removable chip, never a hidden panel (§12.4.2).</summary>
public sealed record FilterChip(string Label, string RemoveHref);

/// <summary>
/// What a report is, independently of what it returns. Kept separate from the report class so the
/// report centre can list every report — including ones the reader may not open — without running
/// any of them.
/// </summary>
public sealed record ReportDescriptor
{
    /// <summary>Kebab-case, in the URL, and permanent: renaming one breaks every link ever sent.</summary>
    public required string Key { get; init; }

    public required string Name { get; init; }

    /// <summary>The question this answers, in the operator's words — §13's "Answers" column.</summary>
    public required string Question { get; init; }

    public required string Category { get; init; }

    /// <summary>
    /// Whether this report shows pay. No default: every author must decide, because D6 makes salary
    /// confidentiality structural and a forgotten <c>false</c> is a leak.
    /// </summary>
    public required bool SalaryBearing { get; init; }

    public required PeriodGrain DefaultGrain { get; init; }

    /// <summary>A partial name, for the few reports that are a matrix or a chart rather than a table.</summary>
    public string? CustomView { get; init; }

    /// <summary>Wide registers print A4 landscape (§12.4.7).</summary>
    public bool Landscape { get; init; }

    /// <summary>
    /// Set only where a report needs a permission of its own — the governance registers, which are
    /// the auditor's surface rather than the HR officer's. Everything else derives from
    /// <see cref="SalaryBearing"/> so it cannot drift.
    /// </summary>
    public string? PermissionOverride { get; init; }

    /// <summary>The permission this report needs.</summary>
    public string RequiredPermission => PermissionOverride
        ?? (SalaryBearing ? "hr.reports.salary" : "hr.reports.view");
}

/// <summary>The §13 categories, in the order the report centre lists them.</summary>
public static class ReportCategory
{
    public const string People = "People";
    public const string Time = "Time & attendance";
    public const string Leave = "Leave";
    public const string Payroll = "Payroll & compensation";
    public const string Statutory = "Statutory & ledgers";
    public const string Management = "Management & analytics";
    public const string Governance = "Governance";

    public static readonly IReadOnlyList<string> All =
        [People, Time, Leave, Payroll, Statutory, Management, Governance];
}

/// <summary>Everything the export header needs, so a file states what it is (§12.4.1, §12.4.6).</summary>
public sealed record ExportHeader(
    string ReportName,
    string PeriodWords,
    IReadOnlyList<FilterChip> Filters,
    string RunBy,
    string RunAt);
