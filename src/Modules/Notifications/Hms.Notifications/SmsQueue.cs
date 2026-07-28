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

    /// <summary>GSM 03.38 default alphabet — one septet each. ESC (0x1B) is deliberately absent:
    /// it is the extension prefix, never a character in its own right.</summary>
    private const string Gsm7Basic =
        "@£$¥èéùìòÇ\nØø\rÅå"
        + "Δ_ΦΓΛΩΠΨΣΘΞ"
        + "ÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?"
        + "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§"
        + "¿abcdefghijklmnopqrstuvwxyzäöñüà";

    /// <summary>The GSM 03.38 escape table — each costs TWO septets (ESC + the character),
    /// so a body of 80 braces is 160 septets, not 80.</summary>
    private const string Gsm7Extended = "\f^{}\\[~]|€";

    /// <summary>
    /// How many messages the operator is billed for. 160 (GSM-7) and 70 (UCS-2) are the
    /// <em>single-message</em> limits; the moment a body needs more than one part, every part
    /// spends 6 bytes of its payload on the UDH concatenation header, leaving <b>153</b> and
    /// <b>67</b>. Dividing the whole body by the single-message limit is the classic under-count
    /// — 320 Latin characters are three segments in the world and were two here, and a 210
    /// character Bangla message is four and was three. The tray prints this as "N billable
    /// segment(s)", so under-counting is the hospital under-reading its own spend.
    ///
    /// Encoding is decided by the GSM-7 alphabet, not by <c>c &gt; 127</c>: é and Ü are one
    /// septet despite being above 127, while a Bangla or emoji code point forces UCS-2 for the
    /// whole message. Two nuances are knowingly not modelled, both of which can only cost one
    /// extra segment on a pathological body: an escaped GSM-7 pair is never split across a
    /// segment boundary, and a UTF-16 surrogate pair is never split either.
    /// </summary>
    public static int SegmentsFor(string body)
    {
        if (string.IsNullOrEmpty(body)) return 1;

        var septets = 0;
        var gsm7 = true;
        foreach (var c in body)
        {
            if (Gsm7Basic.Contains(c)) septets += 1;
            else if (Gsm7Extended.Contains(c)) septets += 2;
            else { gsm7 = false; break; }
        }

        var (units, single, concatenated) = gsm7 ? (septets, 160, 153) : (body.Length, 70, 67);
        return units <= single ? 1 : (int)Math.Ceiling(units / (double)concatenated);
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
