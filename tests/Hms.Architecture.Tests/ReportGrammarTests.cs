using System.Reflection;
using System.Text.RegularExpressions;
using Hms.Hr.Screens.Reports;
using Hms.Shell.Reporting;

namespace Hms.Architecture.Tests;

/// <summary>
/// Spec 0055 — the rules that keep the report grammar one product across four more phases.
/// <para>
/// The reporting layer was built on the premise that ~60 reports are 60 <i>classes</i> sharing one
/// page, so the grammar cannot drift. That premise only holds while these hold: a report cannot
/// declare its own permission story, cannot build its own links, and cannot hand an exporter
/// anything but the grammar. Every assertion here is a thing a later phase would otherwise get
/// wrong quietly.
/// </para>
/// </summary>
public class ReportGrammarTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "hms-erp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    private static IEnumerable<string> ReportSources() =>
        Directory.EnumerateFiles(
            Path.Combine(RepoRoot(), "src", "Modules", "Hr", "Hms.Hr.Screens", "Reports"),
            "*.cs", SearchOption.AllDirectories);

    // ---- the catalogue ---------------------------------------------------------------------------

    [Fact]
    public void Every_report_key_is_unique()
    {
        var duplicates = ReportCatalog.All
            .GroupBy(r => r.Descriptor.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "Two reports share a key, so one is unreachable: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void Every_report_key_is_kebab_case()
    {
        // Keys are in URLs and, once saved views exist, in an operator's saved configuration.
        // Renaming one breaks every link anybody ever sent.
        var bad = ReportCatalog.All
            .Select(r => r.Descriptor.Key)
            .Where(k => !Regex.IsMatch(k, "^[a-z][a-z0-9]*(-[a-z0-9]+)*$"))
            .ToList();

        Assert.True(bad.Count == 0, "Report keys must be kebab-case: " + string.Join(", ", bad));
    }

    [Fact]
    public void Every_report_says_what_it_answers()
    {
        // §13's "Answers" column is what the report centre lists. A report with no question is a
        // row an operator has to open to understand.
        var silent = ReportCatalog.All
            .Where(r => string.IsNullOrWhiteSpace(r.Descriptor.Question)
                        || string.IsNullOrWhiteSpace(r.Descriptor.Name))
            .Select(r => r.Descriptor.Key)
            .ToList();

        Assert.True(silent.Count == 0, "Reports with no name or question: " + string.Join(", ", silent));
    }

    [Fact]
    public void Every_report_lands_in_a_category_the_centre_renders()
    {
        var stray = ReportCatalog.All
            .Where(r => !ReportCategory.All.Contains(r.Descriptor.Category))
            .Select(r => $"{r.Descriptor.Key} → {r.Descriptor.Category}")
            .ToList();

        // A category outside §13's list renders nowhere, so the report would be unreachable.
        Assert.True(stray.Count == 0, "Reports in an unrendered category: " + string.Join(", ", stray));
    }

    // ---- salary confidentiality (D6) --------------------------------------------------------------

    [Fact]
    public void A_salary_bearing_report_requires_the_salary_permission()
    {
        foreach (var report in ReportCatalog.All)
        {
            var d = report.Descriptor;
            if (d.PermissionOverride is not null) continue;

            Assert.Equal(d.SalaryBearing ? "hr.reports.salary" : "hr.reports.view",
                d.RequiredPermission);
        }
    }

    /// <summary>
    /// The salary-bearing set, transcribed from module PRD §13's <c>$</c> markers. Pins the
    /// catalogue to the document: a later phase that adds a payroll register and forgets the flag
    /// fails here rather than leaking a figure.
    /// </summary>
    [Fact]
    public void The_payroll_registers_are_all_marked_salary_bearing()
    {
        var payroll = ReportCatalog.All
            .Where(r => r.Descriptor.Category == ReportCategory.Payroll
                        || r.Descriptor.Category == ReportCategory.Statutory)
            .ToList();

        Assert.NotEmpty(payroll);

        var unmarked = payroll.Where(r => !r.Descriptor.SalaryBearing)
            .Select(r => r.Descriptor.Key).ToList();

        Assert.True(unmarked.Count == 0,
            "§13 marks every payroll and statutory report as salary-bearing ($). These are not: "
            + string.Join(", ", unmarked));
    }

    [Fact]
    public void No_people_time_or_leave_report_is_salary_bearing_by_accident()
    {
        // A department head holds hr.reports.view and no salary grant. If a directory or a muster
        // roll were ever marked salary-bearing it would silently vanish for them.
        var marked = ReportCatalog.All
            .Where(r => r.Descriptor.Category is "People" or "Time & attendance" or "Leave")
            .Where(r => r.Descriptor.SalaryBearing)
            .Select(r => r.Descriptor.Key)
            .ToList();

        Assert.True(marked.Count == 0,
            "These would disappear for a manager who holds no salary grant: " + string.Join(", ", marked));
    }

    [Fact]
    public void Only_the_governance_registers_override_their_permission()
    {
        var overridden = ReportCatalog.All
            .Where(r => r.Descriptor.PermissionOverride is not null)
            .ToList();

        Assert.All(overridden, r =>
        {
            Assert.Equal(ReportCategory.Governance, r.Descriptor.Category);
            Assert.Equal("hr.audit.view", r.Descriptor.PermissionOverride);
        });
    }

    // ---- the structural guarantees ----------------------------------------------------------------

    /// <summary>
    /// §12.4.6, "the export contains exactly what the screen showed", expressed as a fact about
    /// types rather than a rule reviewers must remember: the writers can reach the grammar and
    /// nothing else, so there is no path by which a file could hold a row the screen did not.
    /// </summary>
    [Theory]
    [InlineData(typeof(ReportCsv))]
    [InlineData(typeof(ReportXlsx))]
    public void An_exporter_can_see_only_the_grammar(Type exporter)
    {
        string[] forbidden =
        [
            "Microsoft.EntityFrameworkCore", "Npgsql",
            "Hms.Kernel.Data", "Hms.Hr.Data", "Hms.Hr.Screens",
        ];

        var reachable = exporter
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .Select(t => t.Namespace ?? "")
            .Distinct()
            .ToList();

        var leaks = reachable
            .Where(ns => forbidden.Any(f => ns.StartsWith(f, StringComparison.Ordinal)))
            .ToList();

        Assert.True(leaks.Count == 0,
            $"{exporter.Name} can reach data types, so an export could differ from its screen: "
            + string.Join(", ", leaks));
    }

    /// <summary>
    /// §12.4.5 / D4: a drill-through must carry the period and the live filters. It can only do so
    /// if it was built by <c>ReportContext.Drill</c>, so a report composing a report URL by hand is
    /// a defect whether or not it happens to work today.
    /// </summary>
    [Fact]
    public void No_report_builds_a_report_url_by_hand()
    {
        var offenders = new List<string>();

        foreach (var file in ReportSources())
        {
            if (Path.GetFileName(file) == "HrReport.cs") continue;   // Drill itself lives here

            var text = File.ReadAllText(file);
            if (text.Contains("\"/hr/reports/", StringComparison.Ordinal))
                offenders.Add(Path.GetFileName(file));
        }

        Assert.True(offenders.Count == 0,
            "These compose a report URL directly instead of calling ReportContext.Drill, so the "
            + "period and the filters would be dropped: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// §12.4.4: an empty report explains itself. <c>ReportTable</c> enforces it at construction;
    /// this proves the enforcement fires rather than sitting there unreached.
    /// </summary>
    [Fact]
    public void A_report_with_no_rows_must_say_why()
    {
        var silent = new ReportTable { Columns = [], Rows = [] };

        var thrown = Assert.Throws<InvalidOperationException>(() => silent.Validated());
        Assert.Contains("§12.4.4", thrown.Message);
    }

    [Fact]
    public void The_empty_helper_always_produces_a_sentence()
    {
        // Even a caller who passes nothing gets a sentence rather than a blank grid.
        Assert.False(string.IsNullOrWhiteSpace(ReportTable.Empty("").EmptyReason));
        _ = ReportTable.Empty("").Validated();
    }

    // ---- the period standard ----------------------------------------------------------------------

    /// <summary>
    /// §12.3 is binding on "every report, register, dashboard, calendar and log". These are the HR
    /// screens that select a period, and each must render the one control rather than inventing a
    /// second notion of "when" — which is exactly how the module ended up with ten of them.
    /// </summary>
    [Theory]
    [InlineData("Reports")]
    [InlineData("Index")]
    [InlineData("Team")]
    [InlineData("Timeline")]
    public void Every_period_screen_renders_the_one_control(string page)
    {
        var view = Path.Combine(RepoRoot(), "src", "Modules", "Hr", "Hms.Hr.Screens",
            "Pages", "Hr", page + ".cshtml");

        Assert.True(File.Exists(view), $"{page}.cshtml is missing");
        Assert.Contains("_PeriodBar", File.ReadAllText(view), StringComparison.Ordinal);
    }

    /// <summary>
    /// The Dhaka offset is one fact in one place (<c>Hms.Kernel.Time.Dhaka</c>). It used to be
    /// hand-written in four files, one of which had already drifted off the shared helper.
    /// <para>
    /// The allowlist only ever shrinks. Nothing may be added to it.
    /// </para>
    /// </summary>
    [Fact]
    public void Nobody_hand_writes_the_dhaka_offset()
    {
        string[] allowed =
        [
            // Pre-dating spec 0055. Each is a separate, tracked cleanup: the HR screens were
            // converted with the spec, these were not because they are outside its blast radius.
            Path.Combine("src", "Hms.Web", "Pages", "Ipd", "Reports.cshtml.cs"),
            Path.Combine("src", "Modules", "Hr", "Hms.Hr", "AttendanceService.cs"),
            Path.Combine("src", "Modules", "Hr", "Hms.Hr", "CsvPunchSource.cs"),
            // Demo-data generation, where the offset is a literal in a constructed instant rather
            // than a conversion — a different shape of the same fact, and harmless in a dev seed.
            Path.Combine("src", "Hms.Web", "HistoryGenerator.cs"),
        ];

        var root = RepoRoot();
        var offenders = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => !f.EndsWith(Path.Combine("Hms.Kernel", "Time", "Dhaka.cs"), StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f) is var t
                        && (t.Contains("FromHours(6)", StringComparison.Ordinal)
                            || t.Contains("UtcNow.AddHours(6)", StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(root, f))
            .Where(rel => !allowed.Contains(rel))
            .ToList();

        Assert.True(offenders.Count == 0,
            "The Dhaka offset belongs to Hms.Kernel.Time.Dhaka. These write it out again: "
            + string.Join(", ", offenders));
    }
}
