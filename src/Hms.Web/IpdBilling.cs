using Hms.Admin;
using Hms.Billing;
using Hms.Diagnostics;
using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Lis;
using Hms.Pharmacy;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

/// <summary>
/// The M6 money orchestration (spec 0017). Lives in the composition root like
/// <see cref="PharmacySale"/> because it spans modules (ipd × bill × adm × pharm × diag) and
/// modules never reference each other (ADR-0003). Every path first passes the folio gate
/// (<see cref="FolioService.EnsurePostableAsync"/> — row lock + R4/lock rules), then posts
/// money through the billing spine so folio truth and drawer truth commit together (G19).
/// </summary>
public static class IpdBilling
{
    /// <summary>R4 outdoor guard: an OPD counter refuses new charges for a blocked patient.</summary>
    public static async Task EnsureNotBlockedAsync(TxScope s, long patientId, CancellationToken ct = default)
    {
        var blocked = await s.Ipd.Admissions.AsNoTracking()
            .AnyAsync(a => a.PatientId == patientId && a.State == AdmissionState.Blocked, ct);
        if (blocked)
            throw new IpdException(
                "This patient is blocked for unpaid dues (R4) — clear or release before new charges.");
    }

    /// <summary>Where an admitted patient is lying right now, for the screens that are about to
    /// create an OUTDOOR charge for them (spec 0020 gap 3). Null when not admitted.</summary>
    public static async Task<(long AdmissionId, string AdmissionNo, string Bed)?> FindOpenAdmissionAsync(
        TxScope s, long patientId, CancellationToken ct = default)
    {
        var admission = await s.Ipd.Admissions.AsNoTracking()
            .Where(a => a.PatientId == patientId
                        && a.State != AdmissionState.Discharged
                        && a.State != AdmissionState.Death
                        && a.State != AdmissionState.Absconded
                        && a.State != AdmissionState.Reserved)
            .OrderByDescending(a => a.Id)
            .Select(a => new { a.Id, a.AdmissionNo })
            .FirstOrDefaultAsync(ct);
        if (admission is null) return null;

        var bedId = await s.Ipd.BedStays.AsNoTracking()
            .Where(x => x.AdmissionId == admission.Id && x.ToAt == null)
            .Select(x => (long?)x.BedId).FirstOrDefaultAsync(ct);
        var bed = bedId is long id
            ? await s.Ipd.Beds.AsNoTracking().Where(b => b.Id == id)
                .Select(b => b.Code).FirstOrDefaultAsync(ct) ?? "—"
            : "—";
        return (admission.Id, admission.AdmissionNo, bed);
    }

    public sealed record OutstandingInvoice(long InvoiceId, string InvoiceNo, long Balance, bool IsSettlement);

    /// <summary>
    /// Everything this patient still owes the hospital, settlement invoice first (spec 0020
    /// gap 2). Cross-context by construction: invoice ids come from bill.*, the settlement
    /// reference from ipd.folio, joined in memory (ADR-0003).
    /// </summary>
    public static async Task<IReadOnlyList<OutstandingInvoice>> OutstandingForPatientAsync(
        TxScope s, long patientId, long? settlementInvoiceId, CancellationToken ct = default)
    {
        var invoices = await s.Bill.Invoices.AsNoTracking()
            .Where(i => i.PatientId == patientId && i.State != Hms.Billing.Data.InvoiceState.Cancelled)
            .Select(i => new { i.Id, i.InvoiceNo })
            .ToListAsync(ct);
        if (invoices.Count == 0) return [];

        var ids = invoices.Select(i => i.Id).ToList();
        var dues = await s.Bill.Dues.AsNoTracking()
            .Where(d => ids.Contains(d.InvoiceId) && d.Balance > 0)
            .ToDictionaryAsync(d => d.InvoiceId, d => d.Balance, ct);

        return invoices
            .Where(i => dues.ContainsKey(i.Id))
            .Select(i => new OutstandingInvoice(i.Id, i.InvoiceNo, dues[i.Id], i.Id == settlementInvoiceId))
            .OrderByDescending(o => o.IsSettlement).ThenBy(o => o.InvoiceId)
            .ToList();
    }

