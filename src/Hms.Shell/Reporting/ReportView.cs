using Hms.Kernel.Time;
using Microsoft.AspNetCore.Http;

namespace Hms.Shell.Reporting;

/// <summary>
/// A rendered report's chrome: what <c>_ReportHeader.cshtml</c> and <c>_ReportTable.cshtml</c> need,
/// computed once by the page so the view contains no logic and the print layout, the screen and the
/// export cannot disagree about what was shown.
/// </summary>
public sealed record ReportHead
{
    public required string Name { get; init; }

    public required string Question { get; init; }

    public required string PeriodWords { get; init; }

    public required string RunBy { get; init; }

    public required string RunAt { get; init; }

    public required int RowCount { get; init; }

    public string? CsvHref { get; init; }

    public string? XlsxHref { get; init; }
}

/// <summary>The table plus its column links. Sorting is a URL, so it survives print and export.</summary>
public sealed record ReportView
{
    public required ReportTable Table { get; init; }

    public required IReadOnlyList<Head> Heads { get; init; }

    /// <summary>A column header: its label, where clicking it sorts, and which way it sorts now.</summary>
    public sealed record Head(string Label, ReportAlign Align, string? SortHref, string Indicator);

    public static ReportView For(ReportTable table, HttpRequest request)
    {
        var currentSort = request.Query["sort"].ToString();
        var currentDesc = request.Query["dir"].ToString() == "desc";

        var heads = new List<Head>(table.Columns.Count);
        foreach (var column in table.Columns)
        {
            var key = column.Key ?? Slug(column.Header);
            if (!column.Sortable)
            {
                heads.Add(new Head(column.Header, column.Align, null, ""));
                continue;
            }

            var isCurrent = string.Equals(currentSort, key, StringComparison.OrdinalIgnoreCase);
            var nextDesc = isCurrent && !currentDesc;

            var kept = request.Query
                .Where(kv => !string.Equals(kv.Key, "sort", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(kv.Key, "dir", StringComparison.OrdinalIgnoreCase)
                             && !string.Equals(kv.Key, "handler", StringComparison.OrdinalIgnoreCase))
                .SelectMany(kv => kv.Value,
                    (kv, v) => new KeyValuePair<string, string>(kv.Key, v ?? ""))
                .ToList();
            kept.Add(new("sort", key));
            if (nextDesc) kept.Add(new("dir", "desc"));

            var href = request.Path + "?" + string.Join('&', kept.Select(p =>
                $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

            // A word, not only an arrow — §7 U12: meaning never rides on a glyph alone.
            var indicator = isCurrent ? (currentDesc ? " (high to low)" : " (low to high)") : "";
            heads.Add(new Head(column.Header, column.Align, href, indicator));
        }

        return new ReportView { Table = table, Heads = heads };
    }

    internal static string Slug(string header)
        => new string([.. header.ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')]).Trim('-');

    public static string AlignClass(ReportAlign align) => align switch
    {
        ReportAlign.Right => "num",
        ReportAlign.Centre => "centre",
        _ => "",
    };
}

/// <summary>A report as the report centre lists it: what it is, and whether this reader may open it.</summary>
public sealed record ReportListing(
    ReportDescriptor Descriptor,
    bool Permitted,
    string Href,
    string? UnavailableReason);

/// <summary>The catalogue grouped for display, in §13's category order.</summary>
public sealed record ReportCentre(
    IReadOnlyList<(string Category, IReadOnlyList<ReportListing> Reports)> Categories,
    Period Period);
