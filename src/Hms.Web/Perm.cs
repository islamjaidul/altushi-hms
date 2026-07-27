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

    public const string PharmacyRead = P + "pharmacy.read";
    public const string PharmacySaleCreate = P + "pharmacy.sale.create";
    public const string PharmacyPurchaseManage = P + "pharmacy.purchase.manage";
    public const string PharmacyStockManage = P + "pharmacy.stock.manage";

    public const string EmrRead = P + "emr.read";
    public const string EmrNoteWrite = P + "emr.note.write";
    public const string EmrVitalsRecord = P + "emr.vitals.record";
    public const string EmrChartRecord = P + "emr.chart.record";

    public const string RadiologyWorklistRead = P + "radiology.worklist.read";
    public const string RadiologyStudyPerform = P + "radiology.study.perform";
    public const string RadiologyReportWrite = P + "radiology.report.write";

    public const string OtRead = P + "ot.read";
    public const string OtSchedule = P + "ot.schedule";
    public const string OtRecord = P + "ot.record";

    public const string IpdRead = P + "ipd.read";
    public const string IpdManage = P + "ipd.manage";
    public const string IpdServicePost = P + "ipd.service.post";
    public const string IpdSettle = P + "ipd.settle";
}