    /// <summary>
    /// Posts every owed-but-unposted bed day at the day's effective rate (P18 rule). Runs on
    /// folio view, transfer and settlement; the UNIQUE(admission,date) index makes concurrent
    /// catch-ups collapse into one. Caller has locked the folio (or holds the settlement path).
    /// </summary>
    public static async Task<int> CatchUpBedDaysAsync(
        TxScope s, BillingService billing, FolioService folios, RateResolver rates,
        long branchId, long admissionId, long actorId, CancellationToken ct = default)
    {
        var folio = await s.Ipd.Folios.AsNoTracking()
            .SingleOrDefaultAsync(f => f.AdmissionId == admissionId, ct);
        if (folio is null) return 0;                          // reservations have no folio yet

        var unposted = await folios.ComputeUnpostedBedDaysAsync(s.Ipd, admissionId, ct);
        if (unposted.Dates.Count == 0) return 0;

        var bed = await s.Ipd.Beds.AsNoTracking().SingleAsync(b => b.Id == unposted.BedId, ct);
        var service = await s.Adm.Services.AsNoTracking()
            .SingleAsync(x => x.Id == unposted.TariffServiceId, ct);

        foreach (var date in unposted.Dates)
        {
            var rate = await rates.ResolveAsync(s.Adm, "service", unposted.TariffServiceId, date, ct: ct);
            var line = await billing.PostFolioChargeAsync(s.Bill, branchId, folio.Id, "Ipd",
                new NewChargeLine("service", unposted.TariffServiceId,
                    $"{service.Name} — bed {bed.Code}, {date:dd MMM yyyy}", 1, rate.Price, rate.RateVersionId),
                actorId, ct: ct);
            folios.RecordBedDay(s.Ipd, admissionId, date, unposted.BedId, line.Id);
        }
        await s.Ipd.SaveChangesAsync(ct);
        return unposted.Dates.Count;
    }

    /// <summary>US6.1: post a catalog service (oxygen, nursing, visit, visitor card…) to the
    /// folio — price resolves from the rate plan, the poster never types it.</summary>
    public static async Task<long> PostServiceAsync(
        TxScope s, BillingService billing, FolioService folios, RateResolver rates,
        TimeProvider clock, long branchId, long folioId, long serviceCatalogId, int qty,
        long? doctorId, long? latePostApprovalId, long actorId, CancellationToken ct = default)
    {
        await folios.EnsurePostableAsync(s.Ipd, s.Kernel, folioId, latePostApprovalId, ct);
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var service = await s.Adm.Services.AsNoTracking()
                          .SingleOrDefaultAsync(x => x.Id == serviceCatalogId && x.Active, ct)
                      ?? throw new IpdException("Unknown or inactive service.");
        var rate = await rates.ResolveAsync(s.Adm, "service", serviceCatalogId, today, ct: ct);
        var line = await billing.PostFolioChargeAsync(s.Bill, branchId, folioId, "Ipd",
            new NewChargeLine("service", serviceCatalogId, service.Name, qty, rate.Price,
                rate.RateVersionId, doctorId), actorId, ct: ct);
        return line.Id;
    }

    /// <summary>Admission-time postings: admission fee and the package line (5A-9).</summary>
    public static async Task PostAdmissionChargesAsync(
        TxScope s, BillingService billing, RateResolver rates, TimeProvider clock,
        long branchId, Admission admission, long? admissionFeeServiceId, long actorId,
        CancellationToken ct = default)
    {
        var folio = await s.Ipd.Folios.AsNoTracking()
            .SingleAsync(f => f.AdmissionId == admission.Id, ct);
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);

        if (admissionFeeServiceId is long feeId)
        {
            var fee = await s.Adm.Services.AsNoTracking().SingleAsync(x => x.Id == feeId, ct);
            var feeRate = await rates.ResolveAsync(s.Adm, "service", feeId, today, ct: ct);
            await billing.PostFolioChargeAsync(s.Bill, branchId, folio.Id, "Ipd",
                new NewChargeLine("service", feeId, fee.Name, 1, feeRate.Price, feeRate.RateVersionId),
                actorId, ct: ct);
        }

