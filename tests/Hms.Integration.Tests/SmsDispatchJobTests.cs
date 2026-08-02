using Hms.Notifications;
using Hms.Notifications.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0039 WP6 (AUD-M20-01): the drain's state machine. The invariants that fix the audit
/// finding: a queued live message reaches the gateway; <c>sent</c> happens ONLY on gateway
/// success; a failure keeps the message with a recorded reason and backoff; and the attempt cap
/// parks it as <c>failed</c> — never deleted, never silently stuck at <c>queued</c> forever.
/// </summary>
[Collection("postgres")]
public class SmsDispatchJobTests(PostgresFixture pg) : IAsyncLifetime
{
    private sealed class ScriptedGateway(Func<SmsSendResult> verdict) : ISmsGateway
    {
        public int Calls;
        public Task<SmsSendResult> SendAsync(string recipient, string body, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(verdict());
        }
    }

    public async Task InitializeAsync()
    {
        await using var notif = CreateNotif();
        await notif.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private NotifDbContext CreateNotif() => new(
        new DbContextOptionsBuilder<NotifDbContext>()
            .UseNpgsql(pg.ConnectionString, o => o.MigrationsHistoryTable("__ef_migrations", "notif"))
            .UseSnakeCaseNamingConvention().Options);

    private ServiceProvider BuildHost()
    {
        var services = new ServiceCollection();
        services.AddDbContext<NotifDbContext>(o => o
            .UseNpgsql(pg.ConnectionString, x => x.MigrationsHistoryTable("__ef_migrations", "notif"))
            .UseSnakeCaseNamingConvention());
        return services.BuildServiceProvider();
    }

    private async Task<long> QueueLiveAsync(string recipient, int retryCount = 0, long branchId = 1)
    {
        await using var notif = CreateNotif();
        var msg = new SmsMessage
        {
            BranchId = branchId,
            Event = SmsEvent.Registration,
            Recipient = recipient,
            Body = "test body",
            State = SmsState.Queued,
            Simulated = false,
            QueuedAt = DateTimeOffset.UtcNow,
            RetryCount = retryCount,
        };
        notif.Sms.Add(msg);
        await notif.SaveChangesAsync();
        return msg.Id;
    }

    private async Task<SmsMessage> ReadAsync(long id)
    {
        await using var notif = CreateNotif();
        return await notif.Sms.AsNoTracking().IgnoreQueryFilters().SingleAsync(m => m.Id == id);
    }

    [Fact]
    public async Task Gateway_success_marks_the_message_sent()
    {
        // Branch 9: the drain serves every branch, not the worker scope's default.
        var id = await QueueLiveAsync("01711111111", branchId: 9);
        var gateway = new ScriptedGateway(() => new SmsSendResult(true));
        await using var sp = BuildHost();

        await new SmsDispatchJob(gateway, TimeProvider.System).RunAsync(sp, CancellationToken.None);

        var msg = await ReadAsync(id);
        Assert.Equal(SmsState.Sent, msg.State);
        Assert.NotNull(msg.SentAt);
        Assert.Null(msg.FailReason);
        Assert.True(gateway.Calls >= 1);
    }

    [Fact]
    public async Task Gateway_failure_keeps_the_message_with_a_reason_and_backoff_never_sent()
    {
        var id = await QueueLiveAsync("01722222222");
        var gateway = new ScriptedGateway(() => new SmsSendResult(false, "Gateway answered HTTP 500."));
        await using var sp = BuildHost();
        var job = new SmsDispatchJob(gateway, TimeProvider.System);

        await job.RunAsync(sp, CancellationToken.None);

        var msg = await ReadAsync(id);
        Assert.Equal(SmsState.Queued, msg.State);        // kept, not lost, and NOT sent
        Assert.Null(msg.SentAt);
        Assert.Equal(1, msg.RetryCount);
        Assert.Contains("500", msg.FailReason);
        Assert.True(msg.NextAttemptAt > DateTimeOffset.UtcNow, "backoff must be in the future");

        // A second tick inside the backoff window must not attempt it again.
        var callsBefore = gateway.Calls;
        await job.RunAsync(sp, CancellationToken.None);
        Assert.Equal(callsBefore, gateway.Calls);
        Assert.Equal(1, (await ReadAsync(id)).RetryCount);
    }

    [Fact]
    public async Task The_attempt_cap_parks_the_message_as_failed_with_its_last_reason()
    {
        var id = await QueueLiveAsync("01733333333", retryCount: SmsDispatchJob.MaxAttempts - 1);
        var gateway = new ScriptedGateway(() => new SmsSendResult(false, "SIM balance exhausted"));
        await using var sp = BuildHost();

        await new SmsDispatchJob(gateway, TimeProvider.System).RunAsync(sp, CancellationToken.None);

        var msg = await ReadAsync(id);
        Assert.Equal(SmsState.Failed, msg.State);
        Assert.Contains("SIM balance", msg.FailReason);
        Assert.Null(msg.SentAt);
    }

    [Fact]
    public async Task Skipped_no_phone_rows_are_never_claimed()
    {
        long id;
        await using (var notif = CreateNotif())
        {
            var msg = new SmsMessage
            {
                BranchId = 1,
                Event = SmsEvent.Registration,
                Recipient = null,
                Body = "no phone",
                State = SmsState.SkippedNoPhone,
                Simulated = false,
                QueuedAt = DateTimeOffset.UtcNow,
            };
            notif.Sms.Add(msg);
            await notif.SaveChangesAsync();
            id = msg.Id;
        }
        var gateway = new ScriptedGateway(() => new SmsSendResult(true));
        await using var sp = BuildHost();

        await new SmsDispatchJob(gateway, TimeProvider.System).RunAsync(sp, CancellationToken.None);

        Assert.Equal(SmsState.SkippedNoPhone, (await ReadAsync(id)).State);
    }
}
