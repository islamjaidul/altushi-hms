using Hms.Shell.Reporting;

namespace Hms.Hr.Screens.Reports;

/// <summary>
/// Every report the module ships. One list, so the report centre, the permission rule, the route and
/// the architecture tests all read the same source.
/// <para>
/// <b>Keys are permanent.</b> A key is in a URL and, once saved views exist, in an operator's saved
/// configuration — renaming one breaks every link an HR officer ever sent. An architecture test pins
/// them to kebab-case and to uniqueness.
/// </para>
/// <para>
/// Reports whose data has no writer yet are deliberately absent rather than shipped empty: loans,
/// PF, welfare and tax ledgers arrive with the screens that capture them (spec 0057), and bonus,
/// increment and settlement registers with theirs. A permanently blank report is worse than a
/// missing one, because it reads as "nothing happened" rather than "not built".
/// </para>
/// </summary>
public static class ReportCatalog
{
    private static readonly IHrReport[] Reports =
    [
        // People
        new EmployeeDirectoryReport(),
        new HeadcountSummaryReport(),
        new HeadcountMovementReport(),
        new NewJoinersReport(),
        new SeparationsReport(),
        new ServiceLengthReport(),
        // spec 0056 — the lifecycle registers. §12.2 lists some as screens; ADR-0029 makes every
        // read a report, and each row drills to the record where the decision actually lives.
        new ProbationDueReport(),
        new ContractExpiryReport(),
        new DocumentExpiryReport(),
        new LicenceRegisterReport(),
        new NomineeRegisterReport(),
        new SeparationClearanceReport(),
        new AnniversariesReport(),

        // Time & attendance
        new AttendanceRegisterReport(),
        new DailyAttendanceSummaryReport(),
        new LateRegisterReport(),
        new AbsenceRegisterReport(),
        new OvertimeRegisterReport(),
        new AttendanceExceptionsReport(),
        new CorrectionLogReport(),
        new ImportHealthReport(),

        // Leave
        new LeaveRegisterReport(),
        new LeaveBalanceReport(),
        new LeaveAvailedAnalysisReport(),
        new PendingLeaveAgeingReport(),
        new LeaveWithoutPayReport(),

        // Payroll & compensation — all salary-bearing
        new SalarySheetReport(),
        new SalarySummaryByUnitReport(),
        new ComponentRegisterReport(),
        new EmployerCostReport(),
        new PayrollRunAuditReport(),
        new SettlementRegisterReport(),

        // Governance
        new ActivityLogReport(),
        new EmployeeAuditReport(),
        new LettersIssuedReport(),
    ];

    public static IReadOnlyList<IHrReport> All => Reports;

    public static IHrReport? Find(string? key) => string.IsNullOrWhiteSpace(key)
        ? null
        : Reports.FirstOrDefault(r =>
            string.Equals(r.Descriptor.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The catalogue grouped in §13's category order, for the report centre.</summary>
    public static IReadOnlyList<(string Category, IReadOnlyList<ReportDescriptor> Reports)> ByCategory()
        => [.. ReportCategory.All
            .Select(c => (Category: c,
                Reports: (IReadOnlyList<ReportDescriptor>)[.. Reports
                    .Select(r => r.Descriptor)
                    .Where(d => d.Category == c)
                    .OrderBy(d => d.Name)]))
            .Where(g => g.Reports.Count > 0)];
}
