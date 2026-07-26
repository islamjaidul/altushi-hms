using Hms.Kernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Hms.Kernel.Jobs;

public sealed record ClaimedJob(long Id, string Type, string Payload);

/// <summary>
/// ADR-0002: the job queue is a Postgres table drained with FOR UPDATE SKIP LOCKED —
/// multiple workers never double-claim, and a crashed worker's lock expires by lease timeout.
/// </summary>
public sealed class JobQueue(TimeProvider clock)
{
    public static readonly TimeSpan LockLease = TimeSpan.FromMinutes(5);

    public void Enqueue(KernelDbContext db, string type, string payloadJson, DateTimeOffset? runAfter = null)
        => db.Jobs.Add(new Job
        {
            Type = type,
            Payload = payloadJson,
            RunAfter = runAfter ?? clock.GetUtcNow(),
        });

    /// <summary>Claims at most one due job; returns null when the queue is empty (worker sleeps).</summary>
    public async Task<ClaimedJob?> ClaimNextAsync(KernelDbContext db, string workerId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var staleBefore = now - LockLease;
        var rows = await db.Database.SqlQuery<ClaimedJob>($"""
            UPDATE kernel.job SET locked_by = {workerId}, locked_at = {now}, attempts = attempts + 1
            WHERE id = (
                SELECT id FROM kernel.job
                WHERE done_at IS NULL
                  AND run_after <= {now}
                  AND (locked_by IS NULL OR locked_at < {staleBefore})
                ORDER BY id
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            RETURNING id AS "Id", type AS "Type", payload AS "Payload"
            """).ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    public Task CompleteAsync(KernelDbContext db, long jobId, CancellationToken ct = default)
        => db.Database.ExecuteSqlAsync(
            $"UPDATE kernel.job SET done_at = {clock.GetUtcNow()}, error = NULL WHERE id = {jobId}", ct);

    /// <summary>Failure releases the claim with backoff; poison jobs park after MaxAttempts (04 §6).</summary>
    public const int MaxAttempts = 5;

    public Task FailAsync(KernelDbContext db, long jobId, string error, CancellationToken ct = default)
    {
        var retryAt = clock.GetUtcNow() + TimeSpan.FromMinutes(1);
        return db.Database.ExecuteSqlAsync($"""
            UPDATE kernel.job SET
                error = {error},
                locked_by = NULL, locked_at = NULL,
                run_after = CASE WHEN attempts >= {MaxAttempts} THEN 'infinity'::timestamptz ELSE {retryAt} END
            WHERE id = {jobId}
            """, ct);
    }
}
