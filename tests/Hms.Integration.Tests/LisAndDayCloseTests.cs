using Hms.Billing;
using Hms.Billing.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Hms.Lis;
using Hms.Lis.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

[Collection("postgres")]
public class LisAndDayCloseTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly LisService _lis = new(TimeProvider.System);
    private readonly BillingService _billing = new(
        new NumberSeriesService(), new AuditWriter(TimeProvider.System),
        new FiscalCalendar(7), new BusinessDayCalendar(TimeOnly.MinValue), TimeProvider.System);
    private readonly DayCloseService _dayClose = new(new AuditWriter(TimeProvider.System), TimeProvider.System);

    public LisAndDayCloseTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var lis = CreateLis(null);
        await lis.Database.MigrateAsync();
        await using var bill = CreateBill(null);
        await bill.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private LisDbContext CreateLis(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<LisDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "lis"))
            : new DbContextOptionsBuilder<LisDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "lis")))
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

    // ---- sample chain ------------------------------------------------------

    [Fact]
    public async Task Sample_chain_collect_receive_reject_spawns_child_with_same_tests()
    {
        await using var lis = CreateLis(null);
        var sample = await _lis.CreateSampleAsync(lis, 1, "EDTA", [101, 102]);   // CBC+ESR, one tube
        var barcode1 = await _lis.PrintLabelAsync(lis, sample.Id, 9);
        var barcode2 = await _lis.PrintLabelAsync(lis, sample.Id, 9);            // lost label
        Assert.Equal(barcode1, barcode2);                                        // edge 27: same identity
        Assert.True((await lis.LabelPrints.Where(p => p.SampleId == sample.Id).ToListAsync())
            .Last().Reprint);

        await _lis.CollectAsync(lis, sample.Id, 9);
        await _lis.ReceiveAsync(lis, sample.Id, 10);
        var child = await _lis.RejectAndSpawnRecollectionAsync(lis, sample.Id, "hemolyzed");

        Assert.NotEqual(sample.Barcode, child.Barcode);                          // new identity
        Assert.Equal(sample.Id, child.RecollectionOf);                           // chain preserved
        var childTests = await lis.SampleTests.Where(st => st.SampleId == child.Id)
            .Select(st => st.OrderTestId).OrderBy(x => x).ToListAsync();
        Assert.Equal([101L, 102L], childTests);                                  // same tests, no orphans
    }

    [Fact]
    public async Task Illegal_sample_moves_are_rejected()
    {
        await using var lis = CreateLis(null);
        var sample = await _lis.CreateSampleAsync(lis, 1, "Serum", [201]);
        // receive before collect (G9 illegal move)
        var ex = await Assert.ThrowsAsync<LisException>(() => _lis.ReceiveAsync(lis, sample.Id, 10));
        Assert.Contains("already moved on", ex.Message);
    }

    [Fact]
    public async Task Result_versioning_verify_then_amend_keeps_v1_immutable()
    {
        await using var lis = CreateLis(null);
        var r1 = await _lis.EnterResultAsync(lis, 301,
            new() { ["hb"] = new { value = 13.2, unit = "g/dL" } }, null, 9);

        // double entry refused — corrections are amendments
        await Assert.ThrowsAsync<LisException>(() => _lis.EnterResultAsync(lis, 301, [], null, 9));

        await _lis.VerifyAsync(lis, r1.Id, 41, "reporting_consultant");          // edge 34
        var verified = await lis.Results.SingleAsync(r => r.Id == r1.Id);
        Assert.NotNull(verified.EsignHash);
        Assert.Equal("reporting_consultant", verified.VerifierRole);

        var r2 = await _lis.AmendAsync(lis, 301, new() { ["hb"] = new { value = 13.7 } }, 41, 555);
        Assert.Equal(2, r2.Version);
        Assert.Equal(1, r2.SupersedesVersion);
        var v1 = await lis.Results.SingleAsync(r => r.OrderTestId == 301 && r.Version == 1);
        Assert.Contains("13.2", v1.Values);                                      // v1 untouched (edge 22)
    }

    // ---- day-close ---------------------------------------------------------

    private async Task<(long sessionId, long counterId, long invoiceId)> SessionWithPaidInvoiceAsync(
        long price, long pay)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var bill = CreateBill(conn);
        await using var kernel = CreateKernel(conn);
        await bill.Database.UseTransactionAsync(tx);
        await kernel.Database.UseTransactionAsync(tx);

        var counter = new Counter { BranchId = 1, Name = "DC-" + Guid.NewGuid().ToString("N")[..6], Kind = "front-desk" };
        bill.Counters.Add(counter);
        await bill.SaveChangesAsync();
        var session = await _billing.OpenSessionAsync(bill, 1, counter.Id, 7, openingFloat: 2000);
        var enc = new Encounter
        {
            BranchId = 1, PatientId = 1, OnDate = new DateOnly(2026, 7, 26), Type = "OPD",
            CounterId = counter.Id, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 7,
        };
        bill.Encounters.Add(enc);
        await bill.SaveChangesAsync();
        await _billing.PostChargeAsync(bill, 1, enc.Id, "Billing",
            new NewChargeLine("service", 0, "Panel", 1, price), 7);
        var invoice = await _billing.CreateInvoiceAsync(bill, kernel, 1, enc.Id, session.Id, 1, 0, 0, null, 7, "Rasel");
        if (pay > 0)
            await _billing.CollectAsync(bill, kernel, 1, invoice.Id, session.Id, pay, "cash", null, 7, "Rasel");
        await tx.CommitAsync();
        return (session.Id, counter.Id, invoice.Id);
    }

    private async Task<T> InTxAsync<T>(Func<BillDbContext, KernelDbContext, Task<T>> body)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var bill = CreateBill(conn);
        await using var kernel = CreateKernel(conn);
        await bill.Database.UseTransactionAsync(tx);
        await kernel.Database.UseTransactionAsync(tx);
        var r = await body(bill, kernel);
        await tx.CommitAsync();
        return r;
    }

    [Fact]
    public async Task Day_close_reconciles_and_records_variance_without_blocking()
    {
        var (sessionId, _, _) = await SessionWithPaidInvoiceAsync(price: 1500, pay: 1000);

        // float 2000 + cash 1000 = expected 3000; operator counts 2950 → variance −50, still closes
        var summary = await InTxAsync((bill, kernel) =>
            _dayClose.CloseAsync(bill, kernel, sessionId, countedCash: 2950, 7, "Rasel"));

        Assert.Equal(-50, summary.Variance);                          // edge 18: recorded, not blocked
        Assert.Equal(1500, summary.Net);
        Assert.Equal(500, summary.DueCreated);
        Assert.Contains("\"cash\":1000", summary.TenderTotals.Replace(" ", ""));

        // G6: Σ tender totals = Σ session receipts
        await using var check = CreateBill(null);
        var receiptSum = await check.Receipts.Where(r => r.CounterSessionId == sessionId).SumAsync(r => r.Amount);
        var totals = System.Text.Json.JsonDocument.Parse(summary.TenderTotals).RootElement;
        var tenderSum = totals.EnumerateObject().Sum(p => p.Value.GetInt64());
        Assert.Equal(receiptSum, tenderSum);
    }

    [Fact]
    public async Task Close_vs_late_receipt_race_yields_one_consistent_outcome()
    {
        var (sessionId, _, invoiceId) = await SessionWithPaidInvoiceAsync(price: 1000, pay: 400);

        // A close and a late 300-taka receipt race on the session row lock (G7).
        var closeOk = false; var receiptOk = false;
        await Parallel.ForAsync(0, 2, async (i, ct) =>
        {
            try
            {
                if (i == 0)
                {
                    await InTxAsync((b, k) => _dayClose.CloseAsync(b, k, sessionId, 2400, 7, "Rasel"));
                    closeOk = true;
                }
                else
                {
                    await InTxAsync((b, k) => _billing.CollectAsync(b, k, 1, invoiceId, sessionId, 300, "cash", null, 7, "Rasel"));
                    receiptOk = true;
                }
            }
            catch (BillingException) { /* the loser gets a comprehensible refusal */ }
        });

        Assert.True(closeOk);                                          // close always succeeds
        await using var check = CreateBill(null);
        var summary = await check.DayCloses.SingleAsync(s => s.CounterSessionId == sessionId);
        var receiptSum = await check.Receipts.Where(r => r.CounterSessionId == sessionId).SumAsync(r => r.Amount);
        var tenderSum = System.Text.Json.JsonDocument.Parse(summary.TenderTotals)
            .RootElement.EnumerateObject().Sum(p => p.Value.GetInt64());
        // Whichever order won, the summary equals the receipts that are actually in the session.
        Assert.Equal(receiptSum, tenderSum);
        if (!receiptOk) Assert.Equal(400, receiptSum);                 // late receipt was refused
    }

    [Fact]
    public async Task Refund_after_close_is_negative_receipt_in_current_session()
    {
        var (closedSessionId, _, invoiceId) = await SessionWithPaidInvoiceAsync(price: 800, pay: 800);
        await InTxAsync((b, k) => _dayClose.CloseAsync(b, k, closedSessionId, 2800, 7, "Rasel"));

        // Today's open session takes the refund; the closed session stays untouched (edge 20).
        var (todaySessionId, _, _) = await SessionWithPaidInvoiceAsync(price: 100, pay: 100);
        var originalReceipt = await CreateBill(null).Receipts
            .Where(r => r.CounterSessionId == closedSessionId && r.Amount > 0).FirstAsync();

        var refund = await InTxAsync((b, k) => _billing.CollectAsync(b, k, 1, invoiceId, todaySessionId,
            amount: -800, "cash", null, 7, "Rasel", refundOfReceipt: originalReceipt.Id, approvalId: 777));

        Assert.Equal(-800, refund.Amount);
        Assert.Equal(todaySessionId, refund.CounterSessionId);         // today's money movement
        await using var check = CreateBill(null);
        Assert.Equal(800, (await check.Dues.SingleAsync(d => d.InvoiceId == invoiceId)).Balance);
        Assert.Equal(SessionState.Closed,
            (await check.Sessions.SingleAsync(s => s.Id == closedSessionId)).State);

        // And the closed session's receipt is immutable — the trigger rejects mutation (C5).
        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await using var conn = new NpgsqlConnection(_pg.ConnectionString);
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE bill.receipt SET amount = 1 WHERE id = {originalReceipt.Id}";
            await cmd.ExecuteNonQueryAsync();
        });
        Assert.Contains("immutable", ex.Message);
    }
}
