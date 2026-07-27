using Hms.Billing;
using Hms.Billing.Data;
using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Hms.Pharmacy;
using Hms.Pharmacy.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0017: the folio seam and the M6 machines on real Postgres. The FIRST test posts a
/// folio-parented line through the billing spine — the structural gap the MVP review named
/// (10-mvp-review.md §5: "nothing yet proves the folio-parented path").
/// </summary>
[Collection("postgres")]
public class IpdFolioTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly BillingService _billing = new(
        new NumberSeriesService(), new AuditWriter(TimeProvider.System),
        new FiscalCalendar(7), new BusinessDayCalendar(TimeOnly.MinValue), TimeProvider.System);
    private readonly IpdService _ipd = new(
        new NumberSeriesService(), new AuditWriter(TimeProvider.System),
        new FiscalCalendar(7), TimeProvider.System);
    private readonly FolioService _folios = new(
        new AuditWriter(TimeProvider.System), new BusinessDayCalendar(TimeOnly.MinValue),
        TimeProvider.System);
    private readonly CertificateService _certs = new(
        new NumberSeriesService(), new AuditWriter(TimeProvider.System),
        new FiscalCalendar(7), TimeProvider.System);
    private readonly StockService _stock = new(new AuditWriter(TimeProvider.System), TimeProvider.System);

    public IpdFolioTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var ipd = CreateIpd(null);
        await ipd.Database.MigrateAsync();
        await using var bill = CreateBill(null);
        await bill.Database.MigrateAsync();
        await using var pharm = CreatePharm(null);
        await pharm.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private IpdDbContext CreateIpd(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<IpdDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "ipd"))
            : new DbContextOptionsBuilder<IpdDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "ipd")))
        .UseSnakeCaseNamingConvention().Options);

    private BillDbContext CreateBill(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<BillDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "bill"))
            : new DbContextOptionsBuilder<BillDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "bill")))
        .UseSnakeCaseNamingConvention().Options);

    private PharmDbContext CreatePharm(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<PharmDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "pharm"))
            : new DbContextOptionsBuilder<PharmDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "pharm")))
        .UseSnakeCaseNamingConvention().Options);

    private KernelDbContext CreateKernel(NpgsqlConnection conn) => new(
        new DbContextOptionsBuilder<KernelDbContext>().UseNpgsql(conn,
            o => o.MigrationsHistoryTable("__ef_migrations", "kernel"))
        .UseSnakeCaseNamingConvention().Options);

    /// <summary>ipd + bill + pharm + kernel on ONE connection and transaction (G19).</summary>
    private async Task<T> InTxAsync<T>(
        Func<IpdDbContext, BillDbContext, PharmDbContext, KernelDbContext, Task<T>> body)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var ipd = CreateIpd(conn);
        await using var bill = CreateBill(conn);
        await using var pharm = CreatePharm(conn);
        await using var kernel = CreateKernel(conn);
        await ipd.Database.UseTransactionAsync(tx);
        await bill.Database.UseTransactionAsync(tx);
        await pharm.Database.UseTransactionAsync(tx);
        await kernel.Database.UseTransactionAsync(tx);
        var result = await body(ipd, bill, pharm, kernel);
        await tx.CommitAsync();
        return result;
    }

    private async Task<(long AdmissionId, long FolioId, long BedId)> AdmitPatientAsync(
        long patientId = 0)
    {
        if (patientId == 0) patientId = Random.Shared.NextInt64(100_000, 999_999_999);
        return await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            var ward = new Ward { BranchId = 1, Name = "W-" + Guid.NewGuid().ToString("N")[..6], Class = "general" };
            ipd.Wards.Add(ward);
            await ipd.SaveChangesAsync();
            var bed = new Bed
            {
                BranchId = 1, WardId = ward.Id, Code = "B-" + Guid.NewGuid().ToString("N")[..8],
                TariffServiceId = 1,
            };
            ipd.Beds.Add(bed);
            await ipd.SaveChangesAsync();
            var admission = await _ipd.AdmitAsync(ipd, kernel, 1, patientId, null, "direct", null,
                bed.Id, null, 0, reserveOnly: false, 7, "tester");
            var folio = await ipd.Folios.SingleAsync(f => f.AdmissionId == admission.Id);
            return (admission.Id, folio.Id, bed.Id);
        });
    }

    // ------------------------------------------------------------------ seam

    [Fact]
    public async Task Folio_parented_charge_posts_through_the_spine()
    {
        var (_, folioId, _) = await AdmitPatientAsync();
        var line = await InTxAsync(async (ipd, bill, pharm, kernel) =>
            await _billing.PostFolioChargeAsync(bill, 1, folioId, "Ipd",
                new NewChargeLine("service", 1, "Bed day", 1, 1200), 7));

        Assert.Equal(folioId, line.FolioId);
        Assert.Null(line.EncounterId);

        // The XOR CHECK rejects a parentless line — the seam is a constraint, not a habit.
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            InTxAsync<int>(async (ipd, bill, pharm, kernel) =>
            {
                bill.ChargeLines.Add(new ChargeLine
                {
                    BranchId = 1, SourceModule = "Ipd", CatalogKind = "service", CatalogId = 1,
                    DescriptionSnapshot = "orphan", Qty = 1, UnitPrice = 10, Amount = 10,
                    CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 7,
                });
                await bill.SaveChangesAsync();
                return 0;
            }));
    }

    // ----------------------------------------------------------- concurrency

    [Fact]
    public async Task Two_admissions_to_the_last_bed_serialize()
    {
        var bedId = await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            var ward = new Ward { BranchId = 1, Name = "W-" + Guid.NewGuid().ToString("N")[..6], Class = "cabin" };
            ipd.Wards.Add(ward);
            await ipd.SaveChangesAsync();
            var bed = new Bed
            {
                BranchId = 1, WardId = ward.Id, Code = "B-" + Guid.NewGuid().ToString("N")[..8],
                TariffServiceId = 1,
            };
            ipd.Beds.Add(bed);
            await ipd.SaveChangesAsync();
            return bed.Id;
        });

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            try
            {
                await InTxAsync(async (ipd, bill, pharm, kernel) =>
                    await _ipd.AdmitAsync(ipd, kernel, 1, Random.Shared.NextInt64(1_000_000_000, 2_000_000_000),
                        null, "direct", null, bedId, null, 0, false, 7, "tester"));
                return true;
            }
            catch (IpdException) { return false; }
        }));

        Assert.Single(results, ok => ok);
        var state = await InTxAsync(async (ipd, bill, pharm, kernel) =>
            (await ipd.Beds.AsNoTracking().SingleAsync(b => b.Id == bedId)).State);
        Assert.Equal(BedState.Occupied, state);
    }

    // -------------------------------------------------------------- bed days

    [Fact]
    public async Task Bed_days_are_idempotent_by_constraint()
    {
        var (admissionId, folioId, bedId) = await AdmitPatientAsync();

        // Backdate the admission two days: three business days are owed (P18: through today).
        await InTxAsync(async (ipd, bill, pharm, kernel) =>
            await ipd.Database.ExecuteSqlAsync(
                $"UPDATE ipd.admission SET admitted_at = {DateTimeOffset.UtcNow.AddDays(-2)} WHERE id = {admissionId}"));

        var unposted = await InTxAsync(async (ipd, bill, pharm, kernel) =>
            await _folios.ComputeUnpostedBedDaysAsync(ipd, admissionId));
        Assert.Equal(3, unposted.Dates.Count);

        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            foreach (var date in unposted.Dates)
            {
                var line = await _billing.PostFolioChargeAsync(bill, 1, folioId, "Ipd",
                    new NewChargeLine("service", 1, $"Bed day {date}", 1, 1000), 7);
                _folios.RecordBedDay(ipd, admissionId, date, unposted.BedId, line.Id);
            }
            await ipd.SaveChangesAsync();
            return 0;
        });

        // Second catch-up finds nothing; a forced duplicate dies on the UNIQUE index.
        var again = await InTxAsync(async (ipd, bill, pharm, kernel) =>
            await _folios.ComputeUnpostedBedDaysAsync(ipd, admissionId));
        Assert.Empty(again.Dates);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            InTxAsync<int>(async (ipd, bill, pharm, kernel) =>
            {
                _folios.RecordBedDay(ipd, admissionId, unposted.Dates[0], bedId, 1);
                await ipd.SaveChangesAsync();
                return 0;
            }));
    }

    // ------------------------------------------------------------ settlement

    [Fact]
    public async Task Settlement_applies_advances_and_locks_the_folio()
    {
        var patientId = Random.Shared.NextInt64(100_000, 999_999_999);
        var (admissionId, folioId, _) = await AdmitPatientAsync(patientId);

        var sessionId = await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            var counter = new Counter { BranchId = 1, Name = "C-" + Guid.NewGuid().ToString("N")[..6], Kind = "front-desk" };
            bill.Counters.Add(counter);
            await bill.SaveChangesAsync();
            var session = await _billing.OpenSessionAsync(bill, 1, counter.Id, 7, 0);
            return session.Id;
        });

        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            await _billing.PostFolioChargeAsync(bill, 1, folioId, "Ipd",
                new NewChargeLine("service", 1, "Bed day", 1, 3000), 7);
            await _billing.PostFolioChargeAsync(bill, 1, folioId, "Ipd",
                new NewChargeLine("service", 2, "Oxygen 2h", 2, 150), 7);
            await _billing.CollectAdvanceAsync(bill, kernel, 1, folioId, sessionId, 2000, "cash", null, 7, "tester");
            return 0;
        });

        var (invoice, applied) = await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            await _folios.LockAsync(ipd, folioId);
            await _folios.BeginSettlementAsync(ipd, folioId);
            var result = await _billing.CreateFolioInvoiceAsync(
                bill, kernel, 1, folioId, sessionId, patientId, 0m, 300, null, 7, "tester");
            await _folios.LockAtSettlementAsync(ipd, kernel, folioId, result.Invoice.Id,
                result.AdvanceApplied, 7, "tester");
            return result;
        });

        Assert.Equal(3300, invoice.Gross);                      // 3000 + 2×150
        Assert.Equal(3000, invoice.Net);                        // − 300 discount
        Assert.Equal(2000, applied);                            // advance fully applied
        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            var due = await bill.Dues.AsNoTracking().SingleAsync(d => d.InvoiceId == invoice.Id);
            Assert.Equal(1000, due.Balance);                    // net − advance
            var folio = await ipd.Folios.AsNoTracking().SingleAsync(f => f.Id == folioId);
            Assert.Equal(FolioState.Locked, folio.State);
            Assert.Null(invoice.EncounterId);
            Assert.Equal(folioId, invoice.FolioId);
            return 0;
        });
    }

    [Fact]
    public async Task Excess_advance_returns_through_the_drawer_and_day_close_reconciles()
    {
        var patientId = Random.Shared.NextInt64(100_000, 999_999_999);
        var (admissionId, folioId, _) = await AdmitPatientAsync(patientId);

        var (sessionId, invoiceId) = await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            var counter = new Counter { BranchId = 1, Name = "C-" + Guid.NewGuid().ToString("N")[..6], Kind = "front-desk" };
            bill.Counters.Add(counter);
            await bill.SaveChangesAsync();
            var session = await _billing.OpenSessionAsync(bill, 1, counter.Id, 7, 500);

            await _billing.PostFolioChargeAsync(bill, 1, folioId, "Ipd",
                new NewChargeLine("service", 1, "Bed day", 1, 1000), 7);
            await _billing.CollectAdvanceAsync(bill, kernel, 1, folioId, session.Id, 1500, "cash", null, 7, "t");

            await _folios.LockAsync(ipd, folioId);
            await _folios.BeginSettlementAsync(ipd, folioId);
            var (invoice, applied) = await _billing.CreateFolioInvoiceAsync(
                bill, kernel, 1, folioId, session.Id, patientId, 0m, 0, null, 7, "t");
            Assert.Equal(1000, applied);
            // give back the 500 excess from the drawer
            await _billing.CollectAdvanceAsync(bill, kernel, 1, folioId, session.Id, -500, "cash", null, 7, "t");
            await _folios.LockAtSettlementAsync(ipd, kernel, folioId, invoice.Id, applied, 7, "t");
            return (session.Id, invoice.Id);
        });

        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            var due = await bill.Dues.AsNoTracking().SingleAsync(d => d.InvoiceId == invoiceId);
            Assert.Equal(0, due.Balance);
            var invoice = await bill.Invoices.AsNoTracking().SingleAsync(i => i.Id == invoiceId);
            Assert.Equal(InvoiceState.Paid, invoice.State);

            var dayClose = new DayCloseService(new AuditWriter(TimeProvider.System), new BusinessDayCalendar(TimeOnly.MinValue), TimeProvider.System);
            var summary = await dayClose.CloseAsync(bill, kernel, sessionId, countedCash: 1500, 7, "t");
            // Drawer: 500 float + 1500 advance − 500 returned = 1500. Variance zero.
            Assert.Equal(0, summary.Variance);
            Assert.Equal(1000, summary.AdvancesTaken);          // net advances this session
            return 0;
        });
    }

    // ------------------------------------------------- folio gate (R4 + lock)

    [Fact]
    public async Task Blocked_folio_refuses_posting_and_release_restores()
    {
        var (admissionId, folioId, _) = await AdmitPatientAsync();

        // Bogus approval id → refused (verify-don't-trust).
        await Assert.ThrowsAsync<IpdException>(() =>
            InTxAsync<int>(async (ipd, bill, pharm, kernel) =>
            {
                await _ipd.BlockAsync(ipd, kernel, admissionId, 999_999, 7, "t");
                return 0;
            }));

        var approvalId = await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            kernel.ApprovalRequests.Add(new ApprovalRequest
            {
                BranchId = 1, Type = "patient-block", SourceTable = "ipd.admission",
                SourceId = admissionId, RequestedBy = 7, Reason = "dues",
                RequestedAt = DateTimeOffset.UtcNow, State = ApprovalState.Approved,
            });
            await kernel.SaveChangesAsync();
            return kernel.ApprovalRequests.Local.First().Id;
        });

        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            await _ipd.BlockAsync(ipd, kernel, admissionId, approvalId, 7, "t");
            return 0;
        });

        await Assert.ThrowsAsync<IpdException>(() =>
            InTxAsync<int>(async (ipd, bill, pharm, kernel) =>
            {
                await _folios.EnsurePostableAsync(ipd, kernel, folioId);
                return 0;
            }));

        var releaseId = await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            kernel.ApprovalRequests.Add(new ApprovalRequest
            {
                BranchId = 1, Type = "patient-release", SourceTable = "ipd.admission",
                SourceId = admissionId, RequestedBy = 7, Reason = "paid",
                RequestedAt = DateTimeOffset.UtcNow, State = ApprovalState.Approved,
            });
            await kernel.SaveChangesAsync();
            return kernel.ApprovalRequests.Local.First().Id;
        });

        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            await _ipd.ReleaseAsync(ipd, kernel, admissionId, releaseId, 7, "t");
            var admission = await ipd.Admissions.AsNoTracking().SingleAsync(a => a.Id == admissionId);
            Assert.Equal(AdmissionState.Admitted, admission.State);
            await _folios.EnsurePostableAsync(ipd, kernel, folioId);    // open again
            return 0;
        });
    }

    [Fact]
    public async Task Post_lock_posting_needs_the_supervisor_approval()
    {
        var (admissionId, folioId, _) = await AdmitPatientAsync();
        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            await ipd.Database.ExecuteSqlAsync(
                $"UPDATE ipd.folio SET state = 'locked' WHERE id = {folioId}");
            return 0;
        });

        await Assert.ThrowsAsync<IpdException>(() =>
            InTxAsync<int>(async (ipd, bill, pharm, kernel) =>
            {
                await _folios.EnsurePostableAsync(ipd, kernel, folioId);
                return 0;
            }));

        var approvalId = await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            kernel.ApprovalRequests.Add(new ApprovalRequest
            {
                BranchId = 1, Type = "folio-late-post", SourceTable = "ipd.folio",
                SourceId = folioId, RequestedBy = 7, Reason = "late OT charge",
                RequestedAt = DateTimeOffset.UtcNow, State = ApprovalState.Approved,
            });
            await kernel.SaveChangesAsync();
            return kernel.ApprovalRequests.Local.First().Id;
        });

        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            await _folios.EnsurePostableAsync(ipd, kernel, folioId, approvalId);
            return 0;
        });
    }

    // -------------------------------------------------- §11 machine ordering

    [Fact]
    public async Task Discharge_walks_the_full_machine_and_shortcuts_are_refused()
    {
        var (admissionId, folioId, bedId) = await AdmitPatientAsync();

        // Cannot discharge before the money is settled (§11 order).
        await Assert.ThrowsAsync<IpdException>(() =>
            InTxAsync<int>(async (ipd, bill, pharm, kernel) =>
            {
                await _ipd.DischargeAsync(ipd, kernel, admissionId, 0, null, 7, "t");
                return 0;
            }));

        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            await _ipd.InitiateDischargeAsync(ipd, kernel, admissionId, "well", 7, "t");
            await _ipd.ClinicallyClearAsync(ipd, kernel, admissionId, 7, "t");
            await _ipd.MarkFinanciallySettledAsync(ipd, admissionId);
            await _ipd.DischargeAsync(ipd, kernel, admissionId, 0, null, 7, "t");
            var admission = await ipd.Admissions.AsNoTracking().SingleAsync(a => a.Id == admissionId);
            Assert.Equal(AdmissionState.Discharged, admission.State);
            var bed = await ipd.Beds.AsNoTracking().SingleAsync(b => b.Id == bedId);
            Assert.Equal(BedState.Cleaning, bed.State);
            var stay = await ipd.BedStays.AsNoTracking().SingleAsync(x => x.AdmissionId == admissionId);
            Assert.NotNull(stay.ToAt);
            return 0;
        });
    }

    /// <summary>
    /// Spec 0021: a patient blocked (R4) at the moment of settlement keeps the blocked state
    /// through it, so nothing promotes them to "financially settled". On release they return
    /// to `clinically_cleared` with a locked, already-invoiced folio — and used to be stuck in
    /// the ward forever, because a folio cannot be settled twice. Discharge must recognise the
    /// settlement that plainly happened.
    /// </summary>
    [Fact]
    public async Task A_folio_settled_while_blocked_can_still_be_discharged_after_release()
    {
        var (admissionId, folioId, bedId) = await AdmitPatientAsync();
        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            await _ipd.InitiateDischargeAsync(ipd, kernel, admissionId, "well", 7, "t");
            await _ipd.ClinicallyClearAsync(ipd, kernel, admissionId, 7, "t");
            // Settled, but the admission never reached financially_settled (it was blocked).
            await ipd.Database.ExecuteSqlAsync(
                $"UPDATE ipd.folio SET state = 'locked', settlement_invoice_id = 4242 WHERE id = {folioId}");
            return 0;
        });

        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            await _ipd.DischargeAsync(ipd, kernel, admissionId, 0, null, 7, "t");
            var admission = await ipd.Admissions.AsNoTracking().SingleAsync(a => a.Id == admissionId);
            Assert.Equal(AdmissionState.Discharged, admission.State);
            var bed = await ipd.Beds.AsNoTracking().SingleAsync(b => b.Id == bedId);
            Assert.Equal(BedState.Cleaning, bed.State);          // the bed is freed, not stuck
            return 0;
        });
    }

    // ------------------------------------------------ indoor return restocks

    [Fact]
    public async Task Indent_return_restocks_the_exact_batches()
    {
        var (outletId, productId, batchId) = await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            var company = new Company { Name = "C-" + Guid.NewGuid().ToString("N")[..6] };
            pharm.Companies.Add(company);
            var outlet = new Outlet { BranchId = 1, Name = "O-" + Guid.NewGuid().ToString("N")[..6], Kind = "main" };
            pharm.Outlets.Add(outlet);
            await pharm.SaveChangesAsync();
            var product = new PharmProduct
            {
                BranchId = 1, CompanyId = company.Id, Brand = "B-" + Guid.NewGuid().ToString("N")[..6],
                Generic = "G", Strength = "500 mg", Form = "tablet", Unit = "pcs", ReorderLevel = 5,
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            pharm.Products.Add(product);
            await pharm.SaveChangesAsync();
            var batch = await _stock.ReceiveAsync(pharm, kernel, 1, outlet.Id, product.Id, "IND-01",
                DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), 20, 8, 12, null, 7, "t");
            return (outlet.Id, product.Id, batch.Id);
        });

        // Issue 10 via FEFO against a fake indent id, keeping the allocation trail.
        const long indentId = 424242;
        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            var allocations = await _stock.AllocateFefoAsync(pharm, outletId, productId, 10,
                "ipd.indent", indentId, 7);
            foreach (var a in allocations)
                pharm.IssueAllocations.Add(new IssueAllocation
                {
                    BranchId = 1, IndentId = indentId, ChargeLineId = 1, BatchId = a.BatchId,
                    ProductId = a.ProductId, Qty = a.Qty, UnitMrp = a.UnitMrp, UnitCost = a.UnitCost,
                });
            await pharm.SaveChangesAsync();
            return 0;
        });

        var lines = await InTxAsync(async (ipd, bill, pharm, kernel) =>
            await _stock.RestockIndentAsync(pharm, indentId, productId, 4, 7));

        Assert.Single(lines);
        Assert.Equal(4, lines[0].Qty);
        Assert.Equal(12, lines[0].UnitMrp);                      // reverses the charged MRP
        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            var batch = await pharm.Batches.AsNoTracking().SingleAsync(b => b.Id == batchId);
            Assert.Equal(14, batch.QtyOnHand);                   // 20 − 10 + 4
            return 0;
        });

        // Over-return refused: only 6 remain returnable.
        await Assert.ThrowsAsync<PharmacyException>(() =>
            InTxAsync<int>(async (ipd, bill, pharm, kernel) =>
            {
                await _stock.RestockIndentAsync(pharm, indentId, productId, 7, 7);
                return 0;
            }));
    }

    // ---------------------------------------------------------- certificates

    [Fact]
    public async Task Certificates_number_sequentially_and_reprints_are_counted()
    {
        var (admissionId, _, _) = await AdmitPatientAsync();
        await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            await ipd.Database.ExecuteSqlAsync(
                $"UPDATE ipd.admission SET state = 'financially_settled' WHERE id = {admissionId}");
            return 0;
        });

        var (first, second) = await InTxAsync(async (ipd, bill, pharm, kernel) =>
        {
            var a = await _certs.IssueAsync(ipd, kernel, 1, admissionId, "discharge", "{}", 7, "t");
            var b = await _certs.IssueAsync(ipd, kernel, 1, admissionId, "discharge", "{}", 7, "t");
            return (a, b);
        });
        Assert.StartsWith("DIS-", first.CertNo);
        Assert.NotEqual(first.CertNo, second.CertNo);

        var reprinted = await InTxAsync(async (ipd, bill, pharm, kernel) =>
            await _certs.ReprintAsync(ipd, kernel, first.Id, 7, "t"));
        Assert.Equal(2, reprinted.PrintCount);
        Assert.Equal(first.CertNo, reprinted.CertNo);            // never a new number

        // A death certificate on a settled (not death) admission is refused.
        await Assert.ThrowsAsync<IpdException>(() =>
            InTxAsync<int>(async (ipd, bill, pharm, kernel) =>
            {
                await _certs.IssueAsync(ipd, kernel, 1, admissionId, "death", "{}", 7, "t");
                return 0;
            }));
    }
}
