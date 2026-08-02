using System.Text.Json;
using Hms.Kernel.Data;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Kernel.Jobs;

/// <summary>Payload for the daily jobs: the business day the job is about, ISO yyyy-MM-dd.</summary>
public sealed record DailyJobPayload(string Day);

/// <summary>
/// Spec 0039 WP6 (AUD-XCUT-02): watches for the business-day rollover (P2 boundary, Asia/Dhaka)
/// and enqueues the daily jobs exactly once per day, however many workers poll. The
/// compare-and-set on <c>kernel.setting</c> is the race arbiter: only the worker that flips the
/// stored day enqueues. First boot records the day and enqueues nothing — deploying must not
/// blast a digest for a day nobody asked about. Registered in the ERP host only: the job types
/// it enqueues are handled by the notification handlers that SKU ships.
/// </summary>
public sealed class DailyJobScheduler(
    JobQueue queue, BusinessDayCalendar calendar, TimeProvider clock) : IRecurringJob
{
    public const string SettingKey = "jobs.daily.last_run";
    public const string DueRemindersJobType = "notif.due_reminders";
    public const string EodDigestJobType = "notif.eod_digest";

    public string Name => "daily.scheduler";

    public async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        var kernel = services.GetRequiredService<KernelDbContext>();
        var today = calendar.BusinessDayOf(clock.GetUtcNow());
        var todayText = today.ToString("yyyy-MM-dd");

        var last = await kernel.Settings.AsNoTracking()
            .Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct);

        if (last is null)
        {
            await kernel.Database.ExecuteSqlAsync($"""
                INSERT INTO kernel.setting (key, value) VALUES ({SettingKey}, to_jsonb({todayText}::text))
                ON CONFLICT (key) DO NOTHING
                """, ct);
            return;
        }

        var lastDay = JsonSerializer.Deserialize<string>(last);
        if (lastDay == todayText) return;

        // Compare-and-set: the loser of a concurrent rollover changes zero rows and enqueues nothing.
        var won = await kernel.Database.ExecuteSqlAsync($"""
            UPDATE kernel.setting SET value = to_jsonb({todayText}::text)
            WHERE key = {SettingKey} AND value = to_jsonb({lastDay}::text)
            """, ct);
        if (won == 0) return;

        // The digest reports the day that just closed; the reminders run for the new day.
        var closedDay = today.AddDays(-1).ToString("yyyy-MM-dd");
        queue.Enqueue(kernel, EodDigestJobType, JsonSerializer.Serialize(new DailyJobPayload(closedDay)));
        queue.Enqueue(kernel, DueRemindersJobType, JsonSerializer.Serialize(new DailyJobPayload(todayText)));
        await kernel.SaveChangesAsync(ct);
    }
}
