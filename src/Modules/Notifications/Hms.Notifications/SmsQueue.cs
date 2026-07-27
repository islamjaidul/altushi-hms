using Hms.Notifications.Data;

namespace Hms.Notifications;

/// <summary>Gateway posture. `HMS_SMS_MODE=live` flips it; the MVP ships simulation (edge 3).</summary>
public sealed record SmsOptions(bool Simulated)
{
    public static SmsOptions From(string? mode) =>
        new(!string.Equals(mode, "live", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// §9A.2 module 8. Messages are staged on the caller's context so the SMS commits with the
/// business fact that caused it (G19) — a rolled-back registration never leaves an SMS behind.
/// Simulation mode (edge 3) is the MVP default: the tray renders the exact body the gateway
/// would have sent, stamped SIMULATED, so a demo without a SIM card still shows the beat.
/// </summary>
public sealed class SmsQueue(TimeProvider clock, SmsOptions options)
{
    private bool Simulated => options.Simulated;

    /// <summary>160 GSM-7 characters per segment; anything non-Latin bills as UCS-2 at 70.</summary>
    public static int SegmentsFor(string body)
    {
        var unicode = body.Any(c => c > 127);
        var perSegment = unicode ? 70 : 160;
        return Math.Max(1, (int)Math.Ceiling(body.Length / (double)perSegment));
    }

    /// <summary>Edge 24: a patient with no phone is not a failure — it is a recorded skip.</summary>
    public SmsMessage Queue(NotifDbContext notif, long branchId, string @event, string? recipient, string body)
    {
        var msg = new SmsMessage
        {
            BranchId = branchId,
            Event = @event,
            Recipient = string.IsNullOrWhiteSpace(recipient) ? null : recipient.Trim(),
            Body = body,
            Segments = SegmentsFor(body),
            Simulated = Simulated,
            QueuedAt = clock.GetUtcNow(),
            State = string.IsNullOrWhiteSpace(recipient) ? SmsState.SkippedNoPhone : SmsState.Queued,
        };
        // In simulation the message is "sent" the moment it is queued — the tray is the gateway.
        if (msg.State == SmsState.Queued && Simulated)
        {
            msg.State = SmsState.Sent;
            msg.SentAt = msg.QueuedAt;
        }
        notif.Sms.Add(msg);
        return msg;
    }

    /// <summary>Re-queues an existing message unchanged — the operator resends, never rewrites
    /// (§5 M20 [M] "resend from log").</summary>
    public SmsMessage Resend(NotifDbContext notif, SmsMessage original)
        => Queue(notif, original.BranchId, original.Event, original.Recipient, original.Body);
}
