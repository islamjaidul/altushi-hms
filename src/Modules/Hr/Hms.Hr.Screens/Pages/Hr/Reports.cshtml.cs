using Hms.Hr.Screens.Reports;
using Hms.Kernel.Audit;
using Hms.Kernel.Time;
using Hms.Shell;
using Hms.Shell.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Hms.Hr.Screens.Pages.Hr;

/// <summary>
/// The report centre, and every report in it (module PRD §12.4, §13).
/// <para>
/// <b>One page for sixty reports.</b> The grammar — header, chips, period, sorting, empty state, row
/// count, truncation, permission, export, print, drill-through and the salary-access audit — lives
/// here, once. A report is a class that returns a <c>ReportTable</c> and can bypass none of it. That
/// is what makes D3's "learn one report and you have learned all of them" a property of the build,
/// and it is also why the route table needs a single row rather than sixty hand-synced ones.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.ReportsView)]
public class ReportsModel(
    IHrTx tx,
    IPeriodCalendarSource calendars,
    AuditWriter audit,
    TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Key { get; set; }

    public IHrReport? Report { get; private set; }

    public Period Period { get; private set; } = null!;

    public PeriodBar Bar { get; private set; } = null!;

    public ReportView View { get; private set; } = null!;

    public ReportHead Head { get; private set; } = null!;

    public IReadOnlyList<FilterChip> Chips { get; private set; } = [];

    public IReadOnlyList<(string Category, IReadOnlyList<ReportListing> Reports)> Centre { get; private set; } = [];

    /// <summary>Set when the reader may not open this report. §12.4.8: listed with the reason,
    /// never a silently truncated version.</summary>
    public string? UnavailableReason { get; private set; }

    public string CsvHref => Href("Csv");

    public string XlsxHref => Href("Xlsx");

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        var today = Dhaka.Today(clock);
        // Read before opening the transaction: the calendar source takes its own scope.
        var calendar = await calendars.ForAsync(BranchId, today, ct);

        // The one redirect. A request naming no period is sent to the canonical URL for the
        // remembered one, so the address bar always states a concrete period and a link means the
        // same thing for whoever opens it. It cannot loop: the target always carries `p=`.
        if (PeriodBinding.ShouldRestore(Request, out var remembered))
        {
            var restored = PeriodResolver.Resolve(remembered, today, calendar,
                ReportCatalog.Find(Key)?.Descriptor.DefaultGrain ?? PeriodGrain.Month);
            return Redirect(PeriodBinding.CanonicalUrl(Request, restored));
        }

        Report = ReportCatalog.Find(Key);
        Period = PeriodBinding.Resolve(Request, today, calendar,
            Report?.Descriptor.DefaultGrain ?? PeriodGrain.Month);
        Bar = PeriodBar.For(Period, Request, calendar, today);
        View = ReportView.For(ReportTable.Empty("Choose a report."), Request);

        if (Key is { Length: > 0 } && Report is null)
        {
            Fail("There is no report by that name. Choose one from the list.");
            Key = null;
        }

        if (Report is null)
        {
            Centre = BuildCentre();
            PeriodBinding.Remember(Response, Period);
            return Page();
        }

        // Permission, once, for every report. String literals on purpose: the traceability guard
        // greps Can("…") and a constant — even the correct bare one — is invisible to it.
        var permitted = Report.Descriptor.RequiredPermission switch
        {
            "hr.reports.salary" => Can("hr.reports.salary"),
            "hr.audit.view" => Can("hr.audit.view"),
            _ => Can("hr.reports.view"),
        };

        if (!permitted)
        {
            UnavailableReason = Report.Descriptor.RequiredPermission == "hr.audit.view"
                ? "This is a governance register. Opening it needs the “HR activity log” "
                  + "permission, which your role does not have. An administrator can grant it on "
                  + "Users & roles."
                : "This report shows salary figures. Opening it needs the “HR salary reports” "
                  + "permission, which your role does not have. An administrator can grant it on "
                  + "Users & roles.";
            Head = HeadFor(Report, 0);
            PeriodBinding.Remember(Response, Period);
            return Page();   // Listed, explained, and not one query run against payroll.
        }

        var table = ReportTable.Empty("");
        var notice = PeriodWords.Notice(Period, today, calendar.MaxRangeDays);

        if (Period.Health == PeriodHealth.WhollyFuture)
        {
            // §12.3: an empty result with a sentence, never an error — and no query at all.
            table = ReportTable.Empty(notice!);
        }
        else if (Period.Health == PeriodHealth.TooLong && Request.Query["run"] != "1")
        {
            // Warn before running rather than time out. "Run it anyway" is on the period bar.
            table = ReportTable.Empty(notice!);
        }
        else
        {
            var raw = FilterSet.FromRequest(Request);
            var periodPairs = PeriodBinding.PeriodPairs(Period).ToList();

            await tx.RunAsync(async s =>
            {
                var filters = HrReportFilters.From(raw);
                Chips = await filters.DescribeAsync(s, BranchId, raw, Request.Path, periodPairs, ct);

                var context = new ReportContext
                {
                    BranchId = BranchId,
                    Period = Period,
                    Filters = filters,
                    Raw = raw,
                    Scope = s,
                    PeriodPairs = periodPairs,
                    Sort = Request.Query["sort"],
                    SortDesc = Request.Query["dir"] == "desc",
                    CanSeeSalary = Can("hr.salary.read"),
                };

                table = (await Report.BuildAsync(context, ct)).Validated();

                // N-HR8: access to salary data is itself logged. Reading a branch-wide cost register
                // is an event worth being able to ask about later.
                if (Report.Descriptor.SalaryBearing)
                {
                    audit.Append(s.Kernel, BranchId, ActorId, ActorName,
                        "hr.report.salary.read", "hr.report", 0,
                        after: new { Report.Descriptor.Key, Period = PeriodWords.Describe(Period) },
                        tier: 2);
                }
            }, ct);
        }

        View = ReportView.For(table, Request);
        Head = HeadFor(Report, table.Rows.Count);
        PeriodBinding.Remember(Response, Period);
        return Page();
    }

    public Task<IActionResult> OnGetCsvAsync(CancellationToken ct) => ExportAsync(csv: true, ct);

    public Task<IActionResult> OnGetXlsxAsync(CancellationToken ct) => ExportAsync(csv: false, ct);

    /// <summary>
    /// §12.4.6 — the export is what the screen showed. It runs the identical pipeline and serialises
    /// the identical <c>ReportTable</c> and chips; the writers can see nothing else, so there is no
    /// path by which a file could contain a row the screen did not.
    /// </summary>
    private async Task<IActionResult> ExportAsync(bool csv, CancellationToken ct)
    {
        var page = await OnGetAsync(ct);
        if (page is not PageResult || Report is null || UnavailableReason is not null) return page;

        var header = new ExportHeader(
            Report.Descriptor.Name,
            PeriodWords.Describe(Period),
            Chips,
            ActorName,
            Ui.DateTimeShort(clock.GetUtcNow()));

        return csv
            ? Download.File(ReportCsv.Write(header, View.Table), Download.CsvMime,
                Download.Name(Report.Descriptor.Key, Period, ".csv"))
            : Download.File(ReportXlsx.Write(header, View.Table), ReportXlsx.Mime,
                Download.Name(Report.Descriptor.Key, Period, ".xlsx"));
    }

    private ReportHead HeadFor(IHrReport report, int rowCount) => new()
    {
        Name = report.Descriptor.Name,
        Question = report.Descriptor.Question,
        PeriodWords = PeriodWords.Describe(Period),
        RunBy = ActorName,
        RunAt = Ui.DateTimeShort(clock.GetUtcNow()),
        RowCount = rowCount,
    };

    /// <summary>
    /// The catalogue. A report the reader cannot open is still listed, with the reason — §12.4.8 is
    /// explicit that a user without the grant must not be shown a silently truncated version, and
    /// hiding the row entirely would leave them unable to ask for access to something they cannot
    /// see exists.
    /// </summary>
    private IReadOnlyList<(string, IReadOnlyList<ReportListing>)> BuildCentre()
    {
        var canSalary = Can("hr.reports.salary");
        var canAudit = Can("hr.audit.view");
        var pairs = PeriodBinding.PeriodPairs(Period)
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}");
        var query = "?" + string.Join('&', pairs);

        return [.. ReportCatalog.ByCategory().Select(group => (
            group.Category,
            (IReadOnlyList<ReportListing>)[.. group.Reports.Select(d => new ReportListing(
                d,
                Permitted: Allowed(d),
                Href: $"/hr/reports/{d.Key}{query}",
                UnavailableReason: Allowed(d) ? null : Needs(d)))]))];

        bool Allowed(ReportDescriptor d) => d.RequiredPermission switch
        {
            "hr.reports.salary" => canSalary,
            "hr.audit.view" => canAudit,
            _ => true,   // the page policy already required hr.reports.view
        };

        static string Needs(ReportDescriptor d) => d.RequiredPermission == "hr.audit.view"
            ? "Needs the HR activity log permission"
            : "Needs the HR salary reports permission";
    }

    private string Href(string handler)
    {
        var kept = Request.Query
            .Where(kv => !string.Equals(kv.Key, "handler", StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value, (kv, v) =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(v ?? "")}")
            .ToList();
        kept.Add($"handler={handler}");
        return Request.Path + "?" + string.Join('&', kept);
    }
}
