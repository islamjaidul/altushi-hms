using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Hms.Kernel.Time;
using Hms.Shell.Reporting;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0055 — the export writers (module PRD §12.4.6). Pure functions of (header, table), so they
/// are tested here in milliseconds rather than through a page.
/// </summary>
public class ReportExportTests
{
    private static readonly ExportHeader Header = new(
        ReportName: "Daily attendance summary",
        PeriodWords: "1 – 31 July 2026 · Asia/Dhaka",
        Filters: [new FilterChip("Org unit: Nursing", "/x"), new FilterChip("Status: Active", "/y")],
        RunBy: "Farhana Akter",
        RunAt: "06 Aug 2026, 11:42");

    private static ReportTable Table() => new()
    {
        Columns =
        [
            new("Employee"),
            new("Present", ReportAlign.Right),
            new("Gross", ReportAlign.Right, Money: true),
        ],
        Rows =
        [
            new([new ReportCell("Farhana Akter"), new ReportCell("22", 22), new ReportCell("45,000", 45000)]),
            new([new ReportCell("Rahim Uddin"), new ReportCell("20", 20), new ReportCell("38,500", 38500)]),
        ],
        Totals = [new ReportCell("Total"), new ReportCell("42", 42), new ReportCell("83,500", 83500)],
        MatchedRows = 2,
    };

    // ---- CSV -----------------------------------------------------------------------------------

    private static string Csv(ReportTable table)
        => Encoding.UTF8.GetString(ReportCsv.Write(Header, table));

