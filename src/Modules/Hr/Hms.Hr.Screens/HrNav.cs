using Hms.Kernel.Auth;

namespace Hms.Hr.Screens;

/// <summary>
/// The module's sidebar entries. Hosts concatenate this with their own registry — the ERP appends it
/// to <c>ModuleNav.Registry</c>, the HRM SKU ships it alone (ADR-0025).
/// <para>
/// Nav takes the <b>bare</b> claim; <c>[Authorize]</c> takes the prefixed policy. The menu is only a
/// convenience — server-side endpoint policies remain the actual control (G10), now joined by the
/// module entitlement check (ADR-0026).
/// </para>
/// <para>
/// <b>Grouped by the job an operator has</b> (module PRD §12.1, spec 0055). Eleven flat entries
/// already crowded one heading and the capability in phases 2–5 would make it thirty. One level
/// only — that is all <see cref="NavGroup"/> supports, and it is also the PRD's own cap.
/// A group whose entries are all unpermitted does not render at all: <c>NavComposer</c> filters
/// before it groups, so an empty heading is impossible by construction.
/// </para>
/// <para>
/// <b>Every route stays under <c>/hr</c>.</b> <c>ModuleNav.BuildPrefixes()</c> derives entitlement
/// route prefixes from each entry's first URL segment and throws at static initialisation on a
/// collision — a stray prefix is a boot failure in both hosts, not a test failure.
/// </para>
/// </summary>
public static class HrNav
{
    public const string Dashboard = "Dashboard";
    public const string People = "People";
    public const string Time = "Time";
    public const string Leave = "Leave";
    public const string Payroll = "Payroll";
    public const string Reports = "Reports";
    public const string Setup = "Setup";
    public const string MySpace = "My space";
    public const string Governance = "Governance";

    /// <summary>Kept for the tests and hosts that still refer to the module's old single heading.</summary>
    public const string Group = Dashboard;

    public static readonly IReadOnlyList<NavItem> Registry =
    [
        new(HrModule.Name, "HR dashboard", "/hr", HrPerm.Claim.Read, "monitoring", Dashboard),

        new(HrModule.Name, "Employees", "/hr/employees", HrPerm.Claim.Read, "badge", People),
        new(HrModule.Name, "Org structure", "/hr/masters", HrPerm.Claim.PolicyManage, "factory", People),

        new(HrModule.Name, "Attendance review", "/hr/attendance", HrPerm.Claim.AttendanceReview, "fact_check", Time),
        new(HrModule.Name, "Roster", "/hr/roster", HrPerm.Claim.RosterManage, "calendar_month", Time),

        new(HrModule.Name, "Leave desk", "/hr/leave", HrPerm.Claim.Read, "event_busy", Leave),

        new(HrModule.Name, "Payroll runs", "/hr/payroll", HrPerm.Claim.PayrollRun, "payments", Payroll),
        // The approver's door (AUD-AUZ-01): §12's Accounts Manager / MD hold approve and NOT run,
        // so maker-checker needs a surface they can reach without the ability to generate.
        new(HrModule.Name, "Payroll approvals", "/hr/payroll/approvals", HrPerm.Claim.PayrollApprove, "task_alt", Payroll),
        new(HrModule.Name, "Payslips", "/hr/payslips", HrPerm.Claim.SalaryRead, "receipt_long", Payroll),

        new(HrModule.Name, "Reports", "/hr/reports", HrPerm.Claim.ReportsView, "bar_chart", Reports),

        new(HrModule.Name, "Policies", "/hr/policies", HrPerm.Claim.PolicyManage, "rule", Setup),

        // Always present for anyone with a linked employment — for most employees it is the only
        // group they will ever see, and permission filtering therefore makes it their first.
        new(HrModule.Name, "My space", "/hr/me", HrPerm.Claim.Self, "person", MySpace),
        new(HrModule.Name, "My team", "/hr/team", HrPerm.Claim.TeamView, "groups", MySpace),

        // The log is a register, not a screen of its own (D3: reporting is a capability). It renders
        // through the same grammar as every other report and requires hr.audit.view to open.
        new(HrModule.Name, "Activity log", "/hr/reports/activity-log", HrPerm.Claim.AuditView, "history", Governance),
    ];
}
