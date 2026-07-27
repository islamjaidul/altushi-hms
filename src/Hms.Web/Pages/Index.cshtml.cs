using Hms.Kernel.Auth;
using Hms.Kernel.Entitlements;

namespace Hms.Web.Pages;

/// <summary>
/// §7 U1: the role home is 3–6 large labelled actions, not a menu tree. Everything an operator
/// does daily is one click from here (U11).
/// </summary>
public class IndexModel(IReadOnlyList<NavItem> registry, EntitlementProvider entitlements) : HmsPageModel
{
    public string DisplayName { get; private set; } = "";
    public IReadOnlyList<NavGroup> Groups { get; private set; } = [];
    public IReadOnlyList<NavItem> Actions { get; private set; } = [];

    /// <summary>One line of plain English per action — the trainer that never resigns (§7 U14).</summary>
    public static string Describe(string url) => url switch
    {
        "/registration/new" => "Register a walk-in patient and print the ID card.",
        "/registration" => "Find a patient by name, ID or phone.",
        "/appointments" => "Issue a serial and run today's doctor queue.",
        "/billing/opd" => "Bill consultations and services; take payment.",
        "/billing/dues" => "Collect outstanding balances against invoices.",
        "/billing/refund" => "Reverse a bill: cancel before payment, refund after.",
        "/billing/session" => "Open your counter for the day with its cash float.",
        "/billing/day-close" => "Count the drawer and close the counter.",
        "/billing/reports" => "Collection, department income, discounts and ageing dues.",
        "/diagnostics/order" => "Invoice tests, promise a delivery time, print labels.",
        "/diagnostics/delivery" => "Hand verified reports to patients.",
        "/lis/board" => "Move samples from collection through to delivery.",
        "/lis/results" => "Enter results with reference ranges and flags.",
        "/lis/verify" => "Verify and e-sign results for release.",
        "/lis/amend" => "Correct a released report — both versions are kept.",
        "/dashboard" => "Today's income, collection, due and discount.",
        "/admin/approvals" => "Decide discount, refund and reopen requests.",
        "/admin/users" => "Manage login accounts and their roles.",
        "/admin/masters" => "Test catalog, services and effective-dated prices.",
        "/admin/import" => "Load a price list or test catalog from a spreadsheet.",
        "/admin/people" => "Doctors, referrers and the consultants who sign reports.",
        "/admin/sms" => "The exact words patients receive, and which events send.",
        "/admin/audit" => "Who changed what, and when.",
        "/notifications/tray" => "Every SMS the system has sent or simulated.",
        _ => "",
    };

    public void OnGet()
    {
        DisplayName = ActorName;
        var perms = User.FindAll(PermissionPolicy.ClaimType).Select(c => c.Value).ToHashSet();
        Groups = NavComposer.Compose(registry, perms, entitlements.EnabledModules.ToHashSet());
        // U1 caps the home screen at six; the sidebar still carries the full (already narrow) set.
        Actions = Groups.SelectMany(g => g.Items).Take(6).ToList();
    }
}
