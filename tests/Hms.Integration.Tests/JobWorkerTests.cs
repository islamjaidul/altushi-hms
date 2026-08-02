using System.Text.Json;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0039 WP6 (AUD-XCUT-02): the worker's tick — the audit's finding was that nothing ever
/// drained kernel.job and nothing acted on EscalationMinutes. These tests drive the exact tick
/// the timer loop runs: queued jobs reach their handler and complete; a failing job is released
/// with backoff instead of killing the tick; and a pending approval that outlives its tier's
/// window is escalated once, audit-logged, across every branch.
/// </summary>
[Collection("postgres")]
public class JobWorkerTests(PostgresFixture pg)
{
    private sealed class RecordingHandler : IJobHandler
    {
        public readonly List<string> Payloads = [];
        public string Type => "test.echo";
        public Task ExecuteAsync(IServiceProvider services, string payloadJson, CancellationToken ct)
        {
            Payloads.Add(payloadJson);
            return Task.CompletedTask;
        }
    }

    private sealed class BoomHandler : IJobHandler
    {
        public string Type => "test.boom";
        public Task ExecuteAsync(IServiceProvider services, string payloadJson, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    private ServiceProvider BuildHost(params IJobHandler[] handlers)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<JobQueue>();
        services.AddSingleton(new AuditWriter(TimeProvider.System));
        services.AddSingleton<IRecurringJob, ApprovalEscalationJob>();
        foreach (var h in handlers) services.AddSingleton(h);
        services.AddDbContext<KernelDbContext>(o => o
            .UseNpgsql(pg.ConnectionString, x => x.MigrationsHistoryTable("__ef_migrations", "kernel"))
            .UseSnakeCaseNamingConvention());
        return services.BuildServiceProvider();
    }

    private static JobWorker Worker(ServiceProvider sp) => new(
        sp.GetRequiredService<IServiceScopeFactory>(),
        sp.GetRequiredService<JobQueue>(),
        sp.GetRequiredService<IConfiguration>(),
        NullLogger<JobWorker>.Instance);

    [Fact]
    public async Task A_queued_job_reaches_its_handler_and_completes()
    {
        var echo = new RecordingHandler();
        await using var sp = BuildHost(echo);

        long jobId;
        await using (var db = pg.CreateKernelContext())
        {
            sp.GetRequiredService<JobQueue>().Enqueue(db, "test.echo", """{"n":1}""");
            await db.SaveChangesAsync();
            jobId = await db.Jobs.Where(j => j.Type == "test.echo" && j.DoneAt == null)
                .Select(j => j.Id).OrderByDescending(id => id).FirstAsync();
        }

        await Worker(sp).TickAsync("t-worker", CancellationToken.None);

        // jsonb round-trips with its own whitespace; compare the content, not the formatting.
        Assert.Contains(echo.Payloads, p => p.Replace(" ", "") == """{"n":1}""");
        await using var check = pg.CreateKernelContext();
        var job = await check.Jobs.SingleAsync(j => j.Id == jobId);
        Assert.NotNull(job.DoneAt);
        Assert.Null(job.Error);
    }

    [Fact]
    public async Task A_throwing_job_is_released_with_its_error_and_the_tick_survives()
    {
        var echo = new RecordingHandler();
        await using var sp = BuildHost(echo, new BoomHandler());

        long boomId;
        await using (var db = pg.CreateKernelContext())
        {
            var queue = sp.GetRequiredService<JobQueue>();
            queue.Enqueue(db, "test.boom", "{}");
            queue.Enqueue(db, "test.echo", """{"after":"boom"}""");
            await db.SaveChangesAsync();
            boomId = await db.Jobs.Where(j => j.Type == "test.boom" && j.DoneAt == null)
                .Select(j => j.Id).OrderByDescending(id => id).FirstAsync();
        }

        await Worker(sp).TickAsync("t-worker", CancellationToken.None);

        await using var check = pg.CreateKernelContext();
        var boom = await check.Jobs.SingleAsync(j => j.Id == boomId);
        Assert.Null(boom.DoneAt);                       // failed, not silently completed
        Assert.Contains("boom", boom.Error);
        // The job after the poison one still ran in the same tick.
        Assert.Contains(echo.Payloads, p => p.Contains("after"));
    }

    [Fact]
    public async Task A_job_with_no_handler_on_this_host_records_that_as_its_error()
    {
        await using var sp = BuildHost();
        long id;
        await using (var db = pg.CreateKernelContext())
        {
            sp.GetRequiredService<JobQueue>().Enqueue(db, "test.unknown", "{}");
            await db.SaveChangesAsync();
            id = await db.Jobs.Where(j => j.Type == "test.unknown")
                .Select(j => j.Id).OrderByDescending(x => x).FirstAsync();
        }

        await Worker(sp).TickAsync("t-worker", CancellationToken.None);

        await using var check = pg.CreateKernelContext();
        var job = await check.Jobs.SingleAsync(j => j.Id == id);
        Assert.Null(job.DoneAt);
        Assert.Contains("No handler", job.Error);
    }

    // ------------------------------------------------------------------ escalation (P4)

    private static string Snapshot(params (int Tier, string Role, int EscalationMinutes)[] chain)
        => JsonSerializer.Serialize(new
        {
            requesterRole = "counter",
            threshold = (long?)null,
            chain = chain.Select(c => new { c.Tier, c.Role, c.EscalationMinutes }),
        });

