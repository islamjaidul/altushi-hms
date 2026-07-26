using Hms.Kernel.Auth;

namespace Hms.Web;

/// <summary>
/// Composition-root nav registry (05 §2). Items appear only when the user holds the permission
/// AND the module is entitled (NavComposer). Registry order is sidebar order.
///
/// Scope: the §9A.2 MVP modules only. The design reference carries pharmacy, IPD, OT, stores,
/// blood bank, HR and accounts groups — PRD §9A.3 excludes all of them, so they get no entry
/// here. Adding one is a PM decision (hard rule 2), never a silent edit.
/// </summary>
public static class ModuleNav
{
    public static readonly IReadOnlyList<NavItem> Registry =
    [
        new("Dashboard", "MD Dashboard", "/dashboard", "dashboard.read", "monitoring", "Overview"),

        new("Registration", "New Patient", "/registration/new", "registration.create", "person_add", "Front Desk"),
        new("Registration", "Patient Directory", "/registration", "registration.read", "group", "Front Desk"),

        new("Appointments", "Serials / Queue", "/appointments", "appointments.read", "calendar_month", "Appointments"),

        new("Billing", "OPD Invoice", "/billing/opd", "billing.invoice.create", "point_of_sale", "Billing & Cash"),
        new("Billing", "Due Collection", "/billing/dues", "billing.receipt.create", "payments", "Billing & Cash"),
        new("Billing", "Counter Session", "/billing/session", "billing.session.open", "lock", "Billing & Cash"),
        new("Billing", "Counter Day-Close", "/billing/day-close", "billing.session.close", "savings", "Billing & Cash"),

        new("Diagnostics", "Diagnostic Order", "/diagnostics/order", "diagnostics.order.create", "receipt_long", "Diagnostics"),
        new("Diagnostics", "Report Delivery", "/diagnostics/delivery", "diagnostics.order.create", "send", "Diagnostics"),

        new("Lis", "Work Board", "/lis/board", "lis.worklist.read", "biotech", "Laboratory"),
        new("Lis", "Result Entry", "/lis/results", "lis.result.enter", "edit_note", "Laboratory"),
        new("Lis", "Verification Queue", "/lis/verify", "lis.result.verify", "verified", "Laboratory"),

        new("Notifications", "SMS Tray", "/notifications/tray", "notifications.read", "sms", "Notifications"),

        new("Admin", "Approvals Inbox", "/admin/approvals", "admin.approvals.decide", "fact_check", "Administration"),
        new("Admin", "Users & Roles", "/admin/users", "admin.users.manage", "manage_accounts", "Administration"),
        new("Admin", "Price List & Catalog", "/admin/masters", "admin.masters.manage", "inventory_2", "Administration"),
        new("Admin", "Bulk Import", "/admin/import", "admin.masters.manage", "download", "Administration"),
        new("Admin", "Audit Viewer", "/admin/audit", "admin.audit.read", "history", "Administration"),
    ];
}
