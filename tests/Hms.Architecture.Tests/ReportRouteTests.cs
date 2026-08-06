using System.Text.RegularExpressions;
using Hms.Hr.Screens.Reports;
using Hms.Shell.Reporting;

namespace Hms.Architecture.Tests;

/// <summary>
/// Spec 0056 — the rule that would have caught the defect this spec fixed.
/// <para>
/// Spec 0055 shipped <c>ReportContext.Employee</c> building <c>/hr/employee/{id}</c>, singular,
/// against a page routed at <c>/hr/employees/{id}</c>. <b>Every employee drill-through in every
/// report was a 404</b> — the most-clicked link in the report centre, and nothing failed: the URL
/// was well-formed, the report rendered, the cell was blue. A test can only catch that by comparing
/// what a report can produce against the routes that actually exist on disk.
/// </para>
/// </summary>
public class ReportRouteTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "hms-erp.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>Every <c>@page "/…"</c> in the HR screens library, as a matchable pattern.</summary>
    private static List<string> RouteTemplates()
    {
        var pages = Directory.EnumerateFiles(
            Path.Combine(RepoRoot(), "src", "Modules", "Hr", "Hms.Hr.Screens", "Pages"),
            "*.cshtml", SearchOption.AllDirectories);

        var routes = new List<string>();
        foreach (var page in pages)
        {
            var first = File.ReadLines(page).FirstOrDefault() ?? "";
            var m = Regex.Match(first, "^@page\\s+\"(?<route>/[^\"]*)\"");
            if (m.Success) routes.Add(m.Groups["route"].Value);
        }
        return routes;
    }

    /// <summary>Turns <c>/hr/employees/{id:long}/timeline</c> into a regex that matches a real URL.</summary>
    private static Regex ToMatcher(string template)
    {
        var pattern = Regex.Escape(template);
        // {id:long} and {key?} — escaped braces, so the replacement works on the escaped text.
        pattern = Regex.Replace(pattern, @"\\\{[^}]*\?\\?\}", "[^/]*");
        pattern = Regex.Replace(pattern, @"\\\{[^}]*\\?\}", "[^/]+");
        return new Regex("^" + pattern + "$", RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// The links a report can build, exercised against a context carrying nothing but a period —
    /// which is exactly the shape a report gets. Compared against every route on disk.
    /// </summary>
    [Fact]
    public void Every_url_a_report_can_build_is_served_by_a_page()
    {
        var routes = RouteTemplates();
        Assert.NotEmpty(routes);

        var matchers = routes.Select(ToMatcher).ToList();

        var ctx = new ReportContext
        {
            BranchId = 1,
            Period = null!,
            Filters = new HrReportFilters(),
            Raw = FilterSet.Empty(),
            Scope = null!,
            PeriodPairs = [new KeyValuePair<string, string>("p", "month")],
        };

        var urls = new List<string>
        {
            ctx.Employee(42),
            ctx.Drill("employee-directory"),
            ctx.Drill("attendance-register", ("employee", "42")),
        };

        foreach (var url in urls)
        {
            var path = url.Split('?')[0];
            Assert.True(matchers.Any(m => m.IsMatch(path)),
                $"A report can build \"{path}\", which no page serves. "
                + "Routes on disk: " + string.Join(", ", routes));
        }
    }

    /// <summary>
    /// Every report key resolves through the one page. A key with no report behind it is a 404 in
    /// the report centre's own list.
    /// </summary>
    [Fact]
    public void Every_catalogued_key_resolves_to_its_report()
    {
        foreach (var report in ReportCatalog.All)
        {
            var found = ReportCatalog.Find(report.Descriptor.Key);
            Assert.NotNull(found);
            Assert.Equal(report.Descriptor.Key, found!.Descriptor.Key);
        }

        Assert.Null(ReportCatalog.Find("no-such-report"));
        Assert.Null(ReportCatalog.Find(null));
    }

    /// <summary>
    /// Spec 0056's registers, pinned by key. §12.2 lists some of them under People as screens;
    /// making them reports is a deliberate reading of §12.2 against ADR-0029, and a later phase
    /// quietly turning one back into a page would take the grammar with it.
    /// </summary>
    [Theory]
    [InlineData("probation-due", ReportCategory.People, false)]
    [InlineData("contract-expiry", ReportCategory.People, false)]
    [InlineData("document-expiry", ReportCategory.People, false)]
    [InlineData("licence-register", ReportCategory.People, false)]
    [InlineData("nominee-register", ReportCategory.People, false)]
    [InlineData("separation-clearance", ReportCategory.People, false)]
    [InlineData("anniversaries", ReportCategory.People, false)]
    [InlineData("settlement-register", ReportCategory.Payroll, true)]
    [InlineData("letters-issued", ReportCategory.Governance, false)]
    public void The_lifecycle_registers_are_in_the_catalogue(string key, string category, bool salary)
    {
        var report = ReportCatalog.Find(key);

        Assert.NotNull(report);
        Assert.Equal(category, report!.Descriptor.Category);
        Assert.Equal(salary, report.Descriptor.SalaryBearing);
    }

    /// <summary>
    /// The settlement register is entirely money, so it must sit behind the salary grant. A
    /// department head holding <c>hr.reports.view</c> alone would otherwise read what every
    /// departing colleague was paid.
    /// </summary>
    [Fact]
    public void The_settlement_register_needs_the_salary_permission()
    {
        var report = ReportCatalog.Find("settlement-register");

        Assert.NotNull(report);
        Assert.Equal("hr.reports.salary", report!.Descriptor.RequiredPermission);
    }
}
