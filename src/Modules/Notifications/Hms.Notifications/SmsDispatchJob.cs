using Hms.Notifications.Data;
using Hms.Kernel.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Notifications;

/// <summary>
/// Spec 0039 WP6 (AUD-M20-01): the drain. Before this class, only simulation ever marked a
/// message <c>sent</c> — flipping <c>HMS_SMS_MODE=live</c> silently stopped delivery, because
/// nothing read the queue. Each tick the worker claims queued messages one at a time with the
/// same lease-based FOR UPDATE SKIP LOCKED shape as <see cref="JobQueue"/> (no lock is held
/// across the HTTP call — the pushed-forward <c>next_attempt_at</c> is the lease), hands them to
/// the configured <see cref="ISmsGateway"/>, and records the verdict: <c>sent</c> only on
/// gateway success; <c>failed</c> carries a reason, after <see cref="MaxAttempts"/> attempts
/// with exponential backoff. Nothing is ever deleted — the tray log is permanent (§5 M20).
/// </summary>
public sealed class SmsDispatchJob(ISmsGateway gateway, TimeProvider clock) : IRecurringJob
{
    public string Name => "sms.dispatch";

    /// <summary>Same posture as <see cref="JobQueue.MaxAttempts"/> (04 §6): poison messages park
    /// as <c>failed</c> with their last reason rather than retrying forever.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Messages one tick will attempt — bounds the tick on the 2-vCPU box (§16).</summary>
    public const int BatchSize = 25;

    /// <summary>A claim in flight is invisible to other workers for this long; a crashed worker's
    /// claim simply comes due again.</summary>
    public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(2);

    private sealed record ClaimedSms(long Id, string Recipient, string Body, int RetryCount);

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var notif = services.GetRequiredService<NotifDbContext>();

        for (var n = 0; n < BatchSize && !ct.IsCancellationRequested; n++)
        {
            var now = clock.GetUtcNow();
            var lease = now + ClaimLease;

            // The worker serves every branch — raw SQL here is deliberate and carries no branch
            // predicate (WP5 filters guard operator queries, not the machine's own drain).
            // Skipped-no-phone rows are a recorded outcome (edge 24), never claimed.
            var claimed = (await notif.Database.SqlQuery<ClaimedSms>($"""
                UPDATE notif.sms SET retry_count = retry_count + 1, next_attempt_at = {lease}
                WHERE id = (
                    SELECT id FROM notif.sms
                    WHERE state = 'queued' AND recipient IS NOT NULL
                      AND (next_attempt_at IS NULL OR next_attempt_at <= {now})
                    ORDER BY id
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1)
                RETURNING id, recipient, body, retry_count
                """).ToListAsync(ct)).SingleOrDefault();
            if (claimed is null) break;   // queue drained — sleep until the next poll

            var verdict = await gateway.SendAsync(claimed.Recipient, claimed.Body, ct);
            var done = clock.GetUtcNow();

            if (verdict.Sent)
            {
                // Guarded on state: a message resent/decided elsewhere meanwhile is left alone.
                await notif.Database.ExecuteSqlAsync($"""
                    UPDATE notif.sms
                    SET state = 'sent', sent_at = {done}, fail_reason = NULL, next_attempt_at = NULL
                    WHERE id = {claimed.Id} AND state = 'queued'
                    """, ct);
            }
            else if (claimed.RetryCount >= MaxAttempts)
            {
                var reason = verdict.FailReason ?? "Gateway refused the message.";
                await notif.Database.ExecuteSqlAsync($"""
                    UPDATE notif.sms
                    SET state = 'failed', fail_reason = {reason}, next_attempt_at = NULL
                    WHERE id = {claimed.Id} AND state = 'queued'
                    """, ct);
            }
            else
            {
                // Exponential backoff: 2, 4, 8, 16 minutes between attempts.
                var retryAt = done + TimeSpan.FromMinutes(Math.Pow(2, claimed.RetryCount));
                var reason = verdict.FailReason ?? "Gateway refused the message.";
                await notif.Database.ExecuteSqlAsync($"""
                    UPDATE notif.sms
                    SET fail_reason = {reason}, next_attempt_at = {retryAt}
                    WHERE id = {claimed.Id} AND state = 'queued'
                    """, ct);
            }
        }
    }
}
