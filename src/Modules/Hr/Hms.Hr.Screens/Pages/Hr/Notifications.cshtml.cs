using Hms.Hr.Data;
using Hms.Kernel.Time;
using Hms.Shell;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Hr.Screens.Pages.Hr;

/// <summary>
/// The employer's alert switches (module PRD §7.14, G57 — "each independently switchable by the
/// employer").
/// <para>
/// Only the kinds this product actually raises appear here. A switch that controls nothing is a lie
/// told by a settings screen, so the list grows as each capability lands rather than being written
/// out in full and half-wired.
/// </para>
/// <para>
/// <c>Enabled</c> governs the <b>message</b>; the horizon governs both the message and how far ahead
/// the registers and the command centre look. The ERP SKU has an SMS gateway (M20); the standalone
/// HRM SKU ships no notifications module at all, and this screen says so rather than offering a
/// switch that cannot fire.
/// </para>
/// </summary>
[Authorize(Policy = HrPerm.PolicyManage)]
public class NotificationsModel(IHrTx tx, HrAlertService alerts, HrNotificationChannel channel)
    : HmsPageModel
{
    public IReadOnlyList<NotificationSetting> Settings { get; private set; } = [];
    public IReadOnlyList<HrAlert> DueNow { get; private set; } = [];
    public bool ChannelAvailable => channel.Available;
    public string ChannelName => channel.Name;

    [BindProperty] public Dictionary<string, bool> Enabled { get; set; } = [];
    [BindProperty] public Dictionary<string, int> DaysBefore { get; set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!Can("hr.policy.manage")) return Forbid();

        try
        {
            await tx.RunAsync(async s =>
            {
                foreach (var kind in HrNotificationKind.All)
                {
                    await alerts.SaveSettingAsync(
                        s.Hr, s.Kernel, BranchId, kind,
                        Enabled.GetValueOrDefault(kind),
                        DaysBefore.GetValueOrDefault(kind, HrNotificationKind.DefaultDaysBefore(kind)),
                        ActorId, ActorName);
                }
            });
        }
        catch (HrException e)
        {
            await LoadAsync();
            Fail(e.Message);
            return Page();
        }

        Toast("Alert settings saved", "notifications");
        return Redirect("/hr/notifications");
    }

    private async Task LoadAsync()
    {
        var today = Dhaka.Today(TimeProvider.System);

        await tx.RunAsync(async s =>
        {
            var settings = await HrAlertService.SettingsAsync(s.Hr, BranchId);
            Settings = [.. HrNotificationKind.All.Select(k => settings[k])];

            // Shown beside the switches so the horizon is never abstract: this is what would fire
            // today under the settings as they stand.
            DueNow = await HrAlertService.DueAsync(s.Hr, BranchId, today, settings);
        });
    }

    public int CountFor(string kind) => DueNow.Count(a => a.Kind == kind);
}
