using System.Text.Json;
using Hms.Kernel.Data;
using Hms.Notifications.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

public sealed record SmsTemplate(string Event, string Body, bool Enabled);

/// <summary>
/// §5 M20 [M]: "template management with variables" and "per-module on/off switches".
/// Templates live in `kernel.setting` because they are configuration, not transactions — the
/// hospital edits its own wording during construction without a deployment (§9A.1 F1).
/// A missing or disabled template is not an error: the message is simply not sent, and the
/// tray records why.
/// </summary>
public static class SmsTemplates
{
    public const string Prefix = "sms.template.";

    /// <summary>The wording the product ships with; editable, never lost.</summary>
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [SmsEvent.Registration] =
            "{hospital}: Welcome {patient}. Your patient ID is {uhid}. Please keep it for all future visits.",
        [SmsEvent.Appointment] =
            "{hospital}: {patient}, serial {serial} with {doctor} is confirmed for today.",
        [SmsEvent.ReportReady] =
            "{hospital}: Dear {patient}, your report for order {order} is ready for collection.",
    };

    public static readonly IReadOnlyDictionary<string, string[]> Variables = new Dictionary<string, string[]>
    {
        [SmsEvent.Registration] = ["hospital", "patient", "uhid"],
        [SmsEvent.Appointment] = ["hospital", "patient", "serial", "doctor"],
        [SmsEvent.ReportReady] = ["hospital", "patient", "order"],
    };

    public static async Task<IReadOnlyList<SmsTemplate>> LoadAllAsync(
        KernelDbContext kernel, CancellationToken ct = default)
    {
        var stored = await kernel.Settings.AsNoTracking()
            .Where(s => s.Key.StartsWith(Prefix))
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return Defaults.Select(kv =>
        {
            var saved = stored.GetValueOrDefault(Prefix + kv.Key);
            if (saved is null) return new SmsTemplate(kv.Key, kv.Value, true);
            try
            {
                var t = JsonSerializer.Deserialize<SmsTemplate>(saved);
                return t is null ? new SmsTemplate(kv.Key, kv.Value, true) : t with { Event = kv.Key };
            }
            catch (JsonException) { return new SmsTemplate(kv.Key, kv.Value, true); }
        }).ToList();
    }

    public static async Task SaveAsync(
        KernelDbContext kernel, string @event, string body, bool enabled, CancellationToken ct = default)
    {
        var key = Prefix + @event;
        var json = JsonSerializer.Serialize(new SmsTemplate(@event, body, enabled));
        var existing = await kernel.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (existing is null) kernel.Settings.Add(new Setting { Key = key, Value = json });
        else existing.Value = json;
        await kernel.SaveChangesAsync(ct);
    }

    /// <summary>Substitutes {name} placeholders; an unknown placeholder is left visible rather
    /// than silently blanked, so a broken template is obvious in the tray instead of in the wild.</summary>
    public static string Render(string body, IReadOnlyDictionary<string, string?> values)
    {
        foreach (var (key, value) in values)
            body = body.Replace("{" + key + "}", value ?? "", StringComparison.OrdinalIgnoreCase);
        return body;
    }
}
