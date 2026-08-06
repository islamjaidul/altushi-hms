using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Hms.Shell.Reporting;

/// <summary>
/// A report as a real <c>.xlsx</c>, written with the framework alone.
/// <para>
/// <b>Why no package.</b> A valid workbook is a zip of six small XML parts, and
/// <c>ZipArchive</c> + <c>XmlWriter</c> are both in the shared framework. ClosedXML drags
/// DocumentFormat.OpenXml plus an imaging stack onto a 2 vCPU / 3 GB box (§16) for a feature set
/// this uses none of; EPPlus is commercial. Two hundred lines with a round-trip test is the cheaper
/// answer. If formulas, multiple sheets or conditional formatting are ever required, add
/// <c>DocumentFormat.OpenXml</c> — Microsoft, MIT, no image dependency — rather than ClosedXML.
/// </para>
/// <para>
/// The same purity rule as <see cref="ReportCsv"/> holds: this class can see the grammar and
/// nothing else, so an export cannot differ from the screen it came from (§12.4.6).
/// </para>
/// <para>
/// Numbers are written as numbers, not strings — an "Excel (data layout)" export whose figures do
/// not sum is not one. They carry the lakh–crore number format, so the file also <i>reads</i> the
/// way the screen did.
/// </para>
/// </summary>
public static class ReportXlsx
{
    public const string Mime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Header block rows before the blank line and the column headers.</summary>
    private const int HeaderRows = 5;

    /// <summary>The column-header row. Row 6 is left blank to separate it from the header block.</summary>
    public const int HeaderRowIndex = 7;

    private const int StyleDefault = 0;
    private const int StyleBold = 1;
    private const int StyleMoney = 2;

