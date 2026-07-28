using Hms.Notifications;

namespace Hms.Web.Tests;

/// <summary>
/// Spec 0032 M20-D1. `SmsQueue.SegmentsFor` is the arithmetic behind the tray's
/// "N billable segment(s)" tile — the only number the hospital has for what its messaging
/// costs. Before this file, nothing in `tests/` referenced `SmsQueue` at all.
///
/// The defect it pins: 160 and 70 are the *single-message* limits. A concatenated SMS spends
/// six bytes per part on the UDH header, so real parts hold 153 (GSM-7) and 67 (UCS-2). The old
/// code divided the whole body by the single-message limit and therefore under-counted every
/// long message — always in the direction that flatters the bill.
///
/// Latent when found: every template in `notif.sms` after a full suite run was Latin at
/// 93–122 characters, one segment under either rule. Two ordinary changes trip it — a
/// configurable hospital-name prefix on every message, and PRD §2's Bangla templates, which
/// take the UCS-2 path.
/// </summary>
public class SmsSegmentTests
{
    // ---- the single-message / concatenated split (the defect) -----------------

    [Theory]
    // Latin, GSM-7: one message up to 160, then parts of 153.
    [InlineData(1, 1)]
    [InlineData(160, 1)]      // the last body that fits a single message
    [InlineData(161, 2)]      // concatenation begins
    [InlineData(306, 2)]      // 2 x 153 exactly
    [InlineData(307, 3)]      // the old rule said 2 — ceil(307/160)
    [InlineData(320, 3)]      // the brief's worked example: 3 in the world, 2 in the old code
    [InlineData(459, 3)]      // 3 x 153 exactly
    [InlineData(460, 4)]
    public void Latin_bodies_bill_at_160_then_153(int length, int expected) =>
        Assert.Equal(expected, SmsQueue.SegmentsFor(new string('a', length)));

    [Theory]
    // Bangla, UCS-2: one message up to 70, then parts of 67.
    [InlineData(1, 1)]
    [InlineData(70, 1)]       // still a single message — the brief's "68-70 diverges" is not so
    [InlineData(71, 2)]
    [InlineData(134, 2)]      // 2 x 67 exactly
    [InlineData(135, 3)]      // the old rule said 2 — ceil(135/70)
    [InlineData(201, 3)]
    [InlineData(210, 4)]      // the brief's worked example: 4 in the world, 3 in the old code
    public void Bangla_bodies_bill_at_70_then_67(int length, int expected) =>
        Assert.Equal(expected, SmsQueue.SegmentsFor(new string('ক', length)));   // ক

    /// <summary>The regression in one line: the old `ceil(n / limit)` is wrong exactly where a
    /// message stops being a single message.</summary>
    [Fact]
    public void A_long_body_costs_more_than_the_single_message_limit_suggests()
    {
        Assert.True(SmsQueue.SegmentsFor(new string('a', 320)) > (int)Math.Ceiling(320 / 160.0));
        Assert.True(SmsQueue.SegmentsFor(new string('ক', 210)) > (int)Math.Ceiling(210 / 70.0));
    }

    // ---- encoding is the GSM-7 alphabet, not `c > 127` ------------------------

    [Theory]
    [InlineData('é')]     // 0x05 in GSM-7 basic, but 233 > 127
    [InlineData('è')]
    [InlineData('Ü')]
    [InlineData('ñ')]
    [InlineData('£')]
    [InlineData('§')]
    [InlineData('Ω')]
    public void Gsm7_characters_above_127_do_not_force_ucs2(char c)
    {
        // 160 of them still fit one message; under `c > 127` they billed as UCS-2 at 70 -> 3.
        Assert.Equal(1, SmsQueue.SegmentsFor(new string(c, 160)));
    }

    [Theory]
    [InlineData('ক')]    // Bangla ka — PRD §2's stated future
    [InlineData('ç')]    // GSM-7 carries uppercase Ç and not the lowercase form
    [InlineData('中')]    // CJK
    [InlineData('Б')]    // Cyrillic
    public void Non_gsm7_characters_force_ucs2(char c) =>
        Assert.Equal(2, SmsQueue.SegmentsFor(new string(c, 71)));

    /// <summary>The escaped set bills as two characters and used to count as one.</summary>
    [Theory]
    [InlineData('{')]
    [InlineData('}')]
    [InlineData('[')]
    [InlineData(']')]
    [InlineData('~')]
    [InlineData('^')]
    [InlineData('\\')]
    [InlineData('|')]
    [InlineData('€')]
    public void Gsm7_escaped_characters_cost_two_septets(char c)
    {
        Assert.Equal(1, SmsQueue.SegmentsFor(new string(c, 80)));    // 160 septets — still one
        Assert.Equal(2, SmsQueue.SegmentsFor(new string(c, 81)));    // 162 septets — two now
    }

    // ---- degenerate input -----------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void An_empty_body_is_never_zero_segments(string body) =>
        Assert.Equal(1, SmsQueue.SegmentsFor(body));

    /// <summary>Every template the product ships is one segment — the guard that keeps the
    /// defect latent, pinned so a longer template is a visible change and not a silent cost.</summary>
    [Fact]
    public void The_shipped_templates_are_all_single_segment()
    {
        foreach (var body in SmsTemplates.Defaults.Values)
            Assert.Equal(1, SmsQueue.SegmentsFor(body));
    }
}

/// <summary>Spec 0032 M20: `HMS_SMS_MODE` is the whole live/simulation posture (edge 3).</summary>
public class SmsOptionsTests
{
    [Theory]
    [InlineData("live", false)]
    [InlineData("LIVE", false)]     // case-insensitive by contract
    [InlineData("Live", false)]
    [InlineData("simulated", true)]
    [InlineData("", true)]
    [InlineData(null, true)]
    [InlineData("liveish", true)]   // anything that is not exactly "live" stays simulated
    public void Only_the_exact_word_live_leaves_simulation(string? mode, bool simulated) =>
        Assert.Equal(simulated, SmsOptions.From(mode).Simulated);
}