        if (admission.PackageId is long packageId)
        {
            var package = await s.Ipd.Packages.AsNoTracking().SingleAsync(p => p.Id == packageId, ct);
            var svc = await s.Adm.Services.AsNoTracking()
                .SingleAsync(x => x.Id == package.ServiceCatalogId, ct);
            var rate = await rates.ResolveAsync(s.Adm, "service", package.ServiceCatalogId, today, ct: ct);
            await billing.PostFolioChargeAsync(s.Bill, branchId, folio.Id, "Ipd",
                new NewChargeLine("service", package.ServiceCatalogId,
                    $"{svc.Name} (admission package)", 1, rate.Price, rate.RateVersionId),
                actorId, ct: ct);
        }
    }

    // ---- settlement (US6.2 — the hardest money math in the product) --------

    public const string ServiceChargeCode = "IPD-SVC-PCT";

    /// <summary>
    /// The admission states from which a folio may be closed. Discharge is the ordinary one;
    /// **death and absconding are exits too, and their bills must still be closable** — before
    /// spec 0021 a deceased patient's charges were stranded on an Open folio forever, and an
    /// absconded patient left no due for the follow-up §11 explicitly asks for.
    /// </summary>
    public static bool CanSettle(string admissionState) => admissionState is
        AdmissionState.ClinicallyCleared or AdmissionState.Death or AdmissionState.Absconded;

    /// <summary>
    /// Step 1: freeze the bill. Catches up bed days, posts the service-charge % line over the
    /// eligible base (everything except the package line — a package price is all-inclusive),
    /// then moves the folio to settlement draft.
    /// </summary>
    public static async Task PrepareSettlementAsync(
        TxScope s, BillingService billing, FolioService folios, RateResolver rates,
        TimeProvider clock, long branchId, long admissionId, long actorId, CancellationToken ct = default)
    {
        var admission = await s.Ipd.Admissions.AsNoTracking().SingleAsync(a => a.Id == admissionId, ct);
        if (admission.State == AdmissionState.Blocked)
            throw new IpdException(
                "This patient is blocked for dues (R4) — the folio is frozen. Release them "
                + "(Admissions → Block list) and the bill can be closed.");
        if (!CanSettle(admission.State))
            throw new IpdException(
                "The bill can be closed once the patient is clinically cleared for discharge, "
                + "or has died or absconded (§11).");

        var folio = await s.Ipd.Folios.AsNoTracking().SingleAsync(f => f.AdmissionId == admissionId, ct);
        var state = await folios.LockAsync(s.Ipd, folio.Id, ct);
        if (state != FolioState.Open)
            throw new IpdException(state == FolioState.Blocked
                ? "This patient is blocked for dues (R4) — release before settlement."
                : "The folio is not open for settlement.");

        await CatchUpBedDaysAsync(s, billing, folios, rates, branchId, admissionId, actorId, ct);

        if (admission.ServiceChargePct > 0)
        {
            long? packageServiceId = admission.PackageId is long pkgId
                ? (await s.Ipd.Packages.AsNoTracking().SingleAsync(p => p.Id == pkgId, ct)).ServiceCatalogId
                : null;
            var lines = await s.Bill.ChargeLines.AsNoTracking()
                .Where(c => c.FolioId == folio.Id && c.InvoiceId == null)
                .Select(c => new { c.CatalogId, c.Amount })
                .ToListAsync(ct);
            var eligible = lines.Where(l => l.CatalogId != packageServiceId).Sum(l => l.Amount);
            if (eligible > 0)
            {
                var scService = await s.Adm.Services.AsNoTracking()
                                    .SingleOrDefaultAsync(x => x.Code == ServiceChargeCode, ct)
                                ?? throw new IpdException(
                                    "Service-charge catalog item is missing — check masters.");
                var amount = BillingService.RoundHalfUp(eligible * admission.ServiceChargePct / 100m);
                await billing.PostFolioChargeAsync(s.Bill, branchId, folio.Id, "Ipd",
                    new NewChargeLine("service", scService.Id,
                        $"Service charge {admission.ServiceChargePct}% on ৳ {eligible:N0}", 1, amount),
                    actorId, ct: ct);
            }
        }

        await folios.BeginSettlementAsync(s.Ipd, folio.Id, ct);
    }

    public sealed record SettlementResult(long InvoiceId, long AdvanceApplied, long AdvanceReturned, long Due);

    /// <summary>
    /// Step 2: the settlement invoice. Folio lines freeze into an invoice, held advances apply
    /// to its due, excess advance goes back through the drawer as a negative folio receipt, the
    /// folio locks and the admission becomes financially settled — one transaction.
    /// </summary>
    public static async Task<SettlementResult> ConfirmSettlementAsync(
        TxScope s, BillingService billing, FolioService folios, IpdService ipd,
        long branchId, long admissionId, long sessionId,
        decimal discountPercent, long discountFlat, long? discountApprovalId,
        long actorId, string actorName, CancellationToken ct = default,
        Guid? submissionToken = null)
    {
        var folio = await s.Ipd.Folios.AsNoTracking().SingleAsync(f => f.AdmissionId == admissionId, ct);
        var state = await folios.LockAsync(s.Ipd, folio.Id, ct);
        if (state != FolioState.SettlementDraft)
            throw new IpdException("Prepare the settlement first — the folio is not in draft.");

        var (invoice, advanceApplied) = await billing.CreateFolioInvoiceAsync(
            s.Bill, s.Kernel, branchId, folio.Id, sessionId, folio.PatientId,
            discountPercent, discountFlat, discountApprovalId, actorId, actorName, ct,
            submissionToken);

        var held = await billing.AdvanceHeldAsync(s.Bill, folio.Id, ct);
        var excess = held - advanceApplied;
        if (excess > 0)
            await billing.CollectAdvanceAsync(s.Bill, s.Kernel, branchId, folio.Id, sessionId,
                -excess, "cash", null, actorId, actorName, ct);

        await folios.LockAtSettlementAsync(s.Ipd, s.Kernel, folio.Id, invoice.Id, advanceApplied,
            actorId, actorName, ct);

        // Only a living, cleared patient advances to "financially settled" on the way out of
        // the gate. Death and absconding are terminal clinical facts (§11) — closing their
        // bill must never overwrite them with a state that implies a normal discharge.
        var admissionState = await s.Ipd.Admissions.AsNoTracking()
            .Where(a => a.Id == admissionId).Select(a => a.State).SingleAsync(ct);
        if (admissionState == AdmissionState.ClinicallyCleared)
            await ipd.MarkFinanciallySettledAsync(s.Ipd, admissionId, ct);

        return new SettlementResult(invoice.Id, advanceApplied, excess > 0 ? excess : 0,
            invoice.Net - advanceApplied);
    }

    // ---- medicine indents (5A-9; closes 0016 deferrals #4/#11) -------------

    /// <summary>Issue a requested indent FEFO from an outlet: folio line per batch at that
    /// batch's MRP, allocations kept for discharge-time returns.</summary>
    public static async Task IssueIndentAsync(
        TxScope s, BillingService billing, FolioService folios, StockService stock,
        long branchId, long indentId, long outletId, long actorId, CancellationToken ct = default)
    {
        var indent = await s.Ipd.Indents.AsNoTracking().SingleOrDefaultAsync(i => i.Id == indentId, ct)
                     ?? throw new IpdException("Unknown indent.");
        await folios.EnsurePostableAsync(s.Ipd, s.Kernel, indent.FolioId, null, ct);
        await folios.ClaimIndentForIssueAsync(s.Ipd, indentId, actorId, ct);

        var items = await s.Ipd.IndentItems.Where(i => i.IndentId == indentId).ToListAsync(ct);
        foreach (var item in items)
        {
            var product = await s.Pharm.Products.AsNoTracking()
                .SingleAsync(p => p.Id == item.ProductId, ct);
            var allocations = await stock.AllocateFefoAsync(
                s.Pharm, outletId, item.ProductId, item.QtyRequested, "ipd.indent", indentId, actorId, ct);
            foreach (var a in allocations)
            {
                var line = await billing.PostFolioChargeAsync(s.Bill, branchId, indent.FolioId,
                    "Pharmacy", new NewChargeLine("medicine", product.Id,
                        $"{product.Brand} {product.Strength} {product.Form} (batch {a.BatchNo})",
                        a.Qty, a.UnitMrp), actorId, ct: ct);
                s.Pharm.IssueAllocations.Add(new Hms.Pharmacy.Data.IssueAllocation
                {
                    BranchId = branchId, IndentId = indentId, ChargeLineId = line.Id,
                    BatchId = a.BatchId, ProductId = a.ProductId, Qty = a.Qty,
                    UnitMrp = a.UnitMrp, UnitCost = a.UnitCost,
                });
            }
            item.QtyIssued = item.QtyRequested;
        }
        await s.Pharm.SaveChangesAsync(ct);
        await s.Ipd.SaveChangesAsync(ct);
    }

    /// <summary>Discharge-time return: exact batches restock, negative folio lines reverse the
    /// exact MRP charged (0016 deferral #11).</summary>
    public static async Task ReturnIndentItemAsync(
        TxScope s, BillingService billing, FolioService folios, StockService stock,
        long branchId, long indentId, long productId, int qty, long? latePostApprovalId,
        long actorId, CancellationToken ct = default)
    {
        var indent = await s.Ipd.Indents.AsNoTracking().SingleOrDefaultAsync(i => i.Id == indentId, ct)
                     ?? throw new IpdException("Unknown indent.");
        if (indent.State != IndentState.Issued)
            throw new IpdException("Only an issued indent can take returns.");
        await folios.EnsurePostableAsync(s.Ipd, s.Kernel, indent.FolioId, latePostApprovalId, ct);

        var product = await s.Pharm.Products.AsNoTracking().SingleAsync(p => p.Id == productId, ct);
        var restocked = await stock.RestockIndentAsync(s.Pharm, indentId, productId, qty, actorId, ct);
        foreach (var r in restocked)
            await billing.PostFolioChargeAsync(s.Bill, branchId, indent.FolioId, "Pharmacy",
                new NewChargeLine("medicine", productId,
                    $"Return: {product.Brand} {product.Strength} {product.Form} (batch {r.BatchNo})",
                    -r.Qty, r.UnitMrp), actorId, allowNegativeQty: true, ct: ct);

        var item = await s.Ipd.IndentItems
            .SingleAsync(i => i.IndentId == indentId && i.ProductId == productId, ct);
        item.QtyReturned += qty;
        await s.Ipd.SaveChangesAsync(ct);
    }

    // ---- investigation indent (5A-9; §11 indoor branch) --------------------

    /// <summary>Indoor test order: charges post to the folio, the order is born in-progress
    /// (no invoice gate) and its samples raise immediately — the LIS flow is unchanged.</summary>
    public static async Task<long> OrderTestsAsync(
        TxScope s, BillingService billing, FolioService folios, RateResolver rates, LisService lis,
        TimeProvider clock, long branchId, long folioId, IReadOnlyList<long> testCatalogIds,
        long? doctorId, long actorId, CancellationToken ct = default)
    {
        if (testCatalogIds.Count == 0) throw new IpdException("Pick at least one test.");
        await folios.EnsurePostableAsync(s.Ipd, s.Kernel, folioId, null, ct);

        var folio = await s.Ipd.Folios.AsNoTracking().SingleAsync(f => f.Id == folioId, ct);
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);

        var ordered = new List<OrderedTest>();
        var chargeIds = new List<long>();
        foreach (var testId in testCatalogIds)
        {
            var test = await s.Adm.TestCatalog.AsNoTracking()
                           .SingleOrDefaultAsync(t => t.Id == testId && t.Active, ct)
                       ?? throw new IpdException("Unknown or inactive test.");
            var rate = await rates.ResolveAsync(s.Adm, "test", testId, today, ct: ct);
            var line = await billing.PostFolioChargeAsync(s.Bill, branchId, folioId, "Diagnostics",
                new NewChargeLine("test", testId, test.Name, 1, rate.Price, rate.RateVersionId, doctorId),
                actorId, ct: ct);
            ordered.Add(new OrderedTest(testId, test.Name, test.TatMinutes, rate.Price, rate.RateVersionId));
            chargeIds.Add(line.Id);
        }

        var diagnostics = new DiagnosticsService(new ChargePoster(s.Bill, billing), clock);
        var order = await diagnostics.CreateFolioOrderAsync(
            s.Diag, s.Kernel, branchId, folio.PatientId, folioId, ordered, chargeIds, doctorId, actorId, ct);
        await DiagnosticsRelease.CreateSamplesAsync(s, lis, branchId, order.Id, actorId, ct);
        return order.Id;
    }
}
