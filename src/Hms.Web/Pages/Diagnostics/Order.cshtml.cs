using Hms.Admin;
using Hms.Billing;
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
    HmsTx tx, BillingService billing, RateResolver rates, LisService lis, TimeProvider clock)
    : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public long? PatientId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty] public List<long> Items { get; set; } = [];
    [BindProperty] public long DiscountFlat { get; set; }
    [BindProperty] public long PaidNow { get; set; }
    [BindProperty] public string Tender { get; set; } = "cash";
    /// <summary>§5 M8 [M]: referrer captured on every order — a master row, never free text.</summary>
    [BindProperty] public long? ReferrerId { get; set; }

    public OpenSession? Session { get; private set; }
    public IReadOnlyList<TestItem> Catalog { get; private set; } = [];
    public IReadOnlyList<TestItem> Cart { get; private set; } = [];
    public IReadOnlyList<PatientPick> Patients { get; private set; } = [];
    public string? PatientName { get; private set; }
    public IReadOnlyList<ReferrerPick> Referrers { get; private set; } = [];

    public long Gross => Cart.Sum(c => c.Price);
    public int SlowestTat => Cart.Count == 0 ? 0 : Cart.Max(c => c.TatMinutes);
    public DateTimeOffset PromisedAt => clock.GetUtcNow().AddMinutes(SlowestTat);

    public sealed record PatientPick(long Id, string Label);
    public sealed record ReferrerPick(long Id, string Label);

    public async Task OnGetAsync() => await LoadAsync();

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

            Patients = await s.Reg.Patients.AsNoTracking()
                .Where(p => p.Active && p.MergedInto == null)
                .OrderByDescending(p => p.Id).Take(60)
                .Select(p => new PatientPick(p.Id, p.FullName + " — " + p.Uhid))
                .ToListAsync();

            Referrers = await s.Adm.Referrers.AsNoTracking()
                .Where(r => r.Active).OrderBy(r => r.Kind == "self" ? 0 : 1).ThenBy(r => r.Name)
                .Select(r => new ReferrerPick(r.Id, r.Name + " (" + r.Code + ")"))
                .ToListAsync();
            ReferrerId ??= (await s.Adm.Referrers.AsNoTracking()
                .Where(r => r.Code == "SELF").Select(r => (long?)r.Id).FirstOrDefaultAsync());

            if (PatientId is { } pid and > 0)
                PatientName = await s.Reg.Patients.AsNoTracking()
                    .Where(p => p.Id == pid).Select(p => p.FullName).FirstOrDefaultAsync();
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

        if (Session is null) { Fail("Open your counter before invoicing tests."); return Page(); }
        if (PatientId is null or 0) { Fail("Select a patient first."); return Page(); }
        if (Cart.Count == 0) { Fail("Add at least one test."); return Page(); }

        var today = DateOnly.FromDateTime(Ui.Local(clock.GetUtcNow()).DateTime);
        var gross = Gross;
        var discount = Math.Max(0, Math.Min(gross, DiscountFlat));
        var paid = Math.Max(0, Math.Min(gross - discount, PaidNow));
        var cart = Cart.ToList();

        try
        {
            var (orderId, released) = await tx.RunAsync(async s =>
            {
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
                    0m, discount, null, ActorId, ActorName);

                await diagnostics.MarkInvoicedAsync(s.Diag, order.Id, invoice.Id);

                if (paid > 0)
                    await billing.CollectAsync(s.Bill, s.Kernel, BranchId, invoice.Id, Session.Id,
                        paid, Tender, null, ActorId, ActorName);

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
        catch (BillingException e) { Fail(e.Message); return Page(); }
        catch (DiagnosticsException e) { Fail(e.Message); return Page(); }
        catch (RateResolutionException e) { Fail(e.Message); return Page(); }
    }
}
