using Hms.Kernel.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Hms.Integration.Tests;

// ADR-0002: SKIP LOCKED drain — two workers never double-claim; poison jobs park.
[Collection("postgres")]
public class JobQueueTests(PostgresFixture pg)
{
    private readonly JobQueue _queue = new(TimeProvider.System);

    private async Task<long[]> EnqueueAsync(params string[] types)
    {
        await using var db = pg.CreateKernelContext();
        foreach (var t in types) _queue.Enqueue(db, t, "{}");
        await db.SaveChangesAsync();
        return await db.Jobs.Where(j => types.Contains(j.Type)).Select(j => j.Id).ToArrayAsync();
    }

    [Fact]
    public async Task Concurrent_claims_never_take_the_same_job()
    {
        await EnqueueAsync("sms.a", "sms.b");

        // Two claimants inside open transactions: SKIP LOCKED must hand out distinct jobs.
        await using var db1 = pg.CreateKernelContext();
        await using var db2 = pg.CreateKernelContext();
        await using var tx1 = await db1.Database.BeginTransactionAsync();
        await using var tx2 = await db2.Database.BeginTransactionAsync();

        var a = await _queue.ClaimNextAsync(db1, "worker-1");
        var b = await _queue.ClaimNextAsync(db2, "worker-2");

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a!.Id, b!.Id);

        await _queue.CompleteAsync(db1, a.Id);
        await _queue.CompleteAsync(db2, b.Id);
        await tx1.CommitAsync();
        await tx2.CommitAsync();
    }

    [Fact]
    public async Task Completed_jobs_are_not_reclaimed()
    {
        var ids = await EnqueueAsync("report.ready");
        await using var db = pg.CreateKernelContext();
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var job = await _queue.ClaimNextAsync(db, "w");
            Assert.NotNull(job);
            await _queue.CompleteAsync(db, job!.Id);
            await tx.CommitAsync();
        }

        var done = await db.Jobs.SingleAsync(j => j.Id == ids[0]);
        Assert.NotNull(done.DoneAt);
    }

    [Fact]
    public async Task Failure_releases_with_backoff_and_parks_poison()
    {
        var ids = await EnqueueAsync("gateway.flaky");
        await using var db = pg.CreateKernelContext();

        for (var attempt = 1; attempt <= JobQueue.MaxAttempts; attempt++)
        {
            // make it due again despite the backoff, so each loop can claim it
            await db.Database.ExecuteSqlAsync(
                $"UPDATE kernel.job SET run_after = now() - interval '1 second' WHERE id = {ids[0]} AND run_after < 'infinity'");
            await using var tx = await db.Database.BeginTransactionAsync();
            var job = await _queue.ClaimNextAsync(db, "w");
            Assert.NotNull(job);
            await _queue.FailAsync(db, job!.Id, $"boom {attempt}");
            await tx.CommitAsync();
        }

        var parked = await db.Jobs.SingleAsync(j => j.Id == ids[0]);
        Assert.Equal(JobQueue.MaxAttempts, parked.Attempts);
        Assert.Equal(DateTimeOffset.MaxValue, parked.RunAfter);      // 'infinity' = parked (admin alert path)
        Assert.Null(await _queue.ClaimNextAsync(db, "w2"));          // nothing claimable
    }
}
