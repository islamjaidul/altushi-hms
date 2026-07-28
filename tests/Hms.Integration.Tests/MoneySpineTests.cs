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

    // =======================================================================
    // The reversal path (spec 0032, M4-D1/D2/D4/F1/F2 · LC-BIL-04, LC-BIL-09, LC-BIL-10)
    //
    // `patient-lifecycle.md` cited this class for LC-BIL-09 while it contained no refund test at
    // all: the two operations hard rule 4 exists to govern were referenced by nothing under
    // `tests/`. Everything below is the rules they carry, stated once.
    // =======================================================================

    private DayCloseService DayClose() => new(
        new AuditWriter(TimeProvider.System), new BusinessDayCalendar(TimeOnly.MinValue),
        TimeProvider.System);

    /// <summary>An invoice for <paramref name="net"/>, with <paramref name="paid"/> collected.</summary>
    private async Task<(long InvoiceId, long SessionId)> BilledAsync(
        long net, long paid = 0, string tender = Tenders.Cash)
    {
        var (sessionId, encId) = await SetupEncounterAsync();
        var invoice = await InTxAsync(async (bill, kernel) =>
        {
            await _svc.PostChargeAsync(bill, 1, encId, "Billing",
                new NewChargeLine("service", 0, "Consultation", 1, net), 7);
            return await _svc.CreateInvoiceAsync(bill, kernel, 1, encId, sessionId, 1, 0, 0, null, 7, "Rasel");
        });
        if (paid > 0)
            await InTxAsync((bill, kernel) => _svc.CollectAsync(
                bill, kernel, 1, invoice.Id, sessionId, paid, tender, null, 7, "Rasel"));
        return (invoice.Id, sessionId);
    }

    /// <summary>The three terms of the invariant, read back from a fresh connection.</summary>
    private async Task<(string State, long Net, long Collected, long Due, long Refunded)> ReadAsync(long invoiceId)
    {
        await using var check = CreateBill(null);
        var invoice = await check.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
        var receipts = await check.Receipts.AsNoTracking()
            .Where(r => r.InvoiceId == invoiceId).ToListAsync();
        var due = await check.Dues.AsNoTracking().SingleAsync(d => d.InvoiceId == invoiceId);
        return (invoice.State, invoice.Net, receipts.Sum(r => r.Amount), due.Balance,
                -receipts.Where(r => r.Amount < 0).Sum(r => r.Amount));
    }

    /// <summary>`Σ receipts + due = Realised` — the invariant LC-BIL-04 should have stated.</summary>
    private async Task AssertBalancesAsync(long invoiceId)
    {
        var (state, net, collected, due, refunded) = await ReadAsync(invoiceId);
        Assert.Equal(InvoiceValue.Realised(state, net, refunded), collected + due);
    }

    // ---- M4-D1: the invariant, in every state ------------------------------

    [Fact]
    public async Task The_money_invariant_survives_every_way_an_invoice_can_end()
    {
        // One assertion, six shapes. Before the fix the partial-refund rows were false: a ৳200
        // refund on a fully paid ৳1000 invoice marked the whole thing `Refunded`, so the invoice
        // claimed to be worth nothing while ৳800 sat in the drawer against it.
        var billed = await BilledAsync(net: 700);
        await AssertBalancesAsync(billed.InvoiceId);                       // billed, nothing paid

        var part = await BilledAsync(net: 1000, paid: 600);
        await AssertBalancesAsync(part.InvoiceId);                         // partially paid

        var paid = await BilledAsync(net: 550, paid: 550);
        await AssertBalancesAsync(paid.InvoiceId);                         // paid in full

        var cancelled = await BilledAsync(net: 400);
        await InTxAsync(async (bill, kernel) =>
        {
            await _svc.CancelInvoiceAsync(bill, kernel, 1, cancelled.InvoiceId,
                "wrong patient", 7, "Rasel", approvalId: 1);
            return 0;
        });
        await AssertBalancesAsync(cancelled.InvoiceId);                    // cancelled

        var partlyRefunded = await BilledAsync(net: 1000, paid: 1000);
        await RefundAsync(partlyRefunded, 200);
        await AssertBalancesAsync(partlyRefunded.InvoiceId);               // paid, partly given back

        var fullyRefunded = await BilledAsync(net: 550, paid: 550);
        await RefundAsync(fullyRefunded, 550);
        await AssertBalancesAsync(fullyRefunded.InvoiceId);                // fully reversed
    }

    private Task<Receipt> RefundAsync(
        (long InvoiceId, long SessionId) inv, long amount, string tender = Tenders.Cash)
        => InTxAsync((bill, kernel) => _svc.RefundAsync(
            bill, kernel, 1, inv.InvoiceId, inv.SessionId, amount, tender,
            "test cancelled by the doctor", 7, "Rasel", approvalId: 1));

    [Fact]
    public async Task A_refund_leaves_the_due_alone_rather_than_re_charging_the_patient()
    {
        // The design decision behind M4-D1, pinned so a later reader cannot "fix" the invariant
        // by restoring the due: that would put the patient back on /billing/dues and into the
        // MD's Outstanding-due tile for money the hospital just chose to hand back.
        var inv = await BilledAsync(net: 550, paid: 550);
        await RefundAsync(inv, 550);

        var (state, _, collected, due, refunded) = await ReadAsync(inv.InvoiceId);
        Assert.Equal(0, due);                                              // NOT 550
        Assert.Equal(0, collected);
        Assert.Equal(550, refunded);
        Assert.Equal(InvoiceState.Refunded, state);
    }

    // ---- M4-D2: the rules the two reversals carry --------------------------

    [Fact]
    public async Task A_refund_is_a_negative_receipt_pointing_at_the_original_never_a_delete()
    {
        var inv = await BilledAsync(net: 550, paid: 550);
        var original = await InTxAsync(async (bill, _) =>
            await bill.Receipts.AsNoTracking().SingleAsync(r => r.InvoiceId == inv.InvoiceId));

        var refund = await RefundAsync(inv, 550);

        Assert.Equal(-550, refund.Amount);                                 // negative, not a delete
        Assert.Equal(original.Id, refund.RefundOfReceipt);                 // points at what it reverses
        Assert.NotEqual(original.ReceiptNo, refund.ReceiptNo);             // its own number (ADR-0004)

        await using var check = CreateBill(null);
        Assert.Equal(2, await check.Receipts.CountAsync(r => r.InvoiceId == inv.InvoiceId));
    }

    [Fact]
    public async Task A_refund_cannot_exceed_what_was_actually_paid()
    {
        var inv = await BilledAsync(net: 1000, paid: 600);

        var ex = await Assert.ThrowsAsync<BillingException>(() => RefundAsync(inv, 601));
        Assert.Contains("600", ex.Message);

        // …and not by instalments either: the guard reads receipts NET of earlier refunds.
        await RefundAsync(inv, 400);
        await Assert.ThrowsAsync<BillingException>(() => RefundAsync(inv, 201));
        await AssertBalancesAsync(inv.InvoiceId);
    }

    [Fact]
    public async Task An_invoice_nobody_has_paid_is_cancelled_not_refunded()
    {
        var inv = await BilledAsync(net: 700);
        var ex = await Assert.ThrowsAsync<BillingException>(() => RefundAsync(inv, 700));
        Assert.Contains("cancel it instead", ex.Message);
    }

    [Fact]
    public async Task An_invoice_with_money_on_it_cannot_be_cancelled()
    {
        var inv = await BilledAsync(net: 700, paid: 100);
        var ex = await Assert.ThrowsAsync<BillingException>(() => InTxAsync(async (bill, kernel) =>
        {
            await _svc.CancelInvoiceAsync(bill, kernel, 1, inv.InvoiceId, "changed mind", 7, "Rasel", 1);
            return 0;
        }));
        Assert.Contains("must be refunded, not cancelled", ex.Message);
    }

    [Fact]
    public async Task Both_reversals_need_a_reason()
    {
        var refundable = await BilledAsync(net: 550, paid: 550);
        var reasonless = await Assert.ThrowsAsync<BillingException>(() => InTxAsync(
            (bill, kernel) => _svc.RefundAsync(bill, kernel, 1, refundable.InvoiceId,
                refundable.SessionId, 550, Tenders.Cash, "   ", 7, "Rasel", 1)));
        Assert.Contains("needs a reason", reasonless.Message);

        var cancellable = await BilledAsync(net: 700);
        var unexplained = await Assert.ThrowsAsync<BillingException>(() => InTxAsync(
            async (bill, kernel) =>
            {
                await _svc.CancelInvoiceAsync(bill, kernel, 1, cancellable.InvoiceId, "", 7, "Rasel", 1);
                return 0;
            }));
        Assert.Contains("needs a reason", unexplained.Message);
    }

    [Fact]
    public async Task A_refund_needs_an_open_counter_session()
    {
        var inv = await BilledAsync(net: 550, paid: 550);
        await InTxAsync(async (bill, kernel) =>
            await DayClose().CloseAsync(bill, kernel, inv.SessionId, 2550, 7, "Rasel"));

        var ex = await Assert.ThrowsAsync<BillingException>(() => RefundAsync(inv, 550));
        Assert.Contains("open counter session", ex.Message);
    }

    [Fact]
    public async Task A_cancelled_invoice_keeps_its_number_and_its_row()
    {
        // LC-BIL-10 at the unit layer: §11's pre-payment exit marks and clears, never removes —
        // the number it consumed stays accounted for (ADR-0004).
        var inv = await BilledAsync(net: 400);
        await InTxAsync(async (bill, kernel) =>
        {
            await _svc.CancelInvoiceAsync(bill, kernel, 1, inv.InvoiceId, "duplicate bill", 7, "Rasel", 1);
            return 0;
        });

        var (state, net, _, due, _) = await ReadAsync(inv.InvoiceId);
        Assert.Equal(InvoiceState.Cancelled, state);
        Assert.Equal(400, net);                                            // the amount is still readable
        Assert.Equal(0, due);

        // …and it cannot be cancelled a second time.
        var again = await Assert.ThrowsAsync<BillingException>(() => InTxAsync(async (bill, kernel) =>
        {
            await _svc.CancelInvoiceAsync(bill, kernel, 1, inv.InvoiceId, "again", 7, "Rasel", 1);
            return 0;
        }));
        Assert.Contains("not in a cancellable state", again.Message);
    }

    // ---- M4-F1: a partial refund does not finish the invoice ---------------

    [Fact]
    public async Task A_partial_refund_leaves_the_invoice_live_and_worth_what_is_left()
    {
        var inv = await BilledAsync(net: 1000, paid: 1000);
        await RefundAsync(inv, 200);

        var (state, net, collected, due, refunded) = await ReadAsync(inv.InvoiceId);
        Assert.Equal(InvoiceState.Paid, state);                            // not `Refunded`
        Assert.Equal(800, collected);
        Assert.Equal(0, due);
        Assert.Equal(800, InvoiceValue.Realised(state, net, refunded));
        Assert.False(InvoiceValue.IsReversed(state));
    }

    [Fact]
    public async Task An_invoice_is_only_refunded_once_everything_taken_has_gone_back()
    {
        // Partially paid, then fully refunded: nothing is in the drawer, but ৳400 is still owed,
        // so the invoice is not finished — it is back to `billed`.
        var inv = await BilledAsync(net: 1000, paid: 600);
        await RefundAsync(inv, 600);

        var (state, _, collected, due, _) = await ReadAsync(inv.InvoiceId);
        Assert.Equal(InvoiceState.Billed, state);
        Assert.Equal(0, collected);
        Assert.Equal(400, due);
        await AssertBalancesAsync(inv.InvoiceId);
    }

    // ---- M4-F2: the tender is the operator's word, not an inference --------

    [Fact]
    public async Task A_cash_refund_is_booked_as_cash_however_the_patient_paid()
    {
        // ৳500 cash + ৳1000 card, then ৳200 handed back in cash. The old code copied the tender
        // of the LARGEST receipt, booked the cash refund as card, and left expected cash ৳200
        // high — a shortfall the operator could not explain.
        var (sessionId, encId) = await SetupEncounterAsync();
        var invoice = await InTxAsync(async (bill, kernel) =>
        {
            await _svc.PostChargeAsync(bill, 1, encId, "Billing",
                new NewChargeLine("service", 0, "Package", 1, 1500), 7);
            return await _svc.CreateInvoiceAsync(bill, kernel, 1, encId, sessionId, 1, 0, 0, null, 7, "Rasel");
        });
        await InTxAsync((bill, kernel) => _svc.CollectAsync(
            bill, kernel, 1, invoice.Id, sessionId, 500, Tenders.Cash, null, 7, "Rasel"));
        await InTxAsync((bill, kernel) => _svc.CollectAsync(
            bill, kernel, 1, invoice.Id, sessionId, 1000, Tenders.Card, null, 7, "Rasel"));

        var refund = await RefundAsync((invoice.Id, sessionId), 200, Tenders.Cash);
        Assert.Equal(Tenders.Cash, refund.Tender);

        // It reverses the CASH receipt, not the bigger card one.
        await using var check = CreateBill(null);
        var cash = await check.Receipts.AsNoTracking()
            .SingleAsync(r => r.InvoiceId == invoice.Id && r.Amount == 500);
        Assert.Equal(cash.Id, refund.RefundOfReceipt);

        // And the drawer knows: expected cash is float + 500 − 200.
        var summary = await InTxAsync(async (bill, kernel) =>
            await DayClose().CloseAsync(bill, kernel, sessionId, 0, 7, "Rasel"));
        await using var after = CreateBill(null);
        var session = await after.Sessions.AsNoTracking().SingleAsync(s => s.Id == sessionId);
        Assert.Equal(2000 + 500 - 200, session.ExpectedCash);
        Assert.Equal(200, summary.Refunds);
    }

    [Fact]
    public async Task A_refund_in_a_tender_nobody_recognises_is_refused()
    {
        // Day-close matches the tender string exactly, so "Cash" would be a different drawer.
        var inv = await BilledAsync(net: 550, paid: 550);
        var ex = await Assert.ThrowsAsync<BillingException>(() => RefundAsync(inv, 100, "gold bars"));
        Assert.Contains("not a way money can be handed back", ex.Message);

        // The spelling is normalised rather than refused — the operator gets the drawer they meant.
        var refund = await RefundAsync(inv, 100, "  CASH ");
        Assert.Equal(Tenders.Cash, refund.Tender);
    }

    // ---- M4-D4: the printed statement -------------------------------------

    [Fact]
    public async Task The_day_close_statement_leaves_out_the_invoices_that_were_reversed()
    {
        // A cancelled invoice used to contribute its full Net to a PRINTED financial document
        // with nothing offsetting it, because cancellation writes no receipt.
        var (sessionId, encId) = await SetupEncounterAsync();
        var keep = await InTxAsync(async (bill, kernel) =>
        {
            await _svc.PostChargeAsync(bill, 1, encId, "Billing",
                new NewChargeLine("service", 0, "Consultation", 1, 700), 7);
            return await _svc.CreateInvoiceAsync(bill, kernel, 1, encId, sessionId, 1, 0, 0, null, 7, "Rasel");
        });
        var (_, encId2) = await SetupEncounterAsync();
        var scrap = await InTxAsync(async (bill, kernel) =>
        {
            await _svc.PostChargeAsync(bill, 1, encId2, "Billing",
                new NewChargeLine("service", 0, "Consultation", 1, 900), 7);
            return await _svc.CreateInvoiceAsync(bill, kernel, 1, encId2, sessionId, 1, 0, 0, null, 7, "Rasel");
        });
        await InTxAsync(async (bill, kernel) =>
        {
            await _svc.CancelInvoiceAsync(bill, kernel, 1, scrap.Id, "wrong patient", 7, "Rasel", 1);
            return 0;
        });

        var summary = await InTxAsync(async (bill, kernel) =>
            await DayClose().CloseAsync(bill, kernel, sessionId, 2000, 7, "Rasel"));

        Assert.Equal(700, summary.Gross);                                  // not 1600
        Assert.Equal(700, summary.Net);
        Assert.Equal(summary.Gross - summary.Discount, summary.Net);       // the printed subtraction
        Assert.Equal(700, summary.DueCreated);                             // the cancelled due is 0
        Assert.Equal(keep.Net, summary.Net);
    }

    // ---- M22-D1: the MD's income tile --------------------------------------

    [Fact]
    public async Task Reversed_invoices_do_not_reach_the_income_figure()
    {
        // The dashboard's arithmetic, reached through the same `InvoiceValue.Totalise` the page
        // calls. `golden-thread.py` asserts ৳550 on a FRESH ledger where nothing was reversed,
        // so the defect was invisible by construction to the only test that looked — this one
        // therefore involves actual reversals.
        var live = await BilledAsync(net: 700);
        var cancelled = await BilledAsync(net: 900);
        var refunded = await BilledAsync(net: 550, paid: 550);
        var partly = await BilledAsync(net: 1000, paid: 1000);

        await InTxAsync(async (bill, kernel) =>
        {
            await _svc.CancelInvoiceAsync(bill, kernel, 1, cancelled.InvoiceId, "duplicate", 7, "Rasel", 1);
            return 0;
        });
        await RefundAsync(refunded, 550);
        await RefundAsync(partly, 200);

        var ids = new[] { live.InvoiceId, cancelled.InvoiceId, refunded.InvoiceId, partly.InvoiceId };
        await using var check = CreateBill(null);
        var rows = await check.Invoices.AsNoTracking().Where(i => ids.Contains(i.Id))
            .Select(i => new InvoiceMoneyRow(i.Id, i.PatientId, i.State, i.Net, i.Discount))
            .ToListAsync();
        var refunds = await InvoiceValue.RefundedByInvoiceAsync(check, ids);

        var totals = InvoiceValue.Totalise(rows, refunds);
        Assert.Equal(700 + 800, totals.Income);            // 900 cancelled and 550 refunded are gone
        Assert.Equal(2, totals.Invoices);                  // and neither counts as an invoice raised

        // The two tiles must now agree the way an MD reads them: what was earned is what is in
        // the drawer plus what is still owed. Before the fix they differed by the reversals, and
        // the difference looked like an unpaid due.
        var collected = await check.Receipts.AsNoTracking()
            .Where(r => r.InvoiceId != null && ids.Contains(r.InvoiceId!.Value)).SumAsync(r => r.Amount);
        var outstanding = await check.Dues.AsNoTracking()
            .Where(d => ids.Contains(d.InvoiceId)).SumAsync(d => d.Balance);
        Assert.Equal(800, collected);
        Assert.Equal(700, outstanding);
        Assert.Equal(totals.Income, collected + outstanding);
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
