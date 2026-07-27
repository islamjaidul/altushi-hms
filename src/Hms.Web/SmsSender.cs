using Hms.Notifications;

namespace Hms.Web;

/// <summary>
/// One place where an event becomes a message: look up the template, honour its on/off switch,
/// render the variables, queue it. Callers state the facts; wording and policy live in the
/// template store (§5 M20 [M]).
/// </summary>
public static class SmsSender
{
    public static async Task SendAsync(
        TxScope s, SmsQueue queue, long branchId, string @event,
        string? recipient, IReadOnlyDictionary<string, string?> values,
        CancellationToken ct = default)
    {
        var templates = await SmsTemplates.LoadAllAsync(s.Kernel, ct);
        var template = templates.FirstOrDefault(t => t.Event == @event);

        // Switched off is a deliberate configuration choice, not a failure — nothing is queued.
        if (template is null || !template.Enabled) return;

        queue.Queue(s.Notif, branchId, @event, recipient, SmsTemplates.Render(template.Body, values));
        await s.Notif.SaveChangesAsync(ct);
    }
}
