using Hms.Admin;
using Hms.Billing;
using Hms.Billing.Data;
using Hms.Kernel.Approvals;
using Hms.Kernel.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Billing;

public sealed record CatalogItem(long Id, string Code, string Name, string Dept, long Price, long RateVersionId);
public sealed record CartLine(long CatalogId, string Name, long Price);
public sealed record UnbilledLine(string Description, long Amount, string Source);

/// <summary>
/// 05 §5 screen 4 — the POS template. The cart lives in the form, not in the database: nothing
/// is written until the operator saves, so an abandoned bill leaves no trace. Prices are always
/// re-resolved server-side from the effective-dated rate plan (C6) — the browser never sets a price.
/// </summary>
[Authorize(Policy = Perm.BillingInvoiceCreate)]
public class OpdModel(
    HmsTx tx, BillingService billing, RateResolver rates, ApprovalEngine approvals,
    TimeProvider clock) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? PatientId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    /// <summary>Catalog ids in the cart, carried between round trips as hidden fields.</summary>
    [BindProperty] public List<long> Items { get; set; } = [];
    [BindProperty] public long DiscountFlat { get; set; }
    [BindProperty] public long PaidNow { get; set; }
    [BindProperty] public string Tender { get; set; } = "cash";
    /// <summary>5A-4 [Must]: a second tender on the same invoice — cash + card/mobile.</summary>
    [BindProperty] public long PaidNow2 { get; set; }
    [BindProperty] public string Tender2 { get; set; } = "card";
    [BindProperty] public string? TenderRef2 { get; set; }
    /// <summary>§5 M4 [M]: discounts carry an operator-stated reason, not a generated one.</summary>
    [BindProperty] public string? DiscountReason { get; set; }

    public OpenSession? Session { get; private set; }
    public IReadOnlyList<CatalogItem> Catalog { get; private set; } = [];
    public IReadOnlyList<CartLine> Cart { get; private set; } = [];
    public IReadOnlyList<UnbilledLine> Unbilled { get; private set; } = [];
    public string? PatientName { get; private set; }
    /// <summary>Set when the selected patient is lying in a bed right now (spec 0020).</summary>
    public (long AdmissionId, string AdmissionNo, string Bed)? Admitted { get; private set; }
    public long ApprovedDiscountId { get; private set; }
    public long ApprovedDiscountAmount { get; private set; }
    public bool DiscountPending { get; private set; }

    public long CartTotal => Cart.Sum(c => c.Price);
    public long UnbilledTotal => Unbilled.Sum(u => u.Amount);
    public long Gross => CartTotal + UnbilledTotal;

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>Patient change / catalogue filter: re-render with the cart intact.</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);

        await tx.RunAsync(async s =>
        {
            Session = await CounterContext.FindOpenAsync(s.Bill, ActorId);

            var services = await s.Adm.Services.AsNoTracking()
                .Where(x => x.Active)
                .OrderBy(x => x.Dept).ThenBy(x => x.Name)
                .ToListAsync();

            var catalog = new List<CatalogItem>();
            foreach (var svc in services)
            {
                // A service with no effective rate today is not sellable today — leave it out of
                // the catalogue entirely rather than fail at save (§7 U7, edge 11).
                try
                {
                    var rate = await rates.ResolveAsync(s.Adm, "service", svc.Id, today);
                    catalog.Add(new CatalogItem(svc.Id, svc.Code, svc.Name, svc.Dept, rate.Price, rate.RateVersionId));
                }
                catch (RateResolutionException) { }
            }

            Catalog = string.IsNullOrWhiteSpace(Q)
                ? catalog
                : catalog.Where(c =>
                    c.Name.Contains(Q, StringComparison.OrdinalIgnoreCase) ||
                    c.Code.Contains(Q, StringComparison.OrdinalIgnoreCase)).ToList();

            var byId = catalog.ToDictionary(c => c.Id);
            Cart = Items.Where(byId.ContainsKey)
                .Select(id => new CartLine(id, byId[id].Name, byId[id].Price))
                .ToList();

            if (PatientId is { } pid and > 0)
            {
                var patient = await s.Reg.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pid);
                PatientName = patient?.FullName;
                // Spec 0020 gap 3: an outdoor invoice for an in-house patient is a due that
                // discharge never sees — say so before it is saved.
                Admitted = await IpdBilling.FindOpenAdmissionAsync(s, pid);

                var encounter = await s.Bill.Encounters.AsNoTracking().FirstOrDefaultAsync(
                    e => e.PatientId == pid && e.OnDate == today && e.State == "open");
                if (encounter is not null)
                {
                    // The §9A.2 seam: charges another counter or the doctor raised are already
                    // here — the operator never re-types what the hospital already knows.
                    Unbilled = await s.Bill.ChargeLines.AsNoTracking()
                        .Where(c => c.EncounterId == encounter.Id && c.InvoiceId == null)
                        .Select(c => new UnbilledLine(c.DescriptionSnapshot, c.Amount, c.SourceModule))
                        .ToListAsync();
                }

                // A discount already approved for this patient and not yet spent on an invoice.
                // kernel.* and bill.* are separate contexts (ADR-0003), so "not yet spent" is a
                // second query rather than a sub-select across the module boundary.
                var approvedForPatient = await s.Kernel.ApprovalRequests.AsNoTracking()
                    .Where(a => a.Type == "discount" && a.SourceTable == "reg.patient"
                                && a.SourceId == pid && a.State == ApprovalState.Approved)
                    .OrderByDescending(a => a.Id)
                    .Select(a => new { a.Id, a.Amount })
                    .ToListAsync();

                if (approvedForPatient.Count > 0)
                {
                    var candidateIds = approvedForPatient.Select(a => a.Id).ToList();
                    var spent = await s.Bill.Invoices.AsNoTracking()
                        .Where(i => i.DiscountApprovalId != null
                                    && candidateIds.Contains(i.DiscountApprovalId!.Value))
                        .Select(i => i.DiscountApprovalId!.Value)
                        .ToListAsync();

                    var usable = approvedForPatient.FirstOrDefault(a => !spent.Contains(a.Id));
                    if (usable is not null)
                    {
                        ApprovedDiscountId = usable.Id;
                        ApprovedDiscountAmount = usable.Amount ?? 0;
                    }
                }

                DiscountPending = await s.Kernel.ApprovalRequests.AnyAsync(a =>
                    a.Type == "discount" && a.SourceTable == "reg.patient" && a.SourceId == pid
                    && a.State == ApprovalState.Pending);
            }
            return 0;
        });
    }

    public async Task<IActionResult> OnPostAddAsync(long catalogId)
    {
        Items.Add(catalogId);
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int index)
    {
        if (index >= 0 && index < Items.Count) Items.RemoveAt(index);
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        await LoadAsync();

        if (Session is null) { Fail("Open your counter before billing."); return Page(); }
        if (PatientId is null or 0) { Fail("Select a patient first."); return Page(); }
        if (Cart.Count == 0 && Unbilled.Count == 0) { Fail("Add at least one service."); return Page(); }

        var gross = Gross;
        var discount = Math.Max(0, Math.Min(gross, DiscountFlat));
        long? approvalId = null;

        if (discount > 0 && string.IsNullOrWhiteSpace(DiscountReason))
        {
            Fail("A discount needs a reason — it is attributed to you and shows on the MD dashboard.");
            return Page();
        }

        if (discount > 0)
        {
            if (ApprovedDiscountId > 0 && ApprovedDiscountAmount >= discount)
            {
                approvalId = ApprovedDiscountId;
            }
            else
            {
                // §12/C7: routing and thresholds are policy data. Under the role's threshold this
                // returns approved synchronously, so ordinary small discounts never wait (§8 N1).
                var raise = await tx.RunAsync(s => approvals.RaiseAsync(
                    s.Kernel, BranchId, "discount", "reg.patient", PatientId!.Value,
                    ActorId, ActorRole, DiscountReason!.Trim(), discount));

                if (!raise.AutoApproved)
                {
                    await LoadAsync();
                    DiscountPending = true;
                    Fail($"A discount of {Ui.Money(discount)} is above your limit. " +
                         "The request has gone to the supervisor's approvals inbox — " +
                         "the bill is held here until it is decided.");
                    return Page();
                }
                approvalId = raise.ApprovalId;
            }
        }

        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var net = gross - discount;
        // Split tender: each row becomes its own receipt against the one invoice, so the
        // day-close tender breakdown stays truthful about what actually entered the drawer.
        var tenders = new List<(long Amount, string Mode, string? Reference)>();
        if (PaidNow > 0) tenders.Add((PaidNow, Tender, null));
        if (PaidNow2 > 0) tenders.Add((PaidNow2, Tender2, TenderRef2));
        if (tenders.Sum(x => x.Amount) > net)
        {
            Fail($"The payment adds up to more than the {Ui.Money(net)} payable. " +
                 "Reduce a line — change is handled at the drawer, not on the invoice.");
            return Page();
        }
        var cartIds = Items.ToList();

        try
        {
            var invoiceId = await tx.RunAsync(async s =>
            {
                // R4 (spec 0017): a due-blocked patient takes no new charges at any counter.
                await IpdBilling.EnsureNotBlockedAsync(s, PatientId!.Value);

                var encounter = await CounterContext.GetOrCreateEncounterAsync(
                    s.Bill, BranchId, PatientId!.Value, Session!.CounterId, today, Session.EncounterType,
                    ActorId, clock.GetUtcNow());

                foreach (var id in cartIds)
                {
                    var svc = await s.Adm.Services.AsNoTracking().SingleAsync(x => x.Id == id);
                    var rate = await rates.ResolveAsync(s.Adm, "service", id, today);
                    await billing.PostChargeAsync(s.Bill, BranchId, encounter.Id, "Billing",
                        new NewChargeLine("service", id, svc.Name, 1, rate.Price, rate.RateVersionId),
                        ActorId);
                }

                var invoice = await billing.CreateInvoiceAsync(
                    s.Bill, s.Kernel, BranchId, encounter.Id, Session.Id, PatientId.Value,
                    0m, discount, approvalId, ActorId, ActorName);

                foreach (var (amount, mode, reference) in tenders)
                    await billing.CollectAsync(s.Bill, s.Kernel, BranchId, invoice.Id, Session.Id,
                        amount, mode, reference, ActorId, ActorName);

                return invoice.Id;
            });

            Toast("Invoice saved — money receipt ready", "receipt_long");
            return Redirect($"/billing/invoice/{invoiceId}");
        }
        catch (BillingException e)
        {
            Fail(e.Message);
            return Page();
        }
        catch (Hms.Ipd.IpdException e)
        {
            Fail(e.Message);
            return Page();
        }
        catch (RateResolutionException e)
        {
            Fail(e.Message);
            return Page();
        }
    }
}
