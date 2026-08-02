using Hms.Kernel.Data;
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
    /// <summary>§5 M20 [M] trigger "due reminder", fired by the background worker (spec 0039
    /// WP6). Fires only once its template exists — see the TODO on SmsTemplates.Defaults.</summary>
    public const string DueReminder = "due_reminder";
    /// <summary>§5 M22 [M] "end-of-day insights summary … optionally SMS'd to MD", fired by the
    /// background worker (spec 0039 WP6). Same template gate as DueReminder.</summary>
    public const string EodDigest = "eod_digest";
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
    /// <summary>Spec 0039 WP6 (AUD-M20-01): delivery attempts made by the drain loop. At
    /// <see cref="SmsDispatchJob.MaxAttempts"/> the message goes <c>failed</c> for good.</summary>
    public int RetryCount { get; set; }
    /// <summary>Backoff: the drain skips this message until the instant passes. Null = due now.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
}

public class NotifDbContext(DbContextOptions<NotifDbContext> options) : DbContext(options)
{

    /// <summary>The branch this context's queries are isolated to (spec 0039 WP5). Captured
    /// from the ambient request scope at construction; every entity carrying a BranchId is
    /// filtered to it structurally — see BranchIsolation.</summary>
    public long CurrentBranch { get; set; } = Hms.Kernel.Data.BranchScope.Current;
    public DbSet<SmsMessage> Sms => Set<SmsMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Spec 0039 WP2: the state CHECK from SmsState above and HasMaxLength — same posture
        // as BillDbContext (additive only, NOT VALID in the migration for pre-existing tables).
        b.HasDefaultSchema("notif");
        b.Entity<SmsMessage>(e =>
        {
            e.ToTable("sms", t => t.HasCheckConstraint("ck_sms_state",
                "state IN ('queued','sent','delivered','failed','skipped_no_phone')"));
            e.Property(x => x.Event).HasMaxLength(40);
            e.Property(x => x.Recipient).HasMaxLength(20);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.State).HasMaxLength(40);
            e.Property(x => x.FailReason).HasMaxLength(4000);
            e.HasIndex(x => x.QueuedAt);
            e.HasIndex(x => x.State);          // WP6: the drain's scan is by state

        });
        b.ApplyBranchIsolation(this);   // WP5: branch predicate as structure (AUD-ARCH-01)
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
