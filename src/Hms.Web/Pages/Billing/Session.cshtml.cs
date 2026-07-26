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
    public IReadOnlyList<CounterOption> Counters { get; private set; } = [];
    public long TakenToday { get; private set; }
    public int InvoicesToday { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        (Current, Counters, TakenToday, InvoicesToday) = await tx.RunAsync(async s =>
        {
            var current = await CounterContext.FindOpenAsync(s.Bill, ActorId);

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
            return (current, (IReadOnlyList<CounterOption>)options, taken, count);
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
            var session = await tx.RunAsync(s =>
                billing.OpenSessionAsync(s.Bill, BranchId, CounterId, ActorId, OpeningFloat));
            Toast($"Counter open — float {Ui.Money(session.OpeningFloat)}", "lock");
            return Redirect("/billing/opd");
        }
        catch (BillingException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }
    }
}
