using Hms.Kernel.Auth;

namespace Hms.Web;

/// <summary>
/// Composition-root nav registry (05 §2). Items appear only when the user holds the permission
/// AND the module is entitled (NavComposer). Registry order is sidebar order.
///
/// Scope: the §9A.2 MVP modules, plus the Phase-2 modules the PM has since released
/// (`docs/architect_review_prompt.md` scope change; sequenced by `11-build-plan-phase2.md`).
/// Pharmacy landed with spec 0016. Adding a module remains a PM decision (hard rule 2).
/// </summary>
public static class ModuleNav
{
    public static readonly IReadOnlyList<NavItem> Registry =
    [
        new("Dashboard", "MD Dashboard", "/dashboard", "dashboard.read", "monitoring", "Overview"),

        new("Registration", "New Patient", "/registration/new", "registration.create", "person_add", "Front Desk"),
        new("Registration", "Patient Directory", "/registration", "registration.read", "group", "Front Desk"),
        new("Ipd", "Help Desk", "/frontdesk", "ipd.read", "search", "Front Desk"),

        new("Appointments", "Serials / Queue", "/appointments", "appointments.read", "calendar_month", "Appointments"),

        new("Billing", "OPD Invoice", "/billing/opd", "billing.invoice.create", "point_of_sale", "Billing & Cash"),
        new("Billing", "Due Collection", "/billing/dues", "billing.receipt.create", "payments", "Billing & Cash"),
        new("Billing", "Refund & Cancel", "/billing/refund", "billing.receipt.create", "currency_exchange", "Billing & Cash"),
        new("Billing", "Counter Session", "/billing/session", "billing.session.open", "lock", "Billing & Cash"),
        new("Billing", "Counter Day-Close", "/billing/day-close", "billing.session.close", "savings", "Billing & Cash"),
        new("Billing", "Collection Reports", "/billing/reports", "billing.session.close", "bar_chart", "Billing & Cash"),

        new("Diagnostics", "Diagnostic Order", "/diagnostics/order", "diagnostics.order.create", "receipt_long", "Diagnostics"),
        new("Diagnostics", "Report Delivery", "/diagnostics/delivery", "diagnostics.order.create", "send", "Diagnostics"),

        new("Lis", "Work Board", "/lis/board", "lis.worklist.read", "biotech", "Laboratory"),
        new("Lis", "Result Entry", "/lis/results", "lis.result.enter", "edit_note", "Laboratory"),
        new("Lis", "Verification Queue", "/lis/verify", "lis.result.verify", "verified", "Laboratory"),
        new("Lis", "Amend a Report", "/lis/amend", "lis.result.verify", "sync_alt", "Laboratory"),

        new("Ipd", "Ward Board", "/ipd/board", "ipd.read", "monitoring", "Indoor (IPD)"),
        new("Ipd", "New Admission", "/ipd/admit", "ipd.manage", "person_add", "Indoor (IPD)"),
        new("Ipd", "Admissions & Census", "/ipd/admissions", "ipd.read", "group", "Indoor (IPD)"),
        new("Ipd", "Ward Indents", "/ipd/indents", "ipd.service.post", "receipt_long", "Indoor (IPD)"),
        new("Ipd", "Certificates", "/ipd/certificates", "ipd.settle", "verified", "Indoor (IPD)"),
        new("Ipd", "IPD Reports", "/ipd/reports", "ipd.read", "bar_chart", "Indoor (IPD)"),
        // R5 nursing console (spec 0041). These sit under Indoor rather than a "Nursing" group of
        // their own because NavComposer groups by module and RoutePrefixes lets one module own
        // /ipd — a separate group would mean a separate module and a separate entitlement.
        new("Ipd", "Nursing Station", "/ipd/station", "ipd.read", "monitoring", "Indoor (IPD)"),
        new("Ipd", "Ward Duty", "/ipd/duty", "ipd.duty.manage", "group", "Indoor (IPD)"),

        new("Emr", "Consultation Queue", "/emr/queue", "emr.read", "stethoscope", "Clinical"),
        new("Emr", "Pre-checkup Vitals", "/emr/vitals", "emr.vitals.record", "monitoring", "Clinical"),
        new("Emr", "Patient Record", "/emr/history", "emr.read", "history", "Clinical"),
        new("Emr", "My Templates", "/emr/templates", "emr.note.write", "edit_note", "Clinical"),
        new("Emr", "Indoor Prescription", "/emr/indoor", "emr.note.write", "edit_note", "Clinical"),
        new("Emr", "Nursing Charts", "/emr/charts", "emr.chart.record", "task_alt", "Clinical"),
        new("Emr", "Care Tasks", "/emr/tasks", "emr.task.manage", "task_alt", "Clinical"),

        new("Radiology", "Modality Worklist", "/radiology/worklist", "radiology.worklist.read", "science", "Radiology"),
        new("Radiology", "Machines & Mapping", "/radiology/modalities", "radiology.study.perform", "sync_alt", "Radiology"),

        new("Ot", "OT Board", "/ot/board", "ot.read", "monitoring", "Operation Theatre"),
        new("Ot", "Schedule an Operation", "/ot/schedule", "ot.schedule", "calendar_add_on", "Operation Theatre"),
        new("Ot", "Operation Register", "/ot/register", "ot.read", "history", "Operation Theatre"),
        new("Ot", "Theatres", "/ot/theatres", "ot.schedule", "science", "Operation Theatre"),

        new("Pharmacy", "Pharmacy Sale", "/pharmacy/pos", "pharmacy.sale.create", "point_of_sale", "Pharmacy"),
        new("Pharmacy", "Stock & Expiry", "/pharmacy/stock", "pharmacy.stock.manage", "inventory_2", "Pharmacy"),
        new("Pharmacy", "Purchase Orders", "/pharmacy/purchase", "pharmacy.purchase.manage", "receipt_long", "Pharmacy"),
        new("Pharmacy", "Products & Companies", "/pharmacy/products", "pharmacy.purchase.manage", "science", "Pharmacy"),
        new("Pharmacy", "Indoor Issue Queue", "/pharmacy/indents", "pharmacy.stock.manage", "outbox", "Pharmacy"),
        new("Pharmacy", "Outlet Transfers", "/pharmacy/transfers", "pharmacy.stock.manage", "sync_alt", "Pharmacy"),
        new("Pharmacy", "Suppliers & Ledger", "/pharmacy/suppliers", "pharmacy.purchase.manage", "payments", "Pharmacy"),
        new("Pharmacy", "Pharmacy Reports", "/pharmacy/reports", "pharmacy.read", "bar_chart", "Pharmacy"),
        new("Pharmacy", "Pharmacy Dashboard", "/pharmacy/dashboard", "pharmacy.read", "monitoring", "Pharmacy"),

        new("Notifications", "SMS Tray", "/notifications/tray", "notifications.read", "sms", "Notifications"),

        new("Admin", "Approvals Inbox", "/admin/approvals", "admin.approvals.decide", "fact_check", "Administration"),
        new("Admin", "Users & Roles", "/admin/users", "admin.users.manage", "manage_accounts", "Administration"),
        new("Admin", "Price List & Catalog", "/admin/masters", "admin.masters.manage", "inventory_2", "Administration"),
        new("Admin", "Bulk Import", "/admin/import", "admin.masters.manage", "download", "Administration"),
        new("Admin", "Doctors & Referrers", "/admin/people", "admin.masters.manage", "group", "Administration"),
        new("Admin", "Report Templates", "/admin/templates", "admin.masters.manage", "science", "Administration"),
        new("Admin", "SMS Templates", "/admin/sms", "admin.masters.manage", "mail", "Administration"),
        new("Admin", "Audit Viewer", "/admin/audit", "admin.audit.read", "history", "Administration"),
    ];

