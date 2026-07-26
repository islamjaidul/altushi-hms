using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hms.Notifications.Data;

// notif.sms per 03 §8. Simulation mode (edge 3) renders the exact message with a SIMULATED stamp.

public static class SmsState
{
    public const string Queued = "queued";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Failed = "failed";
    /// <summary>Edge 24: no phone is a recorded outcome, never an error.</summary>
    public const string SkippedNoPhone = "skipped_no_phone";
}

public static class SmsEvent
{
    public const string Registration = "registration";
    public const string ReportReady = "report_ready";
    public const string Appointment = "appointment";
}

public class SmsMessage
{
    public long Id { get; set; }
    public long BranchId { get; set; }
    public required string Event { get; set; }         // registration|report_ready|appointment
    public string? Recipient { get; set; }             // null = patient had no phone (edge 24)
    public required string Body { get; set; }
    public string State { get; set; } = "queued";      // queued|sent|delivered|failed|skipped_no_phone
    public int Segments { get; set; } = 1;
    public bool Simulated { get; set; } = true;        // gateway wiring is I7 integration work
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public string? FailReason { get; set; }
}

public class NotifDbContext(DbContextOptions<NotifDbContext> options) : DbContext(options)
{
    public DbSet<SmsMessage> Sms => Set<SmsMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("notif");
        b.Entity<SmsMessage>(e =>
        {
            e.ToTable("sms");
            e.HasIndex(x => x.QueuedAt);
        });
    }
}

public class NotifDbContextFactory : IDesignTimeDbContextFactory<NotifDbContext>
{
    public NotifDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<NotifDbContext>()
            .UseNpgsql("Host=localhost;Database=hms;Username=postgres",
                o => o.MigrationsHistoryTable("__ef_migrations", "notif"))
            .UseSnakeCaseNamingConvention()
            .Options);
}