    private async Task<long> RaiseAsync(
        DateTimeOffset requestedAt, string snapshot, long branchId = 1, string state = "pending",
        int? escalatedTier = null)
    {
        await using var db = pg.CreateKernelContext();
        var request = new ApprovalRequest
        {
            BranchId = branchId,
            Type = "discount",
            SourceTable = "bill.invoice",
            SourceId = 1,
            RequestedBy = 1,
            Reason = "escalation test",
            RequestedAt = requestedAt,
            State = state,
            ThresholdSnapshot = snapshot,
            EscalatedTier = escalatedTier,
        };
        db.ApprovalRequests.Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    [Fact]
    public async Task A_pending_request_older_than_its_window_escalates_to_the_next_tier_and_is_audited()
    {
        // Branch 7, not the worker's default scope: the scan must serve every branch.
        var id = await RaiseAsync(
            DateTimeOffset.UtcNow.AddMinutes(-30),
            Snapshot((1, "supervisor", 10), (2, "md", 60)),
            branchId: 7);

        await using var sp = BuildHost();
        await Worker(sp).TickAsync("t-worker", CancellationToken.None);

        await using var db = pg.CreateKernelContext();
        var request = await db.ApprovalRequests.IgnoreQueryFilters().SingleAsync(r => r.Id == id);
        Assert.Equal(2, request.EscalatedTier);
        Assert.NotNull(request.EscalatedAt);
        Assert.Equal("pending", request.State);          // escalated, not decided

        var audit = await db.AuditEvents.IgnoreQueryFilters()
            .Where(a => a.Action == "approval.escalate" && a.EntityId == id).SingleAsync();
        Assert.Equal(7, audit.BranchId);
        Assert.Contains("toTier", audit.After);   // jsonb reformats; the tier itself is asserted above
    }

    [Fact]
    public async Task The_top_tier_holds_and_a_fresh_request_waits()
    {
        var top = await RaiseAsync(
            DateTimeOffset.UtcNow.AddHours(-5),
            Snapshot((1, "supervisor", 10), (2, "md", 60)),
            escalatedTier: 2);                            // already at the top of its chain
        var fresh = await RaiseAsync(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            Snapshot((1, "supervisor", 10), (2, "md", 60)));

        await using var sp = BuildHost();
        await Worker(sp).TickAsync("t-worker", CancellationToken.None);

        await using var db = pg.CreateKernelContext();
        Assert.Equal(2, (await db.ApprovalRequests.IgnoreQueryFilters().SingleAsync(r => r.Id == top)).EscalatedTier);
        Assert.Null((await db.ApprovalRequests.IgnoreQueryFilters().SingleAsync(r => r.Id == fresh)).EscalatedTier);
    }

    // ------------------------------------------------------------------ daily rollover

    [Fact]
    public async Task The_daily_scheduler_enqueues_exactly_once_per_rollover()
    {
        var calendar = new Hms.Kernel.Time.BusinessDayCalendar(TimeOnly.MinValue);
        var queue = new JobQueue(TimeProvider.System);
        var scheduler = new DailyJobScheduler(queue, calendar, TimeProvider.System);
        await using var sp = BuildHost();
        var today = calendar.BusinessDayOf(DateTimeOffset.UtcNow);

        // A clean slate for the marker (config row, not financial data).
        await using (var db = pg.CreateKernelContext())
            await db.Database.ExecuteSqlAsync(
                $"DELETE FROM kernel.setting WHERE key = {DailyJobScheduler.SettingKey}");

        // First boot: the day is recorded and nothing is enqueued — no surprise digest blast.
        using (var scope = sp.CreateScope())
            await scheduler.RunAsync(scope.ServiceProvider, CancellationToken.None);
        await using (var db = pg.CreateKernelContext())
        {
            Assert.Equal(0, await db.Jobs.CountAsync(j => j.Type == DailyJobScheduler.EodDigestJobType));
            Assert.Contains(today.ToString("yyyy-MM-dd"),
                await db.Settings.Where(s => s.Key == DailyJobScheduler.SettingKey)
                    .Select(s => s.Value).SingleAsync());
        }

        // Simulate the rollover: the stored day is yesterday.
        var yesterday = today.AddDays(-1).ToString("yyyy-MM-dd");
        await using (var db = pg.CreateKernelContext())
            await db.Database.ExecuteSqlAsync($"""
                UPDATE kernel.setting SET value = to_jsonb({yesterday}::text)
                WHERE key = {DailyJobScheduler.SettingKey}
                """);

        // The tick that crosses the boundary enqueues both daily jobs; the next tick enqueues
        // nothing more — the compare-and-set is the dedupe.
        for (var tick = 0; tick < 2; tick++)
            using (var scope = sp.CreateScope())
                await scheduler.RunAsync(scope.ServiceProvider, CancellationToken.None);

        await using (var check = pg.CreateKernelContext())
        {
            var digests = await check.Jobs
                .Where(j => j.Type == DailyJobScheduler.EodDigestJobType).ToListAsync();
            var reminders = await check.Jobs
                .Where(j => j.Type == DailyJobScheduler.DueRemindersJobType).ToListAsync();
            Assert.Single(digests);
            Assert.Single(reminders);
            Assert.Contains(yesterday, digests[0].Payload);              // the day that closed
            Assert.Contains(today.ToString("yyyy-MM-dd"), reminders[0].Payload);
        }
    }

    [Fact]
    public async Task A_decided_request_never_escalates()
    {
        var id = await RaiseAsync(
            DateTimeOffset.UtcNow.AddHours(-5),
            Snapshot((1, "supervisor", 10), (2, "md", 60)),
            state: "approved");

        await using var sp = BuildHost();
        await Worker(sp).TickAsync("t-worker", CancellationToken.None);

        await using var db = pg.CreateKernelContext();
        Assert.Null((await db.ApprovalRequests.IgnoreQueryFilters().SingleAsync(r => r.Id == id)).EscalatedTier);
    }
}
