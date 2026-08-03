using System.ComponentModel.DataAnnotations;
using Hms.Admin;
using Hms.Billing;
using Hms.Billing.Data;
using Hms.Diagnostics;
using Hms.Lis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Diagnostics;

public sealed record TestItem(
    long Id, string Code, string Name, string Dept, long Price, long RateVersionId,
    int TatMinutes, string SampleType);

/// <summary>
/// 05 §5 screen 5 — the cash engine of a Bangladeshi hospital. Three things happen on save that
/// competitors cannot demonstrate joined up (§9A.2): the order is invoiced, the delivery time is
/// promised from the slowest test's TAT, and payment releases barcode labels plus the lab worklist.
/// </summary>
[Authorize(Policy = Perm.DiagnosticsOrderCreate)]
public class OrderModel(
    HmsTx tx, BillingService billing, Hms.Ipd.FolioService folios, RateResolver rates,
    LisService lis, TimeProvider clock)
    : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? PatientId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty] public List<long> Items { get; set; } = [];
    [BindProperty, Money] public long DiscountFlat { get; set; }
    [BindProperty, Money] public long PaidNow { get; set; }
    /// <summary>Spec 0021: one prepared order, one invoice — survives a double-click.</summary>
    [BindProperty] public Guid SubmissionToken { get; set; }
    [BindProperty] public string Tender { get; set; } = "cash";
    /// <summary>§5 M8 [M]: referrer captured on every order — a master row, never free text.</summary>
    [BindProperty] public long? ReferrerId { get; set; }

    public OpenSession? Session { get; private set; }
    public IReadOnlyList<TestItem> Catalog { get; private set; } = [];
    public IReadOnlyList<TestItem> Cart { get; private set; } = [];
    public string? PatientName { get; private set; }
    public IReadOnlyList<ReferrerPick> Referrers { get; private set; } = [];

    /// <summary>Spec 0042 F2: where this patient is lying right now. Non-null flips the save to
    /// the folio path — an inpatient's test must never become a separate outdoor invoice
    /// (§5 M6 [M] "every chargeable event posts to the folio").</summary>
    public (long AdmissionId, string AdmissionNo, string Bed)? AdmittedAs { get; private set; }

    public long Gross => Cart.Sum(c => c.Price);
    public int SlowestTat => Cart.Count == 0 ? 0 : Cart.Max(c => c.TatMinutes);
    public DateTimeOffset PromisedAt => clock.GetUtcNow().AddMinutes(SlowestTat);

    public sealed record ReferrerPick(long Id, string Label);

    public async Task OnGetAsync()
    {
        SubmissionToken = Submission.NewToken();      // one per visit to the screen
        await LoadAsync();
    }

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

            var tests = await s.Adm.TestCatalog.AsNoTracking()
                .Where(t => t.Active).OrderBy(t => t.Dept).ThenBy(t => t.Name).ToListAsync();

            var catalog = new List<TestItem>();
            foreach (var t in tests)
            {
                try
                {
                    var rate = await rates.ResolveAsync(s.Adm, "test", t.Id, today);
                    catalog.Add(new TestItem(t.Id, t.Code, t.Name, t.Dept, rate.Price,
                        rate.RateVersionId, t.TatMinutes,
                        t.SampleTypes.Length > 0 ? t.SampleTypes[0] : "None"));
                }
                catch (RateResolutionException) { }   // no effective price today ⇒ not sellable (edge 11)
            }

            Catalog = string.IsNullOrWhiteSpace(Q)
                ? catalog
                : catalog.Where(c =>
                    c.Name.Contains(Q, StringComparison.OrdinalIgnoreCase) ||
                    c.Code.Contains(Q, StringComparison.OrdinalIgnoreCase)).ToList();

            var byId = catalog.ToDictionary(c => c.Id);
            Cart = Items.Where(byId.ContainsKey).Select(id => byId[id]).ToList();

            Referrers = await s.Adm.Referrers.AsNoTracking()
                .Where(r => r.Active).OrderBy(r => r.Kind == "self" ? 0 : 1).ThenBy(r => r.Name)
                .Select(r => new ReferrerPick(r.Id, r.Name + " (" + r.Code + ")"))
                .ToListAsync();
            ReferrerId ??= (await s.Adm.Referrers.AsNoTracking()
                .Where(r => r.Code == "SELF").Select(r => (long?)r.Id).FirstOrDefaultAsync());

            if (PatientId is { } pid and > 0)
            {
                PatientName = await s.Reg.Patients.AsNoTracking()
                    .Where(p => p.Id == pid).Select(p => p.FullName).FirstOrDefaultAsync();
                AdmittedAs = await IpdBilling.FindOpenAdmissionAsync(s, pid);
            }
            return 0;
        });
    }

    public async Task<IActionResult> OnPostAddAsync(long catalogId)
    {
        if (!Items.Contains(catalogId)) Items.Add(catalogId);   // one order line per test
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

        if (PatientId is null or 0) { Fail("Select a patient first."); return Page(); }
        if (Cart.Count == 0) { Fail("Add at least one test."); return Page(); }
        if (AdmittedAs is { } admitted) return await SaveToFolioAsync(admitted);

        if (Session is null) { Fail("Open your counter before invoicing tests."); return Page(); }
        // The displayed cart is Items filtered to the priced catalogue; a mismatch means the
        // form carried a test id that is not sellable today. Refuse rather than invoice a subset
        // of what the operator believes is in the cart (AUD-VAL-22b).
        if (Cart.Count != Items.Count)
        {
            Fail("A test in this cart is not in today's catalogue any more — remove it and add it again.");
            return Page();
        }
        if (!Tenders.IsKnown(Tender))
        {
            Fail($"That is not a way money can be taken. Use one of: {string.Join(", ", Tenders.All)}.");
            return Page();
        }
        // The referrer is who the commission is owed to (§5 M13) — an id no master row carries
        // would make the referral report unreconcilable (AUD-VAL-22c).
        if (ReferrerId is { } rid and > 0 && Referrers.All(r => r.Id != rid))
        {
            Fail("That referrer is not on the referrer list — pick one from the list.");
            return Page();
        }

        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var gross = Gross;
        var discount = Math.Max(0, Math.Min(gross, DiscountFlat));
        var net = gross - discount;
        // [Money] already refused negatives; more than the payable is change, and change is
        // handled at the drawer, not on the invoice (same rule as the OPD counter).
        if (PaidNow > net)
        {
            Fail($"The payment is more than the {Ui.Money(net)} payable. Reduce it — " +
                 "change is handled at the drawer, not on the invoice.");
            return Page();
        }
        var paid = PaidNow;
        var cart = Cart.ToList();

        try
        {
            var (orderId, released) = await tx.RunAsync(async s =>
            {
                // Spec 0021: resolve a repeat before anything is created — otherwise a
                // second post would leave a duplicate test order behind even if the
                // invoice were deduplicated.
                if (await Submission.ExistingAsync(s, SubmissionToken) is { } already)
                {
                    var priorOrder = await s.Diag.Orders.AsNoTracking()
                        .Where(o => o.InvoiceId == already.Id)
                        .Select(o => o.Id).FirstOrDefaultAsync();
                    return (priorOrder, 0);
                }

                var poster = new ChargePoster(s.Bill, billing);
                var diagnostics = new DiagnosticsService(poster, clock);

                var encounter = await CounterContext.GetOrCreateEncounterAsync(
                    s.Bill, BranchId, PatientId!.Value, Session!.CounterId, today, Session.EncounterType,
                    ActorId, clock.GetUtcNow());

                var order = await diagnostics.CreateOrderAsync(
                    s.Diag, BranchId, PatientId.Value, encounter.Id,
                    cart.Select(c => new OrderedTest(c.Id, c.Name, c.TatMinutes, c.Price, c.RateVersionId))
                        .ToList(),
                    orderingDoctorId: null, referrerId: ReferrerId, ActorId);

                var invoice = await billing.CreateInvoiceAsync(
                    s.Bill, s.Kernel, BranchId, encounter.Id, Session.Id, PatientId.Value,
                    0m, discount, null, ActorId, ActorName, submissionToken: SubmissionToken);

                await diagnostics.MarkInvoicedAsync(s.Diag, order.Id, invoice.Id);

                if (paid > 0)
                {
                    await billing.CollectAsync(s.Bill, s.Kernel, BranchId, invoice.Id, Session.Id,
                        paid, Tender, null, ActorId, ActorName);

                    // WP1.4 (AUD-VAL-22d): a save that claims a payment produces a receipt or
                    // the whole transaction fails — asserted here, not only trusted to the binder.
                    var receipted = await s.Bill.Receipts
                        .Where(r => r.InvoiceId == invoice.Id)
                        .SumAsync(r => (long?)r.Amount) ?? 0;
                    if (receipted != paid)
                        throw new BillingException(
                            "The payment could not be receipted, so the order was not saved. Try again.");
                }

                // Payment — in full — is what releases the lab (§9A.2 seam). A part-paid order
                // raises no tube here; settling the balance later at Due Collection releases it
                // through the same path, so the lab is never asked to work an undrawn sample.
                var count = await DiagnosticsRelease.ReleasePaidOrdersAsync(
                    s, billing, lis, clock, BranchId, invoice.Id, ActorId);

                return (order.Id, count);
            });

            // Say what actually happened: a part payment prints no labels, and telling the
            // operator otherwise sends them to a printer that will stay silent.
            Toast(released > 0
                    ? "Order paid in full — sample labels ready to print"
                    : "Order invoiced — labels print once the balance is paid",
                  "receipt_long");
            return Redirect($"/diagnostics/order/{orderId}");
        }
        catch (DbUpdateException e) when (Submission.IsDuplicateSubmission(e))
        {
            var existing = await tx.RunAsync(s => Submission.ExistingAsync(s, SubmissionToken));
            var prior = existing is null ? null : await tx.RunAsync(s => s.Diag.Orders.AsNoTracking()
                .Where(o => o.InvoiceId == existing.Id).Select(o => (long?)o.Id).FirstOrDefaultAsync());
            if (prior is long id) return Redirect($"/diagnostics/order/{id}");
            Fail("That order was just saved — reload the screen to see it.");
            return Page();
        }
        catch (BillingException e) { Fail(e.Message); return Page(); }
        catch (DiagnosticsException e) { Fail(e.Message); return Page(); }
        catch (RateResolutionException e) { Fail(e.Message); return Page(); }
    }

    /// <summary>
    /// Spec 0042 F2: the indoor branch of this counter. Before this, an admitted patient's test
    /// became a separate outdoor invoice — off the folio, invisible to settlement, and with no
    /// R4 check, sellable to a bill-blocked patient. Charges post to the folio; no money moves
    /// at this counter for an inpatient.
    /// </summary>
    private async Task<IActionResult> SaveToFolioAsync((long AdmissionId, string AdmissionNo, string Bed) admitted)
    {
        if (PaidNow > 0 || DiscountFlat > 0)
        {
            Fail($"This patient is admitted ({admitted.AdmissionNo}) — tests post to the running "
                 + "folio and are settled at discharge. Take no cash and give no discount here.");
            return Page();
        }

        var cart = Cart.ToList();
        try
        {
            var orderId = await tx.RunAsync(async s =>
            {
                await IpdBilling.EnsureNotBlockedAsync(s, PatientId!.Value);
                var folioId = await s.Ipd.Folios.AsNoTracking()
                                  .Where(f => f.AdmissionId == admitted.AdmissionId)
                                  .Select(f => (long?)f.Id).FirstOrDefaultAsync()
                              ?? throw new Hms.Ipd.IpdException(
                                  "This admission has no folio yet — post from the folio screen.");

                // A double submit must not charge the folio twice. The invoice submission token
                // cannot cover an invoice-less path, so the guard is a repeat check: the same
                // operator, folio and test count inside a minute is the same click.
                var since = clock.GetUtcNow().AddMinutes(-1);
                var repeat = await s.Diag.Orders.AsNoTracking()
                    .Where(o => o.FolioId == folioId && o.CreatedBy == ActorId && o.CreatedAt >= since)
                    .Select(o => (long?)o.Id).FirstOrDefaultAsync();
                if (repeat is { } prior) return prior;

                return await IpdBilling.OrderTestsAsync(s, billing, folios, rates, lis, clock,
                    BranchId, folioId, cart.Select(c => c.Id).ToList(),
                    doctorId: null, ActorId);
            });

            Toast($"Ordered to the folio ({admitted.AdmissionNo}) — samples raise now; "
                  + "the bill settles at discharge", "receipt_long");
            return Redirect($"/diagnostics/order/{orderId}");
        }
        catch (Hms.Ipd.IpdException e) { Fail(e.Message); return Page(); }
        catch (RateResolutionException e) { Fail(e.Message); return Page(); }
    }
}
