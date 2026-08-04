using Hms.Billing;
using Hms.Billing.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Billing;

public sealed record TenderRow(string Tender, long Amount, int Count);
public sealed record ClosedRow(long SummaryId, DateOnly BusinessDay, string Counter, long Net, long Variance);

/// <summary>
/// 05 §5 screen 15. Variance is <em>recorded, never blocking</em> (edge 18) — a counter that is
/// short by ৳50 still closes, because a system that refuses to close is a system operators route
/// around. The session row lock serializes this against a receipt landing at the same moment.
/// </summary>
[Authorize(Policy = Perm.BillingSessionClose)]
public class DayCloseModel(HmsTx tx, DayCloseService dayClose) : HmsPageModel
{
    [BindProperty] public long CountedCash { get; set; }
    /// <summary>Spec 0048: two drawers can be open at once (outdoor + IPD). Default closes the
    /// outdoor one; ?Kind=ipd closes the IPD drawer. The form posts back to the same URL, so
    /// the query survives the POST.</summary>
    [BindProperty(SupportsGet = true)] public string? Kind { get; set; }

    public OpenSession? Session { get; private set; }
    public long OpeningFloat { get; private set; }
    public long CashTaken { get; private set; }
    public long ExpectedCash => OpeningFloat + CashTaken;
    public long Gross { get; private set; }
    public long Discount { get; private set; }
    public long Net { get; private set; }
    public long DueOutstanding { get; private set; }
    public int InvoiceCount { get; private set; }
    public IReadOnlyList<TenderRow> Tenders { get; private set; } = [];
    public IReadOnlyList<ClosedRow> Recent { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        await tx.RunAsync(async s =>
        {
            Session = Kind == CounterContext.IpdKind
                ? await CounterContext.FindOpenIpdAsync(s.Bill, ActorId)
                : await CounterContext.FindOpenOutdoorAsync(s.Bill, ActorId)
                  ?? await CounterContext.FindOpenIpdAsync(s.Bill, ActorId);

            if (Session is not null)
            {
                OpeningFloat = Session.OpeningFloat;

                var receipts = await s.Bill.Receipts.AsNoTracking()
                    .Where(r => r.CounterSessionId == Session.Id).ToListAsync();
                Tenders = receipts
                    .GroupBy(r => r.Tender)
                    .Select(g => new TenderRow(g.Key, g.Sum(r => r.Amount), g.Count()))
                    .OrderByDescending(t => t.Amount)
                    .ToList();
                CashTaken = Tenders.FirstOrDefault(t => t.Tender == "cash")?.Amount ?? 0;

                var invoices = await s.Bill.Invoices.AsNoTracking()
                    .Where(i => i.CounterSessionId == Session.Id).ToListAsync();
                InvoiceCount = invoices.Count;
                Gross = invoices.Sum(i => i.Gross);
                Discount = invoices.Sum(i => i.Discount);
                Net = invoices.Sum(i => i.Net);

                var ids = invoices.Select(i => i.Id).ToList();
                DueOutstanding = await s.Bill.Dues.AsNoTracking()
                    .Where(d => ids.Contains(d.InvoiceId)).SumAsync(d => (long?)d.Balance) ?? 0;
            }

            var closes = await s.Bill.DayCloses.AsNoTracking()
                .OrderByDescending(x => x.Id).Take(10).ToListAsync();
            var sessionIds = closes.Select(c => c.CounterSessionId).ToList();
            var sessions = await s.Bill.Sessions.AsNoTracking()
                .Where(x => sessionIds.Contains(x.Id)).ToListAsync();
            var counters = await s.Bill.Counters.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);

            Recent = closes.Select(c =>
            {
                var sess = sessions.FirstOrDefault(x => x.Id == c.CounterSessionId);
                return new ClosedRow(c.Id, c.BusinessDay,
                    sess is null ? "—" : counters.GetValueOrDefault(sess.CounterId, "—"),
                    c.Net, c.Variance);
            }).ToList();
            return 0;
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadAsync();
        if (Session is null) { Fail("You have no open counter session to close."); return Page(); }
        if (CountedCash < 0) { Fail("The counted cash cannot be negative."); return Page(); }

        try
        {
            var summary = await tx.RunAsync(s => dayClose.CloseAsync(
                s.Bill, s.Kernel, Session!.Id, CountedCash, ActorId, ActorName));

            Toast(summary.Variance == 0
                ? "Counter closed — cash matched exactly"
                : $"Counter closed — variance {Ui.Money(summary.Variance)} recorded", "savings");
            return Redirect($"/billing/statement/{summary.Id}");
        }
        catch (BillingException e)
        {
            Fail(e.Message);
            return Page();
        }
    }
}
