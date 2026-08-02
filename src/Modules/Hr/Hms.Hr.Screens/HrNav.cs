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
/// </summary>
public static class HrNav
{
    public const string Group = "HR & Payroll";

    public static readonly IReadOnlyList<NavItem> Registry =
    [
        new(HrModule.Name, "HR Dashboard", "/hr", HrPerm.Claim.Read, "groups", Group),
        new(HrModule.Name, "Employees", "/hr/employees", HrPerm.Claim.Read, "badge", Group),
        new(HrModule.Name, "Attendance", "/hr/attendance", HrPerm.Claim.AttendanceReview, "fact_check", Group),
        new(HrModule.Name, "Roster", "/hr/roster", HrPerm.Claim.RosterManage, "calendar_month", Group),
        new(HrModule.Name, "Leave", "/hr/leave", HrPerm.Claim.Read, "event_busy", Group),
        new(HrModule.Name, "Payroll", "/hr/payroll", HrPerm.Claim.PayrollRun, "payments", Group),
        new(HrModule.Name, "Org structure", "/hr/masters", HrPerm.Claim.PolicyManage, "factory", Group),
        new(HrModule.Name, "Policies", "/hr/policies", HrPerm.Claim.PolicyManage, "rule", Group),
        new(HrModule.Name, "My Leave", "/hr/me", HrPerm.Claim.LeaveApply, "person", Group),
    ];
}
