using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Billing;

/// <summary>
/// The day-close statement (§6.6): the piece of paper the counter hands the accounts desk.
/// It reads the stored summary row, not a recomputation — reopening a session appends a new
/// version rather than editing this one (02 §2.6).
/// </summary>
[Authorize(Policy = Perm.BillingSessionClose)]
public class StatementModel(HmsTx tx) : HmsPageModel
{
    public DateOnly BusinessDay { get; private set; }
    public string CounterName { get; private set; } = "";
    public string OperatorName { get; private set; } = "";
    public DateTimeOffset ClosedAt { get; private set; }
    public long Gross { get; private set; }
    public long Discount { get; private set; }
    public long Net { get; private set; }
    public long DueCreated { get; private set; }
    public long DueCollected { get; private set; }
    public long Refunds { get; private set; }
    public long Variance { get; private set; }
    public long OpeningFloat { get; private set; }
    public long CountedCash { get; private set; }
    public long ExpectedCash { get; private set; }
    public IReadOnlyList<TenderRow> Tenders { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var found = await tx.RunAsync(async s =>
        {
            var summary = await s.Bill.DayCloses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
            if (summary is null) return false;

            var session = await s.Bill.Sessions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == summary.CounterSessionId);
            var counter = session is null ? null
                : await s.Bill.Counters.AsNoTracking().SingleOrDefaultAsync(c => c.Id == session.CounterId);
            var op = session is null ? null
                : await s.Auth.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == session.OperatorId);

            BusinessDay = summary.BusinessDay;
            CounterName = counter?.Name ?? "—";
            OperatorName = op?.DisplayName ?? "—";
            ClosedAt = session?.ClosedAt ?? DateTimeOffset.UtcNow;
            Gross = summary.Gross;
            Discount = summary.Discount;
            Net = summary.Net;
            DueCreated = summary.DueCreated;
            DueCollected = summary.DueCollected;
            Refunds = summary.Refunds;
            Variance = summary.Variance;
            OpeningFloat = session?.OpeningFloat ?? 0;
            CountedCash = session?.CountedCash ?? 0;
            ExpectedCash = session?.ExpectedCash ?? 0;

            var totals = JsonSerializer.Deserialize<Dictionary<string, long>>(summary.TenderTotals) ?? [];
            Tenders = totals.Select(kv => new TenderRow(kv.Key, kv.Value, 0))
                .OrderByDescending(t => t.Amount).ToList();
            return true;
        });

        return found ? Page() : NotFound();
    }
}
