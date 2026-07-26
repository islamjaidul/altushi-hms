namespace Hms.Web;

/// <summary>
/// Policy names as constants so every endpoint can carry `[Authorize(Policy = Perm.X)]`
/// (G10: deny by default, server-side). These are the same strings the nav registry uses —
/// the sidebar and the endpoint read one source, so the menu cannot drift from enforcement.
/// </summary>
public static class Perm
{
    private const string P = "perm:";

    public const string RegistrationCreate = P + "registration.create";
    public const string RegistrationRead = P + "registration.read";

    public const string AppointmentsRead = P + "appointments.read";
    public const string AppointmentsCreate = P + "appointments.create";

    public const string BillingInvoiceCreate = P + "billing.invoice.create";
    public const string BillingReceiptCreate = P + "billing.receipt.create";
    public const string BillingSessionOpen = P + "billing.session.open";
    public const string BillingSessionClose = P + "billing.session.close";

    public const string DiagnosticsOrderCreate = P + "diagnostics.order.create";

    public const string LisWorklistRead = P + "lis.worklist.read";
    public const string LisSampleCollect = P + "lis.sample.collect";
    public const string LisResultEnter = P + "lis.result.enter";
    public const string LisResultVerify = P + "lis.result.verify";

    public const string DashboardRead = P + "dashboard.read";

    public const string AdminApprovalsDecide = P + "admin.approvals.decide";
    public const string AdminUsersManage = P + "admin.users.manage";
    public const string AdminAuditRead = P + "admin.audit.read";
    public const string AdminMastersManage = P + "admin.masters.manage";

    public const string NotificationsRead = P + "notifications.read";
}
