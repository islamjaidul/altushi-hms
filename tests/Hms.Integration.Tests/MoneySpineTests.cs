using Hms.Billing;
using Hms.Billing.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// S2 (spec 0006): the G6 money invariants as permanent executable specifications, on real
/// Postgres. One connection carries bill + kernel contexts in one transaction (G19).
/// </summary>
[Collection("postgres")]
public class MoneySpineTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly BillingService _svc = new(
        new NumberSeriesService(), new AuditWriter(TimeProvider.System),
        new FiscalCalendar(7), new BusinessDayCalendar(TimeOnly.MinValue), TimeProvider.System);

    public MoneySpineTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var bill = CreateBill(null);
        await bill.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

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

    /// <summary>Runs body with bill+kernel contexts sharing one connection+transaction, commits.</summary>
    private async Task<T> InTxAsync<T>(Func<BillDbContext, KernelDbContext, Task<T>> body)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var bill = CreateBill(conn);
        await using var kernel = CreateKernel(conn);
        await bill.Database.UseTransactionAsync(tx);
        await kernel.Database.UseTransactionAsync(tx);
        var result = await body(bill, kernel);
        await tx.CommitAsync();
        return result;
    }

    private async Task<(long sessionId, long encounterId)> SetupEncounterAsync()
        => await InTxAsync(async (bill, kernel) =>
        {
            var counter = new Counter { BranchId = 1, Name = "C-" + Guid.NewGuid().ToString("N")[..6], Kind = "front-desk" };
            bill.Counters.Add(counter);
            await bill.SaveChangesAsync();
            var session = await _svc.OpenSessionAsync(bill, 1, counter.Id, 7, openingFloat: 2000);
            var enc = new Encounter
            {
                BranchId = 1, PatientId = 1, OnDate = new DateOnly(2026, 7, 26),
                Type = "OPD", CounterId = counter.Id, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 7,
            };
            bill.Encounters.Add(enc);
            await bill.SaveChangesAsync();
            return (session.Id, enc.Id);
        });

    [Fact]
    public async Task Invoice_identity_holds_with_percentage_discount_rounded_once()
    {
        var (sessionId, encId) = await SetupEncounterAsync();

        var invoice = await InTxAsync(async (bill, kernel) =>
        {
            // 3 lines: 333 + 333 + 999 = 1665; 7.5% = 124.875 → 125 (half-up, once)
            foreach (var price in new long[] { 333, 333, 999 })
                await _svc.PostChargeAsync(bill, 1, encId, "Billing",
                    new NewChargeLine("service", 0, $"Svc {price}", 1, price), 7);
            return await _svc.CreateInvoiceAsync(bill, kernel, 1, encId, sessionId, 1,
                discountPercent: 7.5m, discountFlat: 0, discountApprovalId: null, 7, "Rasel");
        });

        Assert.Equal(1665, invoice.Gross);
        Assert.Equal(125, invoice.Discount);                       // 03 §6: half-up at the total, once
        Assert.Equal(1540, invoice.Net);
        Assert.Equal(invoice.Net, invoice.Gross - invoice.Discount + invoice.Tax + invoice.RoundingAdj);
    }

    [Fact]
    public async Task Receipts_plus_due_always_equal_net()
    {
        var (sessionId, encId) = await SetupEncounterAsync();
        var invoice = await InTxAsync(async (bill, kernel) =>
        {
            await _svc.PostChargeAsync(bill, 1, encId, "Billing",
                new NewChargeLine("service", 0, "Consultation", 1, 700), 7);
            return await _svc.CreateInvoiceAsync(bill, kernel, 1, encId, sessionId, 1, 0, 0, null, 7, "Rasel");
        });

        await InTxAsync((bill, kernel) =>
            _svc.CollectAsync(bill, kernel, 1, invoice.Id, sessionId, 500, "cash", null, 7, "Rasel"));

        await using var check = CreateBill(null);
        var receipts = await check.Receipts.Where(r => r.InvoiceId == invoice.Id).SumAsync(r => r.Amount);
        var due = await check.Dues.SingleAsync(d => d.InvoiceId == invoice.Id);
        Assert.Equal(invoice.Net, receipts + due.Balance);         // G6: Σ receipts + due = net
        Assert.Equal(InvoiceState.PartiallyPaid,
            (await check.Invoices.SingleAsync(i => i.Id == invoice.Id)).State);
    }

    [Fact]
    public async Task Parallel_due_collection_never_over_collects()
    {
        var (sessionId, encId) = await SetupEncounterAsync();
        var invoice = await InTxAsync(async (bill, kernel) =>
        {
            await _svc.PostChargeAsync(bill, 1, encId, "Billing",
                new NewChargeLine("service", 0, "X-ray", 1, 1000), 7);
            return await _svc.CreateInvoiceAsync(bill, kernel, 1, encId, sessionId, 1, 0, 0, null, 7, "Rasel");
        });

        // Two counters race to collect 800 each against a 1000 due — exactly one must win (G7).
        var outcomes = new List<bool>();
        await Parallel.ForAsync(0, 2, async (_, ct) =>
        {
            try
            {
                await InTxAsync((bill, kernel) =>
                    _svc.CollectAsync(bill, kernel, 1, invoice.Id, sessionId, 800, "cash", null, 7, "Rasel"));
                lock (outcomes) outcomes.Add(true);
            }
            catch (BillingException)
            {
                lock (outcomes) outcomes.Add(false);
            }
        });

        Assert.Equal([false, true], outcomes.Order());             // one success, one comprehensible refusal
        await using var check = CreateBill(null);
        var due = await check.Dues.SingleAsync(d => d.InvoiceId == invoice.Id);
        Assert.Equal(200, due.Balance);                            // never negative
    }

    [Fact]
    public async Task Overpayment_is_refused()
    {
        var (sessionId, encId) = await SetupEncounterAsync();
        var invoice = await InTxAsync(async (bill, kernel) =>
        {
            await _svc.PostChargeAsync(bill, 1, encId, "Billing",
                new NewChargeLine("service", 0, "ECG", 1, 400), 7);
            return await _svc.CreateInvoiceAsync(bill, kernel, 1, encId, sessionId, 1, 0, 0, null, 7, "Rasel");
        });

        await Assert.ThrowsAsync<BillingException>(() => InTxAsync((bill, kernel) =>
            _svc.CollectAsync(bill, kernel, 1, invoice.Id, sessionId, 500, "cash", null, 7, "Rasel")));
    }

    [Fact]
    public async Task Second_open_session_on_same_counter_is_refused_comprehensibly()
    {
        var counterId = await InTxAsync(async (bill, _) =>
        {
            var c = new Counter { BranchId = 1, Name = "C-dup-" + Guid.NewGuid().ToString("N")[..6], Kind = "front-desk" };
            bill.Counters.Add(c);
            await bill.SaveChangesAsync();
            await _svc.OpenSessionAsync(bill, 1, c.Id, 7, 1000);
            return c.Id;
        });

        var ex = await Assert.ThrowsAsync<BillingException>(() => InTxAsync(
            (bill, _) => _svc.OpenSessionAsync(bill, 1, counterId, 8, 1000)));
        Assert.Contains("already has an open session", ex.Message);
    }

    [Fact]
    public async Task App_role_cannot_delete_financial_rows()
    {
        var (sessionId, encId) = await SetupEncounterAsync();
        var invoice = await InTxAsync(async (bill, kernel) =>
        {
            await _svc.PostChargeAsync(bill, 1, encId, "Billing",
                new NewChargeLine("service", 0, "CBC", 1, 400), 7);
            return await _svc.CreateInvoiceAsync(bill, kernel, 1, encId, sessionId, 1, 0, 0, null, 7, "Rasel");
        });

        // Connect as hms_app (created by the migration) and try to DELETE — must be denied (C5/G11).
        var csb = new NpgsqlConnectionStringBuilder(_pg.ConnectionString)
        { Username = "hms_app", Password = "hms_app_test" };
        await using var conn = new NpgsqlConnection(csb.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DELETE FROM bill.invoice WHERE id = {invoice.Id}";
        var ex = await Assert.ThrowsAsync<PostgresException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal("42501", ex.SqlState);                        // insufficient_privilege
    }
}
