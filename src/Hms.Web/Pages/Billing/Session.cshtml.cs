using Hms.Billing;
using Hms.Billing.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Billing;

public sealed record CounterOption(long Id, string Name, string Kind, string? OpenBy);

/// <summary>
/// Opening the counter is the first act of a billing day (02 §2.4): it fixes the business day
/// receipts are attributed to and records the opening float that day-close will reconcile.
/// </summary>
[Authorize(Policy = Perm.BillingSessionOpen)]
public class SessionModel(HmsTx tx, BillingService billing) : HmsPageModel
{
    [BindProperty] public long CounterId { get; set; }
    [BindProperty] public long OpeningFloat { get; set; }

    public OpenSession? Current { get; private set; }
    /// <summary>Spec 0048: the IPD drawer is its own slot — one outdoor and one IPD session
    /// may be open for the same operator at once (sessions are unique per counter).</summary>
    public OpenSession? CurrentIpd { get; private set; }
    public IReadOnlyList<CounterOption> Counters { get; private set; } = [];
    public long TakenToday { get; private set; }
    public int InvoicesToday { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        (Current, CurrentIpd, Counters, TakenToday, InvoicesToday) = await tx.RunAsync(async s =>
        {
            var current = await CounterContext.FindOpenOutdoorAsync(s.Bill, ActorId);
            var currentIpd = await CounterContext.FindOpenIpdAsync(s.Bill, ActorId);

            var openStates = new[] { SessionState.Active, SessionState.Opened, SessionState.Reopened };
            var busy = await s.Bill.Sessions
                .Where(x => openStates.Contains(x.State))
                .Select(x => new { x.CounterId, x.OperatorId })
                .ToListAsync();
            var operators = await s.Auth.Users
                .Where(u => busy.Select(b => b.OperatorId).Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

            var counters = await s.Bill.Counters.AsNoTracking().OrderBy(c => c.Id)
                .Select(c => new { c.Id, c.Name, c.Kind }).ToListAsync();

            var options = counters.Select(c =>
            {
                var holder = busy.FirstOrDefault(b => b.CounterId == c.Id);
                return new CounterOption(c.Id, c.Name, c.Kind,
                    holder is null ? null : operators.GetValueOrDefault(holder.OperatorId, "another operator"));
            }).ToList();

            long taken = 0;
            var count = 0;
            if (current is not null)
            {
                taken = await s.Bill.Receipts.Where(r => r.CounterSessionId == current.Id)
                    .SumAsync(r => (long?)r.Amount) ?? 0;
                count = await s.Bill.Invoices.CountAsync(i => i.CounterSessionId == current.Id);
            }
            return (current, currentIpd, (IReadOnlyList<CounterOption>)options, taken, count);
        });
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (CounterId == 0)
        {
            await LoadAsync();
            Fail("Choose the counter you are working at.");
            return Page();
        }
        if (OpeningFloat < 0)
        {
            await LoadAsync();
            Fail("The opening float cannot be negative.");
            return Page();
        }

        try
        {
            var (session, kind) = await tx.RunAsync(async s =>
            {
                var opened = await billing.OpenSessionAsync(
                    s.Bill, BranchId, CounterId, ActorId, OpeningFloat);
                var counterKind = await s.Bill.Counters.AsNoTracking()
                    .Where(c => c.Id == CounterId).Select(c => c.Kind).FirstOrDefaultAsync();
                return (opened, counterKind);
            });
            Toast($"Counter open — float {Ui.Money(session.OpeningFloat)}", "lock");
            // Spec 0048: an IPD drawer lands on the IPD workspace, not the OPD till.
            return Redirect(kind == CounterContext.IpdKind ? "/billing/ipd" : "/billing/opd");
        }
        catch (BillingException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }
}
