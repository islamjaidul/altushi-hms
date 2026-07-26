using Hms.Billing;
using Hms.Billing.Data;
using Hms.Diagnostics;
using Hms.Diagnostics.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>The §9A.2 order→billing seam: order → unbilled charges → invoice → pay → outbox event.</summary>
[Collection("postgres")]
public class DiagnosticsOrderTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly BillingService _billing = new(
        new NumberSeriesService(), new AuditWriter(TimeProvider.System),
        new FiscalCalendar(7), new BusinessDayCalendar(TimeOnly.MinValue), TimeProvider.System);

    public DiagnosticsOrderTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var diag = CreateDiag(null);
        await diag.Database.MigrateAsync();
        await using var bill = CreateBill(null);
        await bill.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private DiagDbContext CreateDiag(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<DiagDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "diag"))
            : new DbContextOptionsBuilder<DiagDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "diag")))
        .UseSnakeCaseNamingConvention().Options);

    private BillDbContext CreateBill(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<BillDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "bill"))
            : new DbContextOptionsBuilder<BillDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "bill")))
        .UseSnakeCaseNamingConvention().Options);

    private KernelDbContext CreateKernel(NpgsqlConnection conn) => new(
        new DbContextOptionsBuilder<KernelDbContext>().UseNpgsql(conn,
            o => o.MigrationsHistoryTable("__ef_migrations", "kernel"))
        .UseSnakeCaseNamingConvention().Options);

    [Fact]
    public async Task Order_to_paid_full_seam_in_transactions()
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();

        // --- tx 1: order raised anywhere → unbilled charges appear (the demo seam) ---
        long orderId, encounterId, sessionId;
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await using var bill = CreateBill(conn);
            await using var diag = CreateDiag(conn);
            await bill.Database.UseTransactionAsync(tx);
            await diag.Database.UseTransactionAsync(tx);

            var counter = new Counter { BranchId = 1, Name = "DX-" + Guid.NewGuid().ToString("N")[..6], Kind = "diagnostics" };
            bill.Counters.Add(counter);
            await bill.SaveChangesAsync();
            var session = await _billing.OpenSessionAsync(bill, 1, counter.Id, 7, 1000);
            sessionId = session.Id;
            var enc = new Encounter
            {
                BranchId = 1, PatientId = 1, OnDate = new DateOnly(2026, 7, 26), Type = "OPD",
                CounterId = counter.Id, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 7,
            };
            bill.Encounters.Add(enc);
            await bill.SaveChangesAsync();
            encounterId = enc.Id;

            var svc = new DiagnosticsService(new ChargePoster(bill, _billing), TimeProvider.System);
            var order = await svc.CreateOrderAsync(diag, 1, 1, enc.Id,
                [new OrderedTest(501, "CBC", 240, 400, 1), new OrderedTest(502, "Lipid Profile", 1440, 1200, 2)],
                orderingDoctorId: 11, referrerId: 31, actorId: 7);
            orderId = order.Id;
            await tx.CommitAsync();
        }

        await using (var check = CreateBill(null))
        {
            var unbilled = await check.ChargeLines
                .Where(c => c.EncounterId == encounterId && c.InvoiceId == null).ToListAsync();
            Assert.Equal(2, unbilled.Count);                         // visible at the billing counter
            Assert.All(unbilled, c => Assert.Equal(orderId, c.TestOrderId));
            Assert.All(unbilled, c => Assert.Equal(31, c.ReferrerId)); // attribution from day one
        }

        // --- tx 2: billing invoices the charges; order flips to invoiced ---
        long invoiceId;
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await using var bill = CreateBill(conn);
            await using var diag = CreateDiag(conn);
            await using var kernel = CreateKernel(conn);
            foreach (var c in new DbContext[] { bill, diag, kernel }) await c.Database.UseTransactionAsync(tx);

            var svc = new DiagnosticsService(new ChargePoster(bill, _billing), TimeProvider.System);
            var invoice = await _billing.CreateInvoiceAsync(bill, kernel, 1, encounterId, sessionId, 1,
                0, 0, null, 7, "Rasel");
            await svc.MarkInvoicedAsync(diag, orderId, invoice.Id);
            invoiceId = invoice.Id;
            await tx.CommitAsync();
        }

        // --- tx 3: payment → InProgress + TestOrderPaid outbox row (same tx) ---
        await using (var tx = await conn.BeginTransactionAsync())
        {
            await using var bill = CreateBill(conn);
            await using var diag = CreateDiag(conn);
            await using var kernel = CreateKernel(conn);
            foreach (var c in new DbContext[] { bill, diag, kernel }) await c.Database.UseTransactionAsync(tx);

            await _billing.CollectAsync(bill, kernel, 1, invoiceId, sessionId, 1600, "cash", null, 7, "Rasel");
            var svc = new DiagnosticsService(new ChargePoster(bill, _billing), TimeProvider.System);
            await svc.MarkPaidAsync(diag, kernel, orderId);
            await tx.CommitAsync();
        }

        await using var diagCheck = CreateDiag(null);
        Assert.Equal(TestOrderState.InProgress, (await diagCheck.Orders.SingleAsync(o => o.Id == orderId)).State);

        await using var kernelCheck = _pg.CreateKernelContext();
        var paidEvents = await kernelCheck.Outbox
            .Where(o => o.EventType == "TestOrderPaid").ToListAsync();
        Assert.Contains(paidEvents, o =>
            System.Text.Json.JsonDocument.Parse(o.Payload).RootElement
                .GetProperty("orderId").GetInt64() == orderId);
    }

    [Fact]
    public async Task Illegal_state_jump_is_rejected_with_zero_rows()
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var diag = CreateDiag(conn);
        await using var kernel = CreateKernel(conn);
        await diag.Database.UseTransactionAsync(tx);
        await kernel.Database.UseTransactionAsync(tx);

        diag.Orders.Add(new TestOrder
        {
            BranchId = 1, PatientId = 1, EncounterId = 1, State = TestOrderState.Ordered,
            PromisedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 7,
        });
        await diag.SaveChangesAsync();
        var id = diag.Orders.Local.Single().Id;

        // Paid before invoiced (G9: illegal move asserts rejection)
        var svc = new DiagnosticsService(null!, TimeProvider.System);
        var ex = await Assert.ThrowsAsync<DiagnosticsException>(() => svc.MarkPaidAsync(diag, kernel, id));
        Assert.Contains("not awaiting payment", ex.Message);
    }
}
