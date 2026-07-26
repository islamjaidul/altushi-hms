using Hms.Kernel.Auth;

namespace Hms.Web;

/// <summary>
/// Composition-root nav registry (05 §2). Items appear only when the user holds the permission
/// AND the module is entitled (NavComposer). Screens land sprint by sprint; the registry is
/// the single place a module's nav is declared.
/// </summary>
public static class ModuleNav
{
    public static readonly IReadOnlyList<NavItem> Registry =
    [
        new("Registration", "New Patient", "/registration/new", "registration.create"),
        new("Registration", "Patient Directory", "/registration", "registration.read"),
        new("Appointments", "Serials / Queue", "/appointments", "appointments.read"),
        new("Billing", "OPD Invoice", "/billing/opd", "billing.invoice.create"),
        new("Billing", "Due Collection", "/billing/dues", "billing.receipt.create"),
        new("Billing", "Counter Day-Close", "/billing/day-close", "billing.session.close"),
        new("Diagnostics", "Diagnostic Order", "/diagnostics/order", "diagnostics.order.create"),
        new("Lis", "Work Board", "/lis/board", "lis.worklist.read"),
        new("Lis", "Verification Queue", "/lis/verify", "lis.result.verify"),
        new("Dashboard", "MD Dashboard", "/dashboard", "dashboard.read"),
        new("Admin", "Approvals Inbox", "/admin/approvals", "admin.approvals.decide"),
        new("Admin", "Users & Roles", "/admin/users", "admin.users.manage"),
        new("Admin", "Audit Viewer", "/admin/audit", "admin.audit.read"),
        new("Notifications", "SMS Tray", "/notifications/tray", "notifications.read"),
    ];
}
