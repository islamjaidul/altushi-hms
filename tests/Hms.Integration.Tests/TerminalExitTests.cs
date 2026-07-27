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
/// Spec 0021: one prepared bill produces one invoice, under a double-click AND under two
/// genuinely concurrent posts. The terminal-exit settlement rules live in the composition
/// root (IpdBilling), so they are proven by IpdFolioTests + the lifecycle thread; what is
/// proven here is the money spine's own guarantee, which is what the whole product leans on.
/// </summary>
[Collection("postgres")]
public class TerminalExitTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly BillingService _svc = new(
        new NumberSeriesService(), new AuditWriter(TimeProvider.System),
        new FiscalCalendar(7), new BusinessDayCalendar(TimeOnly.MinValue), TimeProvider.System);

    public TerminalExitTests(PostgresFixture pg) => _pg = pg;

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

    private async Task<(long SessionId, long EncounterId, long PatientId)> SetupAsync()
        => await InTxAsync(async (bill, kernel) =>
        {
            var counter = new Counter
            {
                BranchId = 1, Name = "C-" + Guid.NewGuid().ToString("N")[..6], Kind = "front-desk",
            };
            bill.Counters.Add(counter);
            await bill.SaveChangesAsync();
            var session = await _svc.OpenSessionAsync(bill, 1, counter.Id, 7, 0);
            var patientId = Random.Shared.NextInt64(1_000_000, 9_999_999);
            var enc = new Encounter
            {
                BranchId = 1, PatientId = patientId,
                OnDate = DateOnly.FromDateTime(DateTime.UtcNow), Type = "OPD",
                CounterId = counter.Id, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 7,
            };
            bill.Encounters.Add(enc);
            await bill.SaveChangesAsync();
            return (session.Id, enc.Id, patientId);
        });

    private Task PostChargeAsync(long encounterId, long amount)
        => InTxAsync(async (bill, _) =>
            await _svc.PostChargeAsync(bill, 1, encounterId, "Billing",
                new NewChargeLine("service", 1, "Consultation", 1, amount), 7));

    [Fact]
    public async Task A_repeated_submission_returns_the_same_invoice_and_bills_once()
    {
        var (sessionId, encounterId, patientId) = await SetupAsync();
        await PostChargeAsync(encounterId, 700);
        var token = Guid.NewGuid();

        var first = await InTxAsync(async (bill, kernel) => await _svc.CreateInvoiceAsync(
            bill, kernel, 1, encounterId, sessionId, patientId, 0m, 0, null, 7, "t",
            submissionToken: token));

        // The operator double-clicks: same token, and by then the charges are already billed.
        var second = await InTxAsync(async (bill, kernel) => await _svc.CreateInvoiceAsync(
            bill, kernel, 1, encounterId, sessionId, patientId, 0m, 0, null, 7, "t",
            submissionToken: token));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.InvoiceNo, second.InvoiceNo);
        await InTxAsync(async (bill, _) =>
        {
            var count = await bill.Invoices.CountAsync(i => i.SubmissionToken == token);
            Assert.Equal(1, count);
            var lines = await bill.InvoiceLines.CountAsync(l => l.InvoiceId == first.Id);
            Assert.Equal(1, lines);                        // not doubled
            return 0;
        });
    }

    [Fact]
    public async Task Two_concurrent_posts_of_one_submission_produce_exactly_one_invoice()
    {
        var (sessionId, encounterId, patientId) = await SetupAsync();
        await PostChargeAsync(encounterId, 1200);
        var token = Guid.NewGuid();

        // Both start before either commits. Whatever the interleaving — one wins outright, or
        // the loser catches the unique violation, or the loser reads the winner's row — the
        // patient must end up with exactly ONE invoice, and both callers must point at it.
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            try
            {
                var inv = await InTxAsync(async (bill, kernel) => await _svc.CreateInvoiceAsync(
                    bill, kernel, 1, encounterId, sessionId, patientId, 0m, 0, null, 7, "t",
                    submissionToken: token));
                return inv.Id;
            }
            catch (DbUpdateException)          // unique(submission_token): resolve to the winner
            {
                return await InTxAsync(async (bill, _) => await bill.Invoices.AsNoTracking()
                    .Where(i => i.SubmissionToken == token).Select(i => i.Id).SingleAsync());
            }
            catch (BillingException)           // the winner already consumed the charges
            {
                return await InTxAsync(async (bill, _) => await bill.Invoices.AsNoTracking()
                    .Where(i => i.SubmissionToken == token).Select(i => i.Id).SingleAsync());
            }
        }));

        Assert.Equal(results[0], results[1]);          // both callers, one invoice
        var invoices = await InTxAsync(async (bill, _) =>
            await bill.Invoices.CountAsync(i => i.SubmissionToken == token));
        Assert.Equal(1, invoices);
    }

    [Fact]
    public async Task Submissions_without_a_token_are_unaffected()
    {
        var (sessionId, encounterId, patientId) = await SetupAsync();
        await PostChargeAsync(encounterId, 300);
        var a = await InTxAsync(async (bill, kernel) => await _svc.CreateInvoiceAsync(
            bill, kernel, 1, encounterId, sessionId, patientId, 0m, 0, null, 7, "t"));

        await PostChargeAsync(encounterId, 300);
        var b = await InTxAsync(async (bill, kernel) => await _svc.CreateInvoiceAsync(
            bill, kernel, 1, encounterId, sessionId, patientId, 0m, 0, null, 7, "t"));

        Assert.NotEqual(a.Id, b.Id);           // two deliberate bills remain two bills
        Assert.Null(a.SubmissionToken);
    }

    [Fact]
    public async Task A_folio_settlement_is_idempotent_on_its_submission_too()
    {
        var (sessionId, _, patientId) = await SetupAsync();
        var folioId = Random.Shared.NextInt64(1_000_000, 9_999_999);
        await InTxAsync(async (bill, _) => await _svc.PostFolioChargeAsync(
            bill, 1, folioId, "Ipd", new NewChargeLine("service", 1, "Bed day", 1, 1200), 7));
        var token = Guid.NewGuid();

        var (first, _) = await InTxAsync(async (bill, kernel) => await _svc.CreateFolioInvoiceAsync(
            bill, kernel, 1, folioId, sessionId, patientId, 0m, 0, null, 7, "t",
            submissionToken: token));
        var (second, _) = await InTxAsync(async (bill, kernel) => await _svc.CreateFolioInvoiceAsync(
            bill, kernel, 1, folioId, sessionId, patientId, 0m, 0, null, 7, "t",
            submissionToken: token));

        Assert.Equal(first.Id, second.Id);
        var count = await InTxAsync(async (bill, _) =>
            await bill.Invoices.CountAsync(i => i.FolioId == folioId));
        Assert.Equal(1, count);
    }
}