    [Fact]
    public void Csv_starts_with_a_utf8_bom()
    {
        // Without it Excel on a Bangladeshi desktop reads the system codepage and every Bangla name
        // arrives as mojibake.
        var bytes = ReportCsv.Write(Header, Table());
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    [Fact]
    public void Csv_states_what_the_report_is_and_how_it_was_filtered()
    {
        var csv = Csv(Table());
        Assert.Contains("Report,Daily attendance summary", csv);
        Assert.Contains("1 – 31 July 2026 · Asia/Dhaka", csv);
        Assert.Contains("Org unit: Nursing; Status: Active", csv);
        Assert.Contains("Run by,Farhana Akter", csv);
    }

    [Fact]
    public void Csv_writes_raw_numbers_so_a_column_sums_after_reimport()
    {
        var csv = Csv(Table());
        Assert.Contains("Farhana Akter,22,45000", csv);
        Assert.DoesNotContain("45,000", csv.Split("\r\n")[7]);
    }

    [Theory]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\r\nlines", "\"two\r\nlines\"")]
    public void Csv_quotes_what_rfc4180_requires(string raw, string expected)
    {
        var table = Table() with { Rows = [new([new ReportCell(raw)])], MatchedRows = 1 };
        Assert.Contains(expected, Csv(table));
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+SUM(A1)")]
    [InlineData("-2+3")]
    [InlineData("@lookup")]
    public void Csv_defuses_a_cell_excel_would_execute(string payload)
    {
        var table = Table() with { Rows = [new([new ReportCell(payload)])], MatchedRows = 1 };
        var csv = Csv(table);

        Assert.Contains("'" + payload, csv);
        Assert.DoesNotContain("\r\n" + payload, csv);
    }

    [Fact]
    public void Csv_keeps_bangla_intact()
    {
        var table = Table() with { Rows = [new([new ReportCell("ফারহানা আক্তার")])], MatchedRows = 1 };
        Assert.Contains("ফারহানা আক্তার", Csv(table));
    }

    [Fact]
    public void Csv_carries_the_reason_an_empty_report_is_empty()
        => Assert.Contains("No attendance was recorded",
            Csv(ReportTable.Empty("No attendance was recorded in this period.")));

    [Fact]
    public void Csv_says_when_it_is_not_the_whole_answer()
    {
        var table = Table() with { MatchedRows = 42_318 };
        Assert.Contains("Showing 2 of 42,318 rows", Csv(table));
    }

    // ---- xlsx ----------------------------------------------------------------------------------

    private static ZipArchive Open(byte[] bytes)
        => new(new MemoryStream(bytes), ZipArchiveMode.Read);

    private static XDocument Read(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        Assert.NotNull(entry);
        using var s = entry!.Open();
        return XDocument.Load(s);
    }

    [Fact]
    public void Xlsx_is_a_zip_containing_every_part_a_workbook_needs()
    {
        using var zip = Open(ReportXlsx.Write(Header, Table()));
        string[] required =
        [
            "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml",
            "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml",
        ];
        foreach (var part in required) Assert.NotNull(zip.GetEntry(part));
    }

    [Fact]
    public void Every_part_is_well_formed_xml()
    {
        using var zip = Open(ReportXlsx.Write(Header, Table()));
        foreach (var entry in zip.Entries)
        {
            using var s = entry.Open();
            // A truncated last part is the classic ZipArchive/XmlWriter disposal bug, and Excel
            // reports it only as "unreadable content".
            _ = XDocument.Load(s);
        }
    }

    [Fact]
    public void A_money_cell_is_a_number_not_a_string()
    {
        using var zip = Open(ReportXlsx.Write(Header, Table()));
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var sheet = Read(zip, "xl/worksheets/sheet1.xml");

        var cell = sheet.Descendants(ns + "c")
            .Single(c => (string?)c.Attribute("r") == "C" + (ReportXlsx.HeaderRowIndex + 1));

        Assert.Null(cell.Attribute("t"));                       // not inlineStr
        Assert.Equal("45000", cell.Element(ns + "v")!.Value);   // the raw figure, so Excel sums it
        Assert.Equal("2", (string?)cell.Attribute("s"));        // the lakh–crore money style
    }

    [Fact]
    public void The_money_style_uses_lakh_crore_grouping()
    {
        using var zip = Open(ReportXlsx.Write(Header, Table()));
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var format = (string?)Read(zip, "xl/styles.xml")
            .Descendants(ns + "numFmt").Single().Attribute("formatCode");

        // 1,00,000 not 100,000 — the grouping Ui.Group renders on screen.
        Assert.Contains("[>=100000]", format);
        Assert.Contains("[>=10000000]", format);
    }

    [Fact]
    public void There_is_exactly_one_header_row_and_it_is_where_the_freeze_expects_it()
    {
        using var zip = Open(ReportXlsx.Write(Header, Table()));
        var ns = XNamespace.Get("http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        var sheet = Read(zip, "xl/worksheets/sheet1.xml");

        var headerRow = sheet.Descendants(ns + "row")
            .Single(r => (string?)r.Attribute("r") == ReportXlsx.HeaderRowIndex.ToString());
        Assert.Equal(3, headerRow.Elements(ns + "c").Count());
        Assert.Contains("Employee", headerRow.Value);

        var pane = sheet.Descendants(ns + "pane").Single();
        Assert.Equal(ReportXlsx.HeaderRowIndex.ToString(), (string?)pane.Attribute("ySplit"));

        // §12.4.6: one header row, no merged cells.
        Assert.Empty(sheet.Descendants(ns + "mergeCell"));
    }

    [Fact]
    public void The_sheet_carries_the_report_name_period_and_filters()
    {
        using var zip = Open(ReportXlsx.Write(Header, Table()));
        var text = Read(zip, "xl/worksheets/sheet1.xml").Root!.Value;

        Assert.Contains("Daily attendance summary", text);
        Assert.Contains("1 – 31 July 2026 · Asia/Dhaka", text);
        Assert.Contains("Org unit: Nursing; Status: Active", text);
    }

    [Fact]
    public void An_empty_report_still_says_why()
    {
        using var zip = Open(ReportXlsx.Write(Header, ReportTable.Empty("Nothing was recorded.")));
        Assert.Contains("Nothing was recorded.", Read(zip, "xl/worksheets/sheet1.xml").Root!.Value);
    }

    [Fact]
    public void A_control_character_does_not_produce_an_unreadable_file()
    {
        var table = Table() with
        {
            Rows = [new([new ReportCell("badname")])],
            MatchedRows = 1,
        };
        using var zip = Open(ReportXlsx.Write(Header, table));
        _ = Read(zip, "xl/worksheets/sheet1.xml");   // parses at all — that is the assertion
    }

    [Theory]
    [InlineData(1, "A")]
    [InlineData(26, "Z")]
    [InlineData(27, "AA")]
    [InlineData(52, "AZ")]
    [InlineData(53, "BA")]
    public void Column_names_carry_past_z(int index, string expected)
        => Assert.Equal(expected, ReportXlsxAccessor.ColumnName(index));

    // ---- file names ------------------------------------------------------------------------------

    [Fact]
    public void A_download_is_named_for_its_report_and_its_period()
    {
        var period = PeriodResolver.Of(PeriodGrain.Month, new DateOnly(2026, 7, 10), 1,
            PeriodCalendar.Default, new DateOnly(2026, 8, 6));

        Assert.Equal("daily-attendance-summary_2026-07-01_2026-07-31.xlsx",
            Download.Name("daily-attendance-summary", period, ".xlsx"));
    }

    [Fact]
    public void A_hostile_report_key_cannot_become_a_header_problem()
    {
        var period = PeriodResolver.Of(PeriodGrain.Day, new DateOnly(2026, 8, 6), 1,
            PeriodCalendar.Default, new DateOnly(2026, 8, 6));

        var name = Download.Name("../../etc/passwd" + new string('x', 500), period, ".csv");

        Assert.DoesNotContain("/", name);
        Assert.DoesNotContain("..", name);
        Assert.True(name.Length <= 120, $"name was {name.Length} characters");
        Assert.EndsWith(".csv", name);
    }
}

/// <summary>Reaches the column-name helper without making it public on the writer.</summary>
internal static class ReportXlsxAccessor
{
    public static string ColumnName(int oneBased) => (string)typeof(ReportXlsx)
        .GetMethod("ColumnName", System.Reflection.BindingFlags.NonPublic
                                 | System.Reflection.BindingFlags.Static)!
        .Invoke(null, [oneBased])!;
}
