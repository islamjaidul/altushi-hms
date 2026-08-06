namespace Hms.Hr.Screens;

/// <summary>
/// HR permission policies. These live with the module's screens rather than in the ERP host's
/// <c>Perm.cs</c>, because a razor class library cannot reference the host that consumes it
/// (ADR-0025). Each host composes the registries it ships.
/// <para>
/// The trap this codebase already documents applies here too: <c>[Authorize(Policy = …)]</c> takes
/// the <b>prefixed</b> constant below, while <c>Model.Can("…")</c> takes the <b>bare</b> claim from
/// <see cref="Claim"/>. Passing a prefixed constant to <c>Can</c> compiles and is silently always
/// false, and <c>ViewGuardPermissionTests</c> fails the build for it.
/// </para>
/// <para>
/// Both forms are written out literally, in the same shape as <c>Perm.cs</c>, so
/// <c>eng/check-lifecycle-traceability.sh</c> can parse them. <c>HrPermConsistencyTests</c> proves
/// the two lists cannot drift apart.
/// </para>
/// </summary>
public static class HrPerm
{
    private const string P = "perm:";

    /// <summary>The directory, the roster, attendance. Reveals no pay.</summary>
    public const string Read = P + "hr.read";

    /// <summary>
    /// Salary figures, pay structures, payslips, ledgers. Deliberately separate from
    /// <see cref="Read"/>: a department head approves leave without seeing what anyone earns.
    /// </summary>
    public const string SalaryRead = P + "hr.salary.read";

    public const string EmployeeManage = P + "hr.employee.manage";
    public const string AttendanceReview = P + "hr.attendance.review";
    public const string RosterManage = P + "hr.roster.manage";
    public const string LeaveApply = P + "hr.leave.apply";
    public const string LeaveRecommend = P + "hr.leave.recommend";
    public const string LeaveApprove = P + "hr.leave.approve";
    public const string PayrollRun = P + "hr.payroll.run";
    public const string PayrollApprove = P + "hr.payroll.approve";
    public const string PolicyManage = P + "hr.policy.manage";

    // ---- spec 0055 (module PRD §11) ------------------------------------------------------------
    // Rule D6 governs all of them: no permission other than salary-read may reveal a pay figure, on
    // any surface — screen, report, export, print, notification, log or timeline.

    /// <summary>The report centre and every report that shows no pay.</summary>
    public const string ReportsView = P + "hr.reports.view";

    /// <summary>
    /// Salary-bearing reports and their exports. Deliberately distinct from
    /// <see cref="SalaryRead"/>: a manager may be trusted with one employee's payslip and not with a
    /// branch-wide cost register, and there is no export back door (§11 scoping rule 4).
    /// </summary>
    public const string ReportsSalary = P + "hr.reports.salary";

    /// <summary>Manager self-service — always scoped to the holder's own reporting line.</summary>
    public const string TeamView = P + "hr.team.view";

    /// <summary>The activity log. Read-only by construction: nothing in it is editable by anyone.</summary>
    public const string AuditView = P + "hr.audit.view";

    /// <summary>
    /// The employee's own space. Every employee sees their own record without any HR permission;
    /// nobody sees another's by default (§11 scoping rule 2).
    /// </summary>
    public const string Self = P + "hr.self";

    // ---- spec 0056 (module PRD §11) ------------------------------------------------------------
    // Both names are §11's own. Inventing a permission would be inventing scope (hard rule 2).

    /// <summary>
    /// Separation, clearance and final settlement. Deliberately not <see cref="EmployeeManage"/>:
    /// ending someone's employment and computing what they are owed is a different trust from
    /// correcting their phone number.
    /// </summary>
    public const string SettlementManage = P + "hr.settlement.manage";

    /// <summary>
    /// Generating letters and certificates. A salary certificate additionally needs
    /// <see cref="SalaryRead"/> — the clerk who may issue an experience certificate is not
    /// necessarily the person who tells a landlord what a nurse earns (D6).
    /// </summary>
    public const string DocumentIssue = P + "hr.document.issue";

    // ---- spec 0057 (module PRD §11) --------------------------------------------------------------

    /// <summary>Loan and advance lifecycle: request, approve, disburse, foreclose, write off.</summary>
    public const string LoanManage = P + "hr.loan.manage";

    /// <summary>Bonus sheets, increment runs and promotions — compensation changed in bulk.</summary>
    public const string CompensationManage = P + "hr.compensation.manage";

    // §11 also names hr.ledger.manage, for "PF, welfare and tax ledger operations and
    // withdrawals". Spec 0057 delivers the member STATEMENTS — which are reports, gated by
    // hr.reports.salary like every other salary-bearing register — and not the withdrawal and
    // adjustment operations. The permission arrives with the capability it guards; declaring it
    // now would be a grant that protects nothing, and check-lifecycle-traceability.sh says so.

    /// <summary>The bare claim strings — what nav entries and <c>Can(...)</c> take.</summary>
    public static class Claim
    {
        public const string Read = "hr.read";
        public const string SalaryRead = "hr.salary.read";
        public const string EmployeeManage = "hr.employee.manage";
        public const string AttendanceReview = "hr.attendance.review";
        public const string RosterManage = "hr.roster.manage";
        public const string LeaveApply = "hr.leave.apply";
        public const string LeaveRecommend = "hr.leave.recommend";
        public const string LeaveApprove = "hr.leave.approve";
        public const string PayrollRun = "hr.payroll.run";
        public const string PayrollApprove = "hr.payroll.approve";
        public const string PolicyManage = "hr.policy.manage";
        public const string ReportsView = "hr.reports.view";
        public const string ReportsSalary = "hr.reports.salary";
        public const string TeamView = "hr.team.view";
        public const string AuditView = "hr.audit.view";
        public const string Self = "hr.self";
        public const string SettlementManage = "hr.settlement.manage";
        public const string DocumentIssue = "hr.document.issue";
        public const string LoanManage = "hr.loan.manage";
        public const string CompensationManage = "hr.compensation.manage";

        public static readonly string[] All =
        [
            Read, SalaryRead, EmployeeManage, AttendanceReview, RosterManage,
            LeaveApply, LeaveRecommend, LeaveApprove, PayrollRun, PayrollApprove, PolicyManage,
            ReportsView, ReportsSalary, TeamView, AuditView, Self,
            SettlementManage, DocumentIssue,
            LoanManage, CompensationManage,
        ];
    }
}