    public static byte[] Write(ExportHeader header, ReportTable table)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Part(zip, "[Content_Types].xml", ContentTypes);
            Part(zip, "_rels/.rels", RootRels);
            Part(zip, "xl/workbook.xml", Workbook);
            Part(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
            Part(zip, "xl/styles.xml", Styles);
            Part(zip, "xl/worksheets/sheet1.xml", w => Sheet(w, header, table));
        }
        return buffer.ToArray();
    }

    // ---- the sheet ------------------------------------------------------------------------------

    private static void Sheet(XmlWriter w, ExportHeader header, ReportTable table)
    {
        var columnCount = Math.Max(table.Columns.Count, 2);
        var lastColumn = ColumnName(columnCount);
        var lastRow = HeaderRowIndex + table.Rows.Count + (table.Totals is { Count: > 0 } ? 1 : 0);

        w.WriteStartElement("worksheet",
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        w.WriteStartElement("sheetViews");
        w.WriteStartElement("sheetView");
        w.WriteAttributeString("workbookViewId", "0");
        // Freeze everything above the data, so scrolling a 5,000-row register keeps the headers.
        w.WriteStartElement("pane");
        w.WriteAttributeString("ySplit", HeaderRowIndex.ToString());
        w.WriteAttributeString("topLeftCell", "A" + (HeaderRowIndex + 1));
        w.WriteAttributeString("activePane", "bottomLeft");
        w.WriteAttributeString("state", "frozen");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("sheetData");

        var r = 1;
        HeaderLine(w, r++, "Report", header.ReportName);
        HeaderLine(w, r++, "Period", header.PeriodWords);
        HeaderLine(w, r++, "Filters", header.Filters.Count == 0
            ? "None"
            : string.Join("; ", header.Filters.Select(f => f.Label)));
        HeaderLine(w, r++, "Run by", header.RunBy);
        HeaderLine(w, r++, "Run at", header.RunAt);
        r = HeaderRows + 2;   // row 6 stays empty

        if (table.Columns.Count > 0)
        {
            w.WriteStartElement("row");
            w.WriteAttributeString("r", r.ToString());
            for (var c = 0; c < table.Columns.Count; c++)
                Text(w, ColumnName(c + 1) + r, table.Columns[c].Header, StyleBold);
            w.WriteEndElement();
        }
        r++;

        foreach (var row in table.Rows)
        {
            w.WriteStartElement("row");
            w.WriteAttributeString("r", r.ToString());
            WriteCells(w, row.Cells, table.Columns, r, row.IsSubtotal ? StyleBold : StyleDefault);
            w.WriteEndElement();
            r++;
        }

        if (table.Totals is { Count: > 0 } totals)
        {
            w.WriteStartElement("row");
            w.WriteAttributeString("r", r.ToString());
            WriteCells(w, totals, table.Columns, r, StyleBold);
            w.WriteEndElement();
            r++;
        }

        if (table.Rows.Count == 0 && table.EmptyReason is { } reason)
        {
            w.WriteStartElement("row");
            w.WriteAttributeString("r", r.ToString());
            Text(w, "A" + r, reason, StyleDefault);
            w.WriteEndElement();
        }

        w.WriteEndElement();   // sheetData

        // One header row, no merged cells (§12.4.6) — which is exactly what autoFilter needs.
        if (table.Columns.Count > 0 && table.Rows.Count > 0)
        {
            w.WriteStartElement("autoFilter");
            w.WriteAttributeString("ref", $"A{HeaderRowIndex}:{lastColumn}{lastRow}");
            w.WriteEndElement();
        }

        w.WriteEndElement();   // worksheet
    }

    private static void WriteCells(XmlWriter w, IReadOnlyList<ReportCell> cells,
        IReadOnlyList<ReportColumn> columns, int row, int textStyle)
    {
        for (var c = 0; c < cells.Count; c++)
        {
            var reference = ColumnName(c + 1) + row;
            var cell = cells[c];
            var money = c < columns.Count && columns[c].Money;

            if (cell.Number is { } n)
                Number(w, reference, n, money ? StyleMoney : textStyle);
            else
                Text(w, reference, cell.Text, textStyle);
        }
    }

    private static void HeaderLine(XmlWriter w, int row, string label, string value)
    {
        w.WriteStartElement("row");
        w.WriteAttributeString("r", row.ToString());
        Text(w, "A" + row, label, StyleBold);
        Text(w, "B" + row, value, StyleDefault);
        w.WriteEndElement();
    }

    /// <summary>An inline string — no shared-strings part to build, and no index to keep in step.</summary>
    private static void Text(XmlWriter w, string reference, string? value, int style)
    {
        w.WriteStartElement("c");
        w.WriteAttributeString("r", reference);
        w.WriteAttributeString("t", "inlineStr");
        if (style != StyleDefault) w.WriteAttributeString("s", style.ToString());
        w.WriteStartElement("is");
        w.WriteElementString("t", Clean(value));
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void Number(XmlWriter w, string reference, long value, int style)
    {
        w.WriteStartElement("c");
        w.WriteAttributeString("r", reference);
        if (style != StyleDefault) w.WriteAttributeString("s", style.ToString());
        w.WriteElementString("v", value.ToString());
        w.WriteEndElement();
    }

    /// <summary>XML 1.0 forbids most control characters; a stray one makes the file unreadable.</summary>
    private static string Clean(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var sb = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (ch is '\t' or '\n' or '\r' || !char.IsControl(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    internal static string ColumnName(int oneBased)
    {
        var n = oneBased;
        var name = "";
        while (n > 0)
        {
            var rem = (n - 1) % 26;
            name = (char)('A' + rem) + name;
            n = (n - 1) / 26;
        }
        return name;
    }

    // ---- the fixed parts ------------------------------------------------------------------------

    private static void Part(ZipArchive zip, string path, Action<XmlWriter> body)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        // The writer must be flushed and closed before the entry stream is, or the last part is
        // truncated and Excel reports "unreadable content" on a file that looks fine.
        using var w = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            CloseOutput = false,
        });
        w.WriteStartDocument(standalone: true);
        body(w);
        w.WriteEndDocument();
        w.Flush();
    }

    private static void ContentTypes(XmlWriter w)
    {
        w.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
        Default(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
        Default(w, "xml", "application/xml");
        Override(w, "/xl/workbook.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        Override(w, "/xl/worksheets/sheet1.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        Override(w, "/xl/styles.xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        w.WriteEndElement();

        static void Default(XmlWriter w, string ext, string type)
        {
            w.WriteStartElement("Default");
            w.WriteAttributeString("Extension", ext);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }

        static void Override(XmlWriter w, string part, string type)
        {
            w.WriteStartElement("Override");
            w.WriteAttributeString("PartName", part);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }
    }

    private static void RootRels(XmlWriter w)
    {
        w.WriteStartElement("Relationships",
            "http://schemas.openxmlformats.org/package/2006/relationships");
        Relationship(w, "rId1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument",
            "xl/workbook.xml");
        w.WriteEndElement();
    }

    private static void WorkbookRels(XmlWriter w)
    {
        w.WriteStartElement("Relationships",
            "http://schemas.openxmlformats.org/package/2006/relationships");
        Relationship(w, "rId1",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet",
            "worksheets/sheet1.xml");
        Relationship(w, "rId2",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles",
            "styles.xml");
        w.WriteEndElement();
    }

    private static void Relationship(XmlWriter w, string id, string type, string target)
    {
        w.WriteStartElement("Relationship");
        w.WriteAttributeString("Id", id);
        w.WriteAttributeString("Type", type);
        w.WriteAttributeString("Target", target);
        w.WriteEndElement();
    }

    private static void Workbook(XmlWriter w)
    {
        w.WriteStartElement("workbook",
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        w.WriteAttributeString("xmlns", "r", null,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
        w.WriteStartElement("sheets");
        w.WriteStartElement("sheet");
        w.WriteAttributeString("name", "Report");
        w.WriteAttributeString("sheetId", "1");
        w.WriteAttributeString("id",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships", "rId1");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void Styles(XmlWriter w)
    {
        w.WriteStartElement("styleSheet",
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main");

        // The lakh–crore grouping the product uses everywhere (Ui.Group): 12,34,56,789 rather than
        // 123,456,789. Whole taka, no decimals (§C3, N-HR6).
        w.WriteStartElement("numFmts");
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("numFmt");
        w.WriteAttributeString("numFmtId", "164");
        w.WriteAttributeString("formatCode",
            "[>=10000000]##\\,##\\,##\\,##0;[>=100000]##\\,##\\,##0;##,##0");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("fonts");
        w.WriteAttributeString("count", "2");
        Font(w, bold: false);
        Font(w, bold: true);
        w.WriteEndElement();

        w.WriteStartElement("fills");
        w.WriteAttributeString("count", "2");
        Fill(w, "none");
        Fill(w, "gray125");
        w.WriteEndElement();

        w.WriteStartElement("borders");
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("border");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("cellStyleXfs");
        w.WriteAttributeString("count", "1");
        Xf(w, fontId: 0, numFmtId: 0);
        w.WriteEndElement();

        w.WriteStartElement("cellXfs");
        w.WriteAttributeString("count", "3");
        Xf(w, fontId: 0, numFmtId: 0);                       // 0 — default
        Xf(w, fontId: 1, numFmtId: 0);                       // 1 — bold (headers, totals)
        Xf(w, fontId: 0, numFmtId: 164, applyFormat: true);  // 2 — money
        w.WriteEndElement();

        w.WriteEndElement();

        static void Font(XmlWriter w, bool bold)
        {
            w.WriteStartElement("font");
            if (bold)
            {
                w.WriteStartElement("b");
                w.WriteEndElement();
            }
            Sized(w, "sz", "11");
            Sized(w, "name", "Calibri");
            w.WriteEndElement();
        }

        static void Sized(XmlWriter w, string element, string value)
        {
            w.WriteStartElement(element);
            w.WriteAttributeString("val", value);
            w.WriteEndElement();
        }

        static void Fill(XmlWriter w, string pattern)
        {
            w.WriteStartElement("fill");
            w.WriteStartElement("patternFill");
            w.WriteAttributeString("patternType", pattern);
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void Xf(XmlWriter w, int fontId, int numFmtId, bool applyFormat = false)
        {
            w.WriteStartElement("xf");
            w.WriteAttributeString("numFmtId", numFmtId.ToString());
            w.WriteAttributeString("fontId", fontId.ToString());
            w.WriteAttributeString("fillId", "0");
            w.WriteAttributeString("borderId", "0");
            if (applyFormat) w.WriteAttributeString("applyNumberFormat", "1");
            w.WriteEndElement();
        }
    }
}
