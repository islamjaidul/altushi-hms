using Hms.Kernel.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hms.Kernel.Jobs;

/// <summary>
/// Work the worker runs on every poll tick — a scan that decides for itself whether anything is
/// due (approval escalation, SMS drain, the daily scheduler). Implementations are resolved from
/// the tick's DI scope, receive that scope's provider, and must never assume a signed-in user:
/// there is no request, no cookie and no ambient branch.
/// </summary>
public interface IRecurringJob
{
    /// <summary>Stable name used in logs.</summary>
    string Name { get; }

    Task RunAsync(IServiceProvider services, CancellationToken ct);
}

/// <summary>Executes one queued <c>kernel.job</c> row of a given type (ADR-0002).</summary>
public interface IJobHandler
{
    /// <summary>The <c>kernel.job.type</c> value this handler owns.</summary>
    string Type { get; }

    Task ExecuteAsync(IServiceProvider services, string payloadJson, CancellationToken ct);
}

/// <summary>
/// Spec 0039 WP6 (AUD-XCUT-02): the background worker both hosts run. The codebase convention
/// "no BackgroundService in src/" is deliberately superseded by this class and only this class —
/// the audit's finding was that with no hosted service registered anywhere, nothing could ever
/// run in the background, so approval escalation, SMS delivery, due reminders and the M22
/// end-of-day digest were all dead letters. New background work registers an
/// <see cref="IRecurringJob"/> or <see cref="IJobHandler"/> with this worker; it does not add a
/// second BackgroundService.
/// <para>
/// Concurrency remains PostgreSQL's job, not the CLR's: queue claims go through
/// <see cref="JobQueue"/>'s FOR UPDATE SKIP LOCKED contract, so two instances of a host on one
/// database never double-run a queued job, and a crashed worker's claim expires by lease. A job
/// that throws is failed with backoff and can never take the host down: every job and every
/// tick has its own catch-all.
/// </para>
/// </summary>
public sealed class JobWorker(
    IServiceScopeFactory scopes,
    JobQueue queue,
    IConfiguration config,
    ILogger<JobWorker> log) : BackgroundService
{
    /// <summary>How many queued jobs one tick will execute before yielding to the next poll —
    /// keeps a flooded queue from pinning one of the box's two vCPUs (§16).</summary>
    public const int MaxJobsPerTick = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("Jobs:PollSeconds", 30)));
        var workerId = $"{Environment.MachineName}:{Environment.ProcessId}";
        log.LogInformation("Job worker {WorkerId} polling every {Poll}s", workerId, poll.TotalSeconds);

        // The first tick happens one interval after boot — migrations and seeding have finished
        // by then, and a host that fails to start should not have half-run a tick first.
        using var timer = new PeriodicTimer(poll);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await TickAsync(workerId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    // Never crash the host (AUD-XCUT-02 acceptance): log and try again next tick.
                    log.LogError(e, "Job worker tick failed; retrying on the next poll");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host shutdown — nothing to do; an in-flight claim expires by lease.
        }
    }

    /// <summary>One poll tick. Public so the integration tests drive the exact code the timer
    /// loop runs — the loop above is its only production caller.</summary>
    public async Task TickAsync(string workerId, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        // 1. Recurring scans — each isolated, so one failing consumer cannot starve the rest.
        foreach (var job in sp.GetServices<IRecurringJob>())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await job.RunAsync(sp, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                log.LogError(e, "Recurring job {Job} failed; the next tick retries", job.Name);
            }
        }

        // 2. Drain kernel.job through JobQueue's claim contract (FOR UPDATE SKIP LOCKED).
        var kernel = sp.GetRequiredService<KernelDbContext>();
        var handlers = sp.GetServices<IJobHandler>()
            .ToDictionary(h => h.Type, StringComparer.Ordinal);

        for (var n = 0; n < MaxJobsPerTick && !ct.IsCancellationRequested; n++)
        {
            var job = await queue.ClaimNextAsync(kernel, workerId, ct);
            if (job is null) break;   // queue empty — the worker sleeps until the next poll

            try
            {
                if (!handlers.TryGetValue(job.Type, out var handler))
                    throw new InvalidOperationException(
                        $"No handler is registered for job type '{job.Type}' on this host.");

                await handler.ExecuteAsync(sp, job.Payload, ct);
                await queue.CompleteAsync(kernel, job.Id, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown mid-job: leave the claim to expire by lease; the job re-runs.
                throw;
            }
            catch (Exception e)
            {
                log.LogError(e, "Job {JobId} ({Type}) failed on attempt; released with backoff",
                    job.Id, job.Type);
                try
                {
                    await queue.FailAsync(kernel, job.Id, e.Message, ct);
                }
                catch (Exception fail)
                {
                    // Even the failure write must not kill the worker; the lease will release it.
                    log.LogError(fail, "Could not record failure for job {JobId}", job.Id);
                }
            }
        }
    }
}
