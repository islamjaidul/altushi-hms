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

        public static readonly string[] All =
        [
            Read, SalaryRead, EmployeeManage, AttendanceReview, RosterManage,
            LeaveApply, LeaveRecommend, LeaveApprove, PayrollRun, PayrollApprove, PolicyManage,
        ];
    }
}
