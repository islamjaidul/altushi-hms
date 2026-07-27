using Hms.Billing;
using Hms.Kernel.Approvals;
using Hms.Pharmacy;
using Hms.Pharmacy.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Pharmacy;

public sealed record ShelfItem(
    long ProductId, string Brand, string Generic, string Strength, string Form,
    int OnHand, long Mrp, bool NearExpiry);
public sealed record PosCartLine(long ProductId, string Label, int Qty, long UnitMrp);

/// <summary>
/// §5 M11 [M] outdoor sale — the POS family (05 §4), same shape as OPD billing: the cart
/// lives in the form, prices come from batches server-side (FEFO at save, US11.1), and the
/// money path is the bill spine so dues/refunds/day-close need nothing pharmacy-specific.
/// Walk-in retail needs no registration; credit (a due) requires a named patient.
/// </summary>
[Authorize(Policy = Perm.PharmacySaleCreate)]
public class PosModel(
    HmsTx tx, BillingService billing, StockService stock, ApprovalEngine approvals,
    TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? PatientId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public long? OutletId { get; set; }
    [BindProperty] public List<long> Items { get; set; } = [];
    [BindProperty] public List<int> Qtys { get; set; } = [];
    [BindProperty] public long DiscountFlat { get; set; }
    [BindProperty] public string? DiscountReason { get; set; }
    [BindProperty] public bool StaffSale { get; set; }
    [BindProperty] public long PaidNow { get; set; }
    /// <summary>Spec 0021: one prepared sale, one invoice — survives a double-click.</summary>
    [BindProperty] public Guid SubmissionToken { get; set; }
    [BindProperty] public string Tender { get; set; } = "cash";
    [BindProperty] public long PaidNow2 { get; set; }
    [BindProperty] public string Tender2 { get; set; } = "card";
    [BindProperty] public string? TenderRef2 { get; set; }

    public OpenSession? Session { get; private set; }
    public IReadOnlyList<Outlet> Outlets { get; private set; } = [];
    public IReadOnlyList<ShelfItem> Shelf { get; private set; } = [];
    public IReadOnlyList<PosCartLine> Cart { get; private set; } = [];
    public string? PatientName { get; private set; }
    /// <summary>Set when the selected patient is lying in a bed right now (spec 0020).</summary>
    public (long AdmissionId, string AdmissionNo, string Bed)? Admitted { get; private set; }
    public bool DiscountPending { get; private set; }
    public long ApprovedDiscountId { get; private set; }
    public long ApprovedDiscountAmount { get; private set; }

    public long Gross => Cart.Sum(c => c.Qty * c.UnitMrp);

    public async Task OnGetAsync()
    {
        SubmissionToken = Submission.NewToken();      // one per visit to the counter screen
        await LoadAsync();
    }
    public async Task<IActionResult> OnPostAsync() { await LoadAsync(); return Page(); }

    public async Task<IActionResult> OnPostAddAsync(long productId)
    {
        var idx = Items.IndexOf(productId);
        if (idx >= 0) Qtys[idx] += 1;
        else { Items.Add(productId); Qtys.Add(1); }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int index)
    {
        if (index >= 0 && index < Items.Count) { Items.RemoveAt(index); Qtys.RemoveAt(index); }
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var near = today.AddDays(StockService.NearExpiryDays);

        await tx.RunAsync(async s =>
        {
            Session = await CounterContext.FindOpenAsync(s.Bill, ActorId);
            Outlets = await s.Pharm.Outlets.AsNoTracking().Where(o => o.Active).OrderBy(o => o.Id).ToListAsync();
            OutletId ??= Outlets.FirstOrDefault()?.Id;

            // Sellable = InStock and unexpired (US11.1); expired stock is invisible to the
            // sale by predicate, exactly as the allocator enforces it.
            var products = await s.Pharm.Products.AsNoTracking().Where(p => p.Active).ToListAsync();
            var batches = await s.Pharm.Batches.AsNoTracking()
                .Where(b => b.OutletId == OutletId && b.State == BatchState.InStock
                            && b.Expiry > today && b.QtyOnHand > 0)
                .ToListAsync();

            var term = Q?.Trim();
            Shelf = products
                .Where(p => string.IsNullOrWhiteSpace(term)
                            || p.Brand.Contains(term, StringComparison.OrdinalIgnoreCase)
                            || p.Generic.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(p =>
                {
                    var mine = batches.Where(b => b.ProductId == p.Id).OrderBy(b => b.Expiry).ToList();
                    return new ShelfItem(p.Id, p.Brand, p.Generic, p.Strength, p.Form,
                        mine.Sum(b => b.QtyOnHand),
                        mine.FirstOrDefault()?.Mrp ?? 0,
                        mine.FirstOrDefault() is { } f && f.Expiry <= near);
                })
                .OrderBy(x => x.Brand)
                .ToList();

            var byId = products.ToDictionary(p => p.Id);
            var priceOf = Shelf.ToDictionary(x => x.ProductId, x => x.Mrp);
            Cart = Items.Zip(Qtys, (id, qty) => (id, qty))
                .Where(x => byId.ContainsKey(x.id))
                .Select(x => new PosCartLine(x.id,
                    $"{byId[x.id].Brand} {byId[x.id].Strength} {byId[x.id].Form}",
                    Math.Max(1, x.qty), priceOf.GetValueOrDefault(x.id)))
                .ToList();

            if (PatientId is { } pid and > 0)
            {
                PatientName = await s.Reg.Patients.AsNoTracking()
                    .Where(p => p.Id == pid).Select(p => p.FullName + " — " + p.Uhid).FirstOrDefaultAsync();
                // Spec 0020 gap 3: a counter sale to an admitted patient is legitimate (the
                // attendant buys) but must never be invisible — ward medicine belongs on the
                // folio via an indent (5A-9), and a due raised here never reaches discharge.
                Admitted = await IpdBilling.FindOpenAdmissionAsync(s, pid);
            }

            if (PatientId is { } pid2 and > 0)
            {
                // Same unspent-approval detection as the OPD POS (§12/C7).
                var approvedForPatient = await s.Kernel.ApprovalRequests.AsNoTracking()
                    .Where(a => a.Type == "discount" && a.SourceTable == "reg.patient"
                                && a.SourceId == pid2 && a.State == Hms.Kernel.Data.ApprovalState.Approved)
                    .OrderByDescending(a => a.Id).Select(a => new { a.Id, a.Amount }).ToListAsync();
                if (approvedForPatient.Count > 0)
                {
                    var ids = approvedForPatient.Select(a => a.Id).ToList();
                    var spent = await s.Bill.Invoices.AsNoTracking()
                        .Where(i => i.DiscountApprovalId != null && ids.Contains(i.DiscountApprovalId!.Value))
                        .Select(i => i.DiscountApprovalId!.Value).ToListAsync();
                    if (approvedForPatient.FirstOrDefault(a => !spent.Contains(a.Id)) is { } usable)
                    {
                        ApprovedDiscountId = usable.Id;
                        ApprovedDiscountAmount = usable.Amount ?? 0;
                    }
                }
                DiscountPending = await s.Kernel.ApprovalRequests.AnyAsync(a =>
                    a.Type == "discount" && a.SourceTable == "reg.patient" && a.SourceId == pid2
                    && a.State == Hms.Kernel.Data.ApprovalState.Pending);
            }
            return 0;
        });
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadAsync();

        if (Session is null) { Fail("Open your counter before selling."); return Page(); }
        if (OutletId is null) { Fail("No pharmacy outlet configured."); return Page(); }
        if (Cart.Count == 0) { Fail("Add at least one medicine."); return Page(); }

        var gross = Gross;
        var discount = Math.Max(0, Math.Min(gross, DiscountFlat));
        long? approvalId = null;

        if (StaffSale && PatientId is null or 0)
        { Fail("A staff sale is attributed to a person — pick the staff member's patient record."); return Page(); }

        if (discount > 0)
        {
            var reason = StaffSale ? $"staff purchase — {DiscountReason}".TrimEnd(' ', '—') : DiscountReason;
            if (string.IsNullOrWhiteSpace(reason))
            { Fail("A discount needs a reason — it is attributed to you and shows on the MD dashboard."); return Page(); }
            if (PatientId is null or 0)
            { Fail("A discount needs a named customer — pick the patient record first."); return Page(); }

            if (ApprovedDiscountId > 0 && ApprovedDiscountAmount >= discount)
                approvalId = ApprovedDiscountId;
            else
            {
                var raise = await tx.RunAsync(s => approvals.RaiseAsync(
                    s.Kernel, BranchId, "discount", "reg.patient", PatientId!.Value,
                    ActorId, ActorRole, reason!.Trim(), discount));
                if (!raise.AutoApproved)
                {
                    DiscountPending = true;
                    Fail($"A discount of {Ui.Money(discount)} is above your limit — " +
                         "the request has gone to the approvals inbox; the sale is held here.");
                    return Page();
                }
                approvalId = raise.ApprovalId;
            }
        }

        var net = gross - discount;
        var tenders = new List<(long Amount, string Mode, string? Reference)>();
        if (PaidNow > 0) tenders.Add((PaidNow, Tender, null));
        if (PaidNow2 > 0) tenders.Add((PaidNow2, Tender2, TenderRef2));
        if (tenders.Sum(x => x.Amount) > net)
        { Fail($"The payment adds up to more than the {Ui.Money(net)} payable."); return Page(); }
        if (tenders.Sum(x => x.Amount) < net && PatientId is null or 0)
        { Fail("Credit needs a name to follow up — walk-in sales are paid in full. Pick the patient record, or collect the balance."); return Page(); }

        var items = Cart.Select(c => new SaleItem(c.ProductId, c.Qty)).ToList();
        try
        {
            var invoiceId = await tx.RunAsync(async s =>
            {
                // Spec 0021: resolve a repeat before any stock leaves the shelf.
                if (await Submission.ExistingAsync(s, SubmissionToken) is { } already)
                    return already.Id;

                var patientId = PatientId is > 0
                    ? PatientId.Value
                    : await PharmacySale.EnsureWalkInPatientAsync(s, BranchId, ActorId);
                return await PharmacySale.SaveAsync(
                    s, billing, stock, clock, BranchId, OutletId!.Value, Session!, patientId,
                    items, discount, approvalId, tenders, ActorId, ActorName, SubmissionToken);
            });
            Toast("Sale saved — receipt ready", "receipt_long");
            return Redirect($"/billing/invoice/{invoiceId}");
        }
        catch (DbUpdateException e) when (Submission.IsDuplicateSubmission(e))
        {
            var existing = await tx.RunAsync(s => Submission.ExistingAsync(s, SubmissionToken));
            if (existing is not null) return Redirect($"/billing/invoice/{existing.Id}");
            Fail("That sale was just saved — reload the counter screen to see it.");
            return Page();
        }
        catch (PharmacyException e) { Fail(e.Message); return Page(); }
        catch (BillingException e) { Fail(e.Message); return Page(); }
    }
}
