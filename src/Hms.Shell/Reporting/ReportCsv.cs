using System.Text;

namespace Hms.Shell.Reporting;

/// <summary>
/// A report as CSV.
/// <para>
/// <b>The signature is the guarantee.</b> This type can see an <see cref="ExportHeader"/> and a
/// <see cref="ReportTable"/> and nothing else — no <c>DbContext</c>, no transaction, no report
/// class. It therefore cannot fetch a row the screen did not have, cannot ignore the display cap and
/// cannot re-run a query without the filters. That is how §12.4.6's "the export contains exactly
/// what the screen showed" is enforced structurally rather than by review, and an architecture test
/// fails the build if a data type ever appears in reach of this class.
/// </para>
/// </summary>
public static class ReportCsv
{
    public static byte[] Write(ExportHeader header, ReportTable table)
    {
        var sb = new StringBuilder();

        // §12.4.1 / §12.4.6: the file states what it is, for what period, filtered how, by whom.
        Line(sb, "Report", header.ReportName);
        Line(sb, "Period", header.PeriodWords);
        Line(sb, "Filters", header.Filters.Count == 0
            ? "None"
            : string.Join("; ", header.Filters.Select(f => f.Label)));
        Line(sb, "Run by", header.RunBy);
        Line(sb, "Run at", header.RunAt);
        sb.Append("\r\n");

        if (table.Columns.Count > 0)
            sb.Append(string.Join(',', table.Columns.Select(c => Escape(c.Header)))).Append("\r\n");

        foreach (var row in table.Rows)
            sb.Append(string.Join(',', row.Cells.Select(Value))).Append("\r\n");

        if (table.Totals is { Count: > 0 } totals)
            sb.Append(string.Join(',', totals.Select(Value))).Append("\r\n");

        if (table.Rows.Count == 0 && table.EmptyReason is { } reason)
            sb.Append(Escape(reason)).Append("\r\n");

        if (table.Truncated)
        {
            sb.Append(Escape(
                $"Showing {table.Rows.Count:N0} of {table.MatchedRows:N0} rows — narrow the period "
                + "or the filters to export them all.")).Append("\r\n");
        }

        // UTF-8 *with* BOM. Without it Excel on a Bangladeshi desktop reads the file as the system
        // codepage and every Bangla name arrives as mojibake. This is not optional.
        //
        // The preamble is written by hand: Encoding.GetBytes never emits it whatever the encoding
        // was constructed with — only a StreamWriter does — so constructing a UTF8Encoding(true)
        // here and trusting it is a silent no-op. A test pins the first three bytes.
        var body = Encoding.UTF8.GetBytes(sb.ToString());
        var preamble = Encoding.UTF8.GetPreamble();
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private static void Line(StringBuilder sb, string label, string value)
        => sb.Append(Escape(label)).Append(',').Append(Escape(value)).Append("\r\n");

    /// <summary>CSV writes the raw number, so a column re-imports and sums.</summary>
    private static string Value(ReportCell cell)
        => cell.Number is { } n ? n.ToString() : Escape(cell.Text);

    private static string Escape(string? raw)
    {
        var s = raw ?? "";

        // A cell beginning with a formula trigger is executed by Excel on open. Prefixing with an
        // apostrophe is the standard mitigation and the one Excel itself writes; it costs a
        // character of fidelity on text that would otherwise be a code-execution vector.
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r') s = "'" + s;

        return s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }
}
