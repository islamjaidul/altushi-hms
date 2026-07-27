using Hms.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web.Pages.Admin;

/// <summary>
/// §5 M20 [M]: template management with variables, per-event on/off switches, and resend from
/// the log. The hospital edits its own wording during construction; nothing here needs a deploy.
/// </summary>
[Authorize(Policy = Perm.AdminMastersManage)]
public class SmsModel(HmsTx tx, SmsQueue queue) : HmsPageModel
{
    [BindProperty] public string? Event { get; set; }
    [BindProperty] public string? Body { get; set; }
    [BindProperty] public bool Enabled { get; set; }

    public IReadOnlyList<SmsTemplate> Templates { get; private set; } = [];
    public IReadOnlyDictionary<string, string[]> Variables => SmsTemplates.Variables;

    public async Task OnGetAsync() => await LoadAsync();

    private async Task LoadAsync() =>
        Templates = await tx.RunAsync(s => SmsTemplates.LoadAllAsync(s.Kernel));

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Event) || string.IsNullOrWhiteSpace(Body))
        { await LoadAsync(); Fail("The message body cannot be empty."); return Page(); }

        // A template that references an unknown variable would ship a literal {foo} to a patient.
        var known = SmsTemplates.Variables.GetValueOrDefault(Event!, []);
        var used = System.Text.RegularExpressions.Regex.Matches(Body!, @"\{([a-zA-Z]+)\}")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        var unknown = used.Where(u => !known.Contains(u, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0)
        {
            await LoadAsync();
            Fail($"Unknown placeholder(s): {string.Join(", ", unknown.Select(u => "{" + u + "}"))}. " +
                 $"Available here: {string.Join(", ", known.Select(k => "{" + k + "}"))}.");
            return Page();
        }

        await tx.RunAsync(s => SmsTemplates.SaveAsync(s.Kernel, Event!, Body!.Trim(), Enabled));
        Toast($"{Ui.Humanize(Event!)} template saved" + (Enabled ? "" : " — sending is switched off"), "sms");
        return Redirect("/admin/sms");
    }

    /// <summary>Resend re-queues the message that was actually sent — it never re-renders it,
    /// so the patient receives the same words the log already shows.</summary>
    public async Task<IActionResult> OnPostResendAsync(long id)
    {
        await tx.RunAsync(async s =>
        {
            var original = await s.Notif.Sms.AsNoTracking().SingleAsync(m => m.Id == id);
            queue.Resend(s.Notif, original);
            await s.Notif.SaveChangesAsync();
            return 0;
        });
        Toast("Message re-queued", "replay");
        return Redirect("/notifications/tray");
    }
}
