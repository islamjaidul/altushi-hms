using System.Text;
using Hms.Kernel.Time;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Shell.Reporting;

/// <summary>
/// Handing a generated file to the browser. Small, but the filename is a header, and a header built
/// from user-influenced text is how an operator once locked themselves out with HTTP 431 (spec 0039
/// WP1.3) — so it is sanitised and bounded here rather than at each call site.
/// </summary>
public static class Download
{
    public const string CsvMime = "text/csv";

    private const int MaxNameLength = 120;

    public static FileContentResult File(byte[] bytes, string mime, string fileName)
        => new(bytes, mime) { FileDownloadName = fileName };

    /// <summary>
    /// <c>daily-attendance-summary_2026-07-01_2026-07-31.xlsx</c> — the report and the period it
    /// covers, so a folder of exports is still readable in six months.
    /// </summary>
    public static string Name(string reportKey, Period period, string extension)
    {
        var key = Slug(reportKey);
        var span = $"{period.Range.From:yyyy-MM-dd}_{period.Range.To:yyyy-MM-dd}";
        var name = $"{key}_{span}{extension}";
        return name.Length <= MaxNameLength ? name : name[..(MaxNameLength - extension.Length)] + extension;
    }

    private static string Slug(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            else if (ch is '-' or '_') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "report" : slug;
    }
}
