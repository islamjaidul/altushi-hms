using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Notifications;

public sealed record SmsRow(
    long Id, string Event, string? Recipient, string Body, string State,
    int Segments, bool Simulated, DateTimeOffset QueuedAt);

/// <summary>
/// §9A.2 module 8 + edge 3: with no gateway procured, the tray <em>is</em> the gateway. It shows
/// the exact body that would have been sent, stamped simulated — so a demo on a construction site
/// with no SIM card still shows the beat, and nobody has to pretend.
/// </summary>
[Authorize(Policy = Perm.NotificationsRead)]
public class TrayModel(HmsTx tx) : HmsPageModel
{
    [BindProperty(SupportsGet = true)] public string? Event { get; set; }

    public IReadOnlyList<SmsRow> Rows { get; private set; } = [];
    public int Sent { get; private set; }
    public int Skipped { get; private set; }
    public int Segments { get; private set; }
    public bool AnyLive { get; private set; }

    public async Task OnGetAsync()
    {
        await tx.RunAsync(async s =>
        {
            var query = s.Notif.Sms.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(Event))
                query = query.Where(m => m.Event == Event);

            Rows = await query.OrderByDescending(m => m.Id).Take(120)
                .Select(m => new SmsRow(m.Id, m.Event, m.Recipient, m.Body, m.State,
                    m.Segments, m.Simulated, m.QueuedAt))
                .ToListAsync();

            var all = await s.Notif.Sms.AsNoTracking()
                .Select(m => new { m.State, m.Segments, m.Simulated }).ToListAsync();
            Sent = all.Count(m => m.State is "sent" or "delivered");
            Skipped = all.Count(m => m.State == "skipped_no_phone");
            Segments = all.Sum(m => m.Segments);
            AnyLive = all.Any(m => !m.Simulated);
            return 0;
        });
    }
}