    /// <summary>
    /// What the sidebar actually renders. HR's screens live in a razor class library so the same
    /// build serves the standalone HRM SKU (ADR-0025), and a library cannot reference this host —
    /// so its entries are contributed rather than listed above. Further such modules append here.
    /// </summary>
    public static readonly IReadOnlyList<NavItem> Composed =
        [.. Registry, .. Hms.Hr.Screens.HrNav.Registry];

    /// <summary>
    /// Route prefix → module, for ADR-0026 choke point 2. Derived from the nav registry rather than
    /// hand-listed, because a hand-listed map is exactly the thing that goes stale: adding a screen
    /// would otherwise leave its module unenforced and nobody would notice until a customer found
    /// they could reach what they had not licensed.
    /// <para>
    /// The first path segment is the unit of enforcement, so <c>/billing/invoice/7</c> is covered by
    /// the same rule as <c>/billing/opd</c> even though only the latter is in the menu. Routes with
    /// no module — <c>/login</c>, <c>/denied</c>, <c>/health</c>, the public displays — are not
    /// gated, which is correct: they belong to the product, not to a module.
    /// </para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RoutePrefixes = BuildPrefixes();

    private static Dictionary<string, string> BuildPrefixes()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in Composed)
        {
            var segments = item.Url.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) continue;              // "/" belongs to no module
            var prefix = "/" + segments[0];

            if (map.TryGetValue(prefix, out var owner) && owner != item.Module)
                throw new InvalidOperationException(
                    $"Route prefix {prefix} is claimed by both {owner} and {item.Module}. "
                    + "Entitlement is enforced per prefix, so two modules cannot share one.");

            map[prefix] = item.Module;
        }

        return map;
    }
}
