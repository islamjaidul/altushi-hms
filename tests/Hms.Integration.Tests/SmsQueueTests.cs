using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Notifications;
using Hms.Notifications.Data;
using Hms.Registration;
using Hms.Registration.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0032 M20. Before this file `grep -rn "SmsQueue" tests/` returned nothing: the whole
/// notification module — the thing that talks to patients — had no test anywhere.
///
/// The rule worth a real test is G19. `SmsQueue.Queue` stages the message on the <em>caller's</em>
/// context and never saves it itself, precisely so the SMS commits with the business fact that
/// caused it. A registration that rolls back must leave no SMS behind: the alternative is a
/// patient thanked by name for a registration that does not exist.
/// </summary>
[Collection("postgres")]
public class SmsQueueTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RegistrationService _registration = new(
        new NumberSeriesService(), new AuditWriter(TimeProvider.System), TimeProvider.System);

    public SmsQueueTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var reg = CreateReg(null);
        await reg.Database.MigrateAsync();
        await using var notif = CreateNotif(null);
        await notif.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static SmsQueue Simulated() => new(TimeProvider.System, SmsOptions.From(null));
    private static SmsQueue Live() => new(TimeProvider.System, SmsOptions.From("live"));

    private NotifDbContext CreateNotif(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<NotifDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "notif"))
            : new DbContextOptionsBuilder<NotifDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "notif")))
        .UseSnakeCaseNamingConvention().Options);

    private RegDbContext CreateReg(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<RegDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "reg"))
            : new DbContextOptionsBuilder<RegDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "reg")))
        .UseSnakeCaseNamingConvention().Options);

    private KernelDbContext CreateKernel(NpgsqlConnection conn) => new(
        new DbContextOptionsBuilder<KernelDbContext>().UseNpgsql(conn,
            o => o.MigrationsHistoryTable("__ef_migrations", "kernel"))
        .UseSnakeCaseNamingConvention().Options);

    // ---- G19: the SMS commits with the fact that caused it --------------------

    /// <summary>
    /// The registration and its welcome SMS are one transaction. Roll it back and BOTH are gone —
    /// this is the assertion the module's own summary comment claims and nothing checked.
    /// </summary>
    [Fact]
    public async Task A_rolled_back_registration_leaves_no_sms_behind()
    {
        var marker = $"rollback-{Guid.NewGuid():N}";

        await using (var conn = new NpgsqlConnection(_pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using var reg = CreateReg(conn);
            await using var kernel = CreateKernel(conn);
            await using var notif = CreateNotif(conn);
            await reg.Database.UseTransactionAsync(tx);
            await kernel.Database.UseTransactionAsync(tx);
            await notif.Database.UseTransactionAsync(tx);

            var patient = await _registration.RegisterAsync(reg, kernel, new RegisterPatientCommand(
                1, "Rollback Rahman", 'M', null, 40, null, false, "01711-000111",
                null, "Sylhet", null, null, "general", false, 7, "Jashim"));

            Simulated().Queue(notif, 1, SmsEvent.Registration, patient.Phone,
                $"{marker} welcome {patient.Uhid}");
            await notif.SaveChangesAsync();

            // Everything above succeeded and is visible inside the transaction...
            Assert.True(await notif.Sms.AnyAsync(m => m.Body.StartsWith(marker)));

            await tx.RollbackAsync();       // ...and then the business fact does not happen.
        }

        // Fresh connections: nothing survived, on either side.
        await using var freshNotif = CreateNotif(null);
        await using var freshReg = CreateReg(null);
        Assert.False(await freshNotif.Sms.AnyAsync(m => m.Body.StartsWith(marker)));
        Assert.False(await freshReg.Patients.AnyAsync(p => p.FullName == "Rollback Rahman"));
    }

    /// <summary>The other half of G19: when the registration commits, so does its SMS.</summary>
    [Fact]
    public async Task A_committed_registration_leaves_exactly_one_sms()
    {
        var marker = $"commit-{Guid.NewGuid():N}";
        string uhid;

        await using (var conn = new NpgsqlConnection(_pg.ConnectionString))
        {
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            await using var reg = CreateReg(conn);
            await using var kernel = CreateKernel(conn);
            await using var notif = CreateNotif(conn);
            await reg.Database.UseTransactionAsync(tx);
            await kernel.Database.UseTransactionAsync(tx);
            await notif.Database.UseTransactionAsync(tx);

            var patient = await _registration.RegisterAsync(reg, kernel, new RegisterPatientCommand(
                1, "Commit Karim", 'M', null, 41, null, false, "01711-000222",
                null, "Sylhet", null, null, "general", false, 7, "Jashim"));
            uhid = patient.Uhid;

            Simulated().Queue(notif, 1, SmsEvent.Registration, patient.Phone,
                $"{marker} welcome {patient.Uhid}");
            await notif.SaveChangesAsync();
            await tx.CommitAsync();
        }

        await using var fresh = CreateNotif(null);
        var saved = Assert.Single(await fresh.Sms.Where(m => m.Body.StartsWith(marker)).ToListAsync());
        Assert.Equal(SmsEvent.Registration, saved.Event);
        Assert.Equal("01711-000222", saved.Recipient);
        Assert.Contains(uhid, saved.Body);
    }

    // ---- edge 24: no phone is a recorded skip, not a failure ------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_patient_with_no_phone_is_a_recorded_skip(string? phone)
    {
        var marker = $"skip-{Guid.NewGuid():N}";
        await using var notif = CreateNotif(null);

        var msg = Simulated().Queue(notif, 1, SmsEvent.Registration, phone, $"{marker} welcome");
        await notif.SaveChangesAsync();

        // It is a row, not an exception and not a silence: the hospital can count who it missed.
        Assert.Equal(SmsState.SkippedNoPhone, msg.State);
        Assert.Null(msg.Recipient);
        Assert.Null(msg.SentAt);

        await using var fresh = CreateNotif(null);
        var saved = await fresh.Sms.SingleAsync(m => m.Body.StartsWith(marker));
        Assert.Equal(SmsState.SkippedNoPhone, saved.State);
        Assert.Null(saved.Recipient);
    }

    [Fact]
    public async Task A_recipient_is_trimmed_and_a_real_number_is_sent_in_simulation()
    {
        var marker = $"sent-{Guid.NewGuid():N}";
        await using var notif = CreateNotif(null);

        var msg = Simulated().Queue(notif, 1, SmsEvent.Appointment, "  01711-000333  ", $"{marker} serial 4");
        await notif.SaveChangesAsync();

        Assert.Equal("01711-000333", msg.Recipient);
        Assert.Equal(SmsState.Sent, msg.State);     // simulation: the tray IS the gateway
        Assert.Equal(msg.QueuedAt, msg.SentAt);
        Assert.True(msg.Simulated);
    }

    /// <summary>Live posture stops at `queued` — nothing may claim "sent" until a gateway says so.</summary>
    [Fact]
    public async Task A_live_gateway_leaves_the_message_queued_not_sent()
    {
        var marker = $"live-{Guid.NewGuid():N}";
        await using var notif = CreateNotif(null);

        var msg = Live().Queue(notif, 1, SmsEvent.ReportReady, "01711-000444", $"{marker} report ready");
        await notif.SaveChangesAsync();

        Assert.Equal(SmsState.Queued, msg.State);
        Assert.Null(msg.SentAt);
        Assert.False(msg.Simulated);
    }

    // ---- §5 M20 [M]: resend re-queues, never rewrites --------------------------

    /// <summary>
    /// The operator resends; they never edit. A resend that re-rendered the template would send
    /// today's wording for last week's fact — the log would stop being what the patient received.
    /// </summary>
    [Fact]
    public async Task Resend_requeues_the_message_unchanged()
    {
        var marker = $"resend-{Guid.NewGuid():N}";
        await using var notif = CreateNotif(null);
        var queue = Simulated();

        var original = queue.Queue(notif, 1, SmsEvent.Registration, "01711-000555",
            $"{marker} Dear Rahim, your UHID is UH-000123.");
        await notif.SaveChangesAsync();

        var copy = queue.Resend(notif, original);
        await notif.SaveChangesAsync();

        Assert.NotEqual(original.Id, copy.Id);              // a new row, not a mutation
        Assert.Equal(original.Body, copy.Body);             // the same words
        Assert.Equal(original.Recipient, copy.Recipient);
        Assert.Equal(original.Event, copy.Event);
        Assert.Equal(original.BranchId, copy.BranchId);
        Assert.Equal(original.Segments, copy.Segments);

        // The original is untouched — the log is append-only, so history keeps both attempts.
        await using var fresh = CreateNotif(null);
        var rows = await fresh.Sms.Where(m => m.Body.StartsWith(marker))
            .OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(rows[0].Body, rows[1].Body);
    }

    /// <summary>A skipped message resends as a skip: resend must not invent a recipient.</summary>
    [Fact]
    public async Task Resending_a_skipped_message_stays_a_skip()
    {
        var marker = $"resend-skip-{Guid.NewGuid():N}";
        await using var notif = CreateNotif(null);
        var queue = Simulated();

        var original = queue.Queue(notif, 1, SmsEvent.Registration, null, $"{marker} welcome");
        await notif.SaveChangesAsync();
        var copy = queue.Resend(notif, original);
        await notif.SaveChangesAsync();

        Assert.Equal(SmsState.SkippedNoPhone, copy.State);
        Assert.Null(copy.Recipient);
    }

    /// <summary>The segment count is stamped on the row at queue time, so the tray's spend tile
    /// reads what was actually billed rather than recomputing it under a later rule.</summary>
    [Fact]
    public async Task The_stored_segment_count_matches_the_body_it_was_stamped_from()
    {
        var marker = $"seg-{Guid.NewGuid():N}";
        await using var notif = CreateNotif(null);

        var body = marker + new string('a', 320);
        var msg = Simulated().Queue(notif, 1, SmsEvent.Registration, "01711-000666", body);
        await notif.SaveChangesAsync();

        await using var fresh = CreateNotif(null);
        var saved = await fresh.Sms.SingleAsync(m => m.Id == msg.Id);
        Assert.Equal(SmsQueue.SegmentsFor(body), saved.Segments);
        Assert.True(saved.Segments >= 3);        // >320 Latin characters is three parts, not two
    }
}
