using Hms.Admin;
using Hms.Admin.Data;
using Hms.Billing;
using Hms.Emr;
using Hms.Emr.Data;
using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Hms.Lis;
using Hms.Pharmacy;
using Hms.Pharmacy.Data;
using Hms.Web;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0042: the composition-root money seams of the ward, tested through the SAME orchestrators
/// the pages call. The 0042 audit found that every prior test drove the module services directly,
/// so <c>IpdBilling.OrderTestsAsync</c>, <c>IssueIndentAsync</c> and <c>PostServiceAsync</c> — the
/// three paths that turn ward events into folio money — had zero coverage; the indent test even
/// bypassed the orchestrator with a hand-built allocation. These tests construct a real
/// <see cref="TxScope"/> over the fixture's connection, which is exactly what <c>HmsTx</c> does.
/// </summary>
[Collection("postgres")]
public class WardMoneySeamTests : IAsyncLifetime
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
    private readonly EmrService _emr = new(new AuditWriter(TimeProvider.System), TimeProvider.System);
    private readonly StockService _stock = new(new AuditWriter(TimeProvider.System), TimeProvider.System);
    private readonly LisService _lis = new(TimeProvider.System);
    private readonly RateResolver _rates = new();
    private readonly AuditWriter _audit = new(TimeProvider.System);

    public WardMoneySeamTests(PostgresFixture pg) => _pg = pg;

    private long _visitServiceId;
    private long _testCatalogId;

    public async Task InitializeAsync()
    {
        // Migrate every schema this seam touches, each on its own connection — migrations must
        // not run inside the test's explicit transaction. Idempotent across the collection.
        await MigrateAsync<AdmDbContext>("adm_data", o => new(o));
        await MigrateAsync<EmrDbContext>("emr", o => new(o));
        await MigrateAsync<IpdDbContext>("ipd", o => new(o));
        await MigrateAsync<Hms.Billing.Data.BillDbContext>("bill", o => new(o));
        await MigrateAsync<PharmDbContext>("pharm", o => new(o));
        await MigrateAsync<Hms.Diagnostics.Data.DiagDbContext>("diag", o => new(o));
        await MigrateAsync<Hms.Lis.Data.LisDbContext>("lis", o => new(o));

        // The IPD-VISIT master and its effective-dated rate — what DevSeed provides on a dev box.
        await using var adm = CreateAdm();
        var visit = await adm.Services.FirstOrDefaultAsync(x => x.Code == "IPD-VISIT");
        if (visit is null)
        {
            visit = new Service { Code = "IPD-VISIT", Name = "Consultant Visit (indoor)", Dept = "IPD", Kind = "other" };
            adm.Services.Add(visit);
            await adm.SaveChangesAsync();
            adm.RateVersions.Add(new RateVersion
            {
                BranchId = 1, CatalogKind = "service", CatalogId = visit.Id, Price = 800,
                ValidFrom = new DateOnly(2026, 1, 1), AuthorId = 1,
            });
            await adm.SaveChangesAsync();
        }
        _visitServiceId = visit.Id;

        var test = await adm.TestCatalog.FirstOrDefaultAsync(x => x.Code == "WARD-CBC");
        if (test is null)
        {
            test = new TestCatalogItem
            {
                Code = "WARD-CBC", Name = "CBC (ward seam)", Dept = "haematology",
                SampleTypes = ["blood"], TatMinutes = 120,
            };
            adm.TestCatalog.Add(test);
            await adm.SaveChangesAsync();
            adm.RateVersions.Add(new RateVersion
            {
                BranchId = 1, CatalogKind = "test", CatalogId = test.Id, Price = 500,
                ValidFrom = new DateOnly(2026, 1, 1), AuthorId = 1,
            });
            await adm.SaveChangesAsync();
        }
        _testCatalogId = test.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task MigrateAsync<T>(string schema, Func<DbContextOptions<T>, T> factory)
        where T : DbContext
    {
        await using var ctx = factory(new DbContextOptionsBuilder<T>()
            .UseNpgsql(_pg.ConnectionString, o => o.MigrationsHistoryTable("__ef_migrations", schema))
            .UseSnakeCaseNamingConvention().Options);
        await ctx.Database.MigrateAsync();
    }

    private AdmDbContext CreateAdm() => new(
        new DbContextOptionsBuilder<AdmDbContext>().UseNpgsql(_pg.ConnectionString,
            o => o.MigrationsHistoryTable("__ef_migrations", "adm_data"))
        .UseSnakeCaseNamingConvention().Options);

    private EmrDbContext CreateEmr() => new(
        new DbContextOptionsBuilder<EmrDbContext>().UseNpgsql(_pg.ConnectionString,
            o => o.MigrationsHistoryTable("__ef_migrations", "emr"))
        .UseSnakeCaseNamingConvention().Options);

    /// <summary>One business action, exactly as HmsTx runs it: one connection, one transaction,
    /// every context attached, flush-then-commit.</summary>
    private async Task<T> InScopeAsync<T>(Func<TxScope, Task<T>> body)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var scope = new TxScope(conn, tx);
        var result = await body(scope);
        await scope.SaveAllAsync();
        await tx.CommitAsync();
        return result;
    }

    private async Task<(long AdmissionId, long FolioId, long PatientId)> AdmitAsync()
    {
        var patientId = Random.Shared.NextInt64(2_000_000_000, 3_000_000_000);
        return await InScopeAsync(async s =>
        {
            var ward = new Ward { BranchId = 1, Name = "W-" + Guid.NewGuid().ToString("N")[..6], Class = "general" };
            s.Ipd.Wards.Add(ward);
            await s.Ipd.SaveChangesAsync();
            var bed = new Bed
            {
                BranchId = 1, WardId = ward.Id, Code = "B-" + Guid.NewGuid().ToString("N")[..8],
                TariffServiceId = 1,
            };
            s.Ipd.Beds.Add(bed);
            await s.Ipd.SaveChangesAsync();
            var admission = await _ipd.AdmitAsync(s.Ipd, s.Kernel, 1, patientId, null, "direct",
                null, bed.Id, null, 0, reserveOnly: false, 7, "tester");
            var folio = await s.Ipd.Folios.SingleAsync(f => f.AdmissionId == admission.Id);
            return (admission.Id, folio.Id, patientId);
        });
    }

    private async Task<long> SignedNoteAsync(long admissionId, long doctorId)
        => await InScopeAsync(async s =>
        {
            var note = await _emr.OpenDraftAsync(s.Emr, 1, admissionId, null, admissionId, doctorId);
            await _emr.SaveDraftAsync(s.Emr, note.Id,
                new NoteBody("Round", null, "Stable", null, null),
                [new DrugLine(null, "Napa 500", "1 tab", "1+0+1", "3 days", null)]);
            await _emr.FinaliseAsync(s.Emr, s.Kernel, note.Id, 1, doctorId, "Dr Seam");
            return note.Id;
        });

    // ---- consultant visit (the [M] gap) ------------------------------------------------------

    [Fact]
    public async Task Signing_records_one_visit_and_one_charge_per_doctor_per_day()
    {
        var (admissionId, folioId, _) = await AdmitAsync();
        var noteId = await SignedNoteAsync(admissionId, doctorId: 501);

        var first = await InScopeAsync(s => IpdBilling.PostConsultantVisitAsync(
            s, _billing, _folios, _rates, _audit, TimeProvider.System, 1,
            admissionId, 501, noteId, 9, "Nasrin"));

        Assert.True(first.Charged);
        Assert.NotNull(first.Visit.ChargeLineId);

        var lines = await InScopeAsync(s => s.Bill.ChargeLines.AsNoTracking()
            .Where(c => c.FolioId == folioId && c.CatalogId == _visitServiceId).ToListAsync());
        Assert.Single(lines);
        Assert.Equal(800, lines[0].Amount);                  // the rate plan's price, never typed
        Assert.Equal((long?)501, lines[0].DoctorId);         // the signer, not a cashier's guess

        // The same doctor's second note the same day: a longer round, not a second fee.
        var secondNote = await SignedNoteAsync(admissionId, doctorId: 501);
        var second = await InScopeAsync(s => IpdBilling.PostConsultantVisitAsync(
            s, _billing, _folios, _rates, _audit, TimeProvider.System, 1,
            admissionId, 501, secondNote, 9, "Nasrin"));
        Assert.False(second.Charged);
        Assert.Equal(first.Visit.Id, second.Visit.Id);

        // A different consultant the same day is a different visit and a different fee.
        var otherNote = await SignedNoteAsync(admissionId, doctorId: 502);
        var other = await InScopeAsync(s => IpdBilling.PostConsultantVisitAsync(
            s, _billing, _folios, _rates, _audit, TimeProvider.System, 1,
            admissionId, 502, otherNote, 9, "Nasrin"));
        Assert.True(other.Charged);

        Assert.Equal(2, await InScopeAsync(s => s.Bill.ChargeLines.AsNoTracking()
            .CountAsync(c => c.FolioId == folioId && c.CatalogId == _visitServiceId)));
        Assert.Equal(2, await InScopeAsync(s => s.Ipd.ConsultantVisits.AsNoTracking()
            .CountAsync(v => v.AdmissionId == admissionId)));
    }

    [Fact]
    public async Task A_blocked_folio_records_the_visit_without_a_charge()
    {
        // The round happened — that is a clinical and payout fact regardless of the R4 hold.
        // The money follows the late-post path once the hold clears; it must not block signing.
        var (admissionId, folioId, _) = await AdmitAsync();
        var noteId = await SignedNoteAsync(admissionId, doctorId: 503);
        await InScopeAsync(s => s.Ipd.Database.ExecuteSqlAsync(
            $"UPDATE ipd.folio SET state = 'blocked' WHERE id = {folioId}"));

        var result = await InScopeAsync(s => IpdBilling.PostConsultantVisitAsync(
            s, _billing, _folios, _rates, _audit, TimeProvider.System, 1,
            admissionId, 503, noteId, 9, "Nasrin"));

        Assert.False(result.Charged);
        Assert.Null(result.Visit.ChargeLineId);
        Assert.Equal(0, await InScopeAsync(s => s.Bill.ChargeLines.AsNoTracking()
            .CountAsync(c => c.FolioId == folioId && c.CatalogId == _visitServiceId)));
    }

    // ---- indoor lab seam (first-ever coverage) -----------------------------------------------

    [Fact]
    public async Task An_indoor_test_order_charges_the_folio_and_raises_samples()
    {
        var (_, folioId, _) = await AdmitAsync();

        var orderId = await InScopeAsync(s => IpdBilling.OrderTestsAsync(
            s, _billing, _folios, _rates, _lis, TimeProvider.System, 1, folioId,
            [_testCatalogId], doctorId: 501, actorId: 9));

        var order = await InScopeAsync(s => s.Diag.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId));
        Assert.Equal(folioId, order.FolioId);
        Assert.Null(order.EncounterId);
        Assert.Null(order.InvoiceId);                         // folio money, not an outdoor invoice

        var charge = await InScopeAsync(s => s.Bill.ChargeLines.AsNoTracking()
            .SingleAsync(c => c.FolioId == folioId && c.CatalogKind == "test"));
        Assert.Equal(500, charge.Amount);

        // The tube exists the moment the order does — §11's indoor branch has no invoice gate.
        var orderTestIds = await InScopeAsync(s => s.Diag.OrderTests.AsNoTracking()
            .Where(t => t.TestOrderId == orderId).Select(t => t.Id).ToListAsync());
        Assert.True(await InScopeAsync(s => s.Lis.SampleTests.AsNoTracking()
            .AnyAsync(st => orderTestIds.Contains(st.OrderTestId))));
    }

    [Fact]
    public async Task A_blocked_folio_refuses_an_indoor_test_order()
    {
        var (_, folioId, _) = await AdmitAsync();
        await InScopeAsync(s => s.Ipd.Database.ExecuteSqlAsync(
            $"UPDATE ipd.folio SET state = 'blocked' WHERE id = {folioId}"));

        await Assert.ThrowsAsync<IpdException>(() => InScopeAsync(s => IpdBilling.OrderTestsAsync(
            s, _billing, _folios, _rates, _lis, TimeProvider.System, 1, folioId,
            [_testCatalogId], null, 9)));
    }

    // ---- indent issue seam (first-ever real-path coverage) -----------------------------------

    [Fact]
    public async Task Issuing_an_indent_charges_the_folio_at_each_batch_mrp()
    {
        var (admissionId, folioId, _) = await AdmitAsync();

        var (outletId, productId) = await InScopeAsync(async s =>
        {
            var company = new Company { Name = "C-" + Guid.NewGuid().ToString("N")[..6] };
            s.Pharm.Companies.Add(company);
            var outlet = new Outlet { BranchId = 1, Name = "O-" + Guid.NewGuid().ToString("N")[..6], Kind = "main" };
            s.Pharm.Outlets.Add(outlet);
            await s.Pharm.SaveChangesAsync();
            var product = new PharmProduct
            {
                BranchId = 1, CompanyId = company.Id, Brand = "B-" + Guid.NewGuid().ToString("N")[..6],
                Generic = "G", Strength = "500 mg", Form = "tablet", Unit = "pcs", ReorderLevel = 10,
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            };
            s.Pharm.Products.Add(product);
            await s.Pharm.SaveChangesAsync();
            await _stock.ReceiveAsync(s.Pharm, s.Kernel, 1, outlet.Id, product.Id, "BATCH-A",
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(365), 100, 5, 8, null, 7, "Tester");
            return (outlet.Id, product.Id);
        });

        var indentId = await InScopeAsync(async s =>
            (await _folios.CreateIndentAsync(s.Ipd, s.Kernel, 1, admissionId,
                [(productId, 3)], "ward seam", 9, "Nasrin")).Id);

        await InScopeAsync(async s =>
        {
            await IpdBilling.IssueIndentAsync(s, _billing, _folios, _stock, 1, indentId, outletId, 7);
            return 0;
        });

        var line = await InScopeAsync(s => s.Bill.ChargeLines.AsNoTracking()
            .SingleAsync(c => c.FolioId == folioId && c.CatalogKind == "medicine"));
        Assert.Equal(3, line.Qty);
        Assert.Equal(8, line.UnitPrice);                     // the batch MRP, not a typed price
        Assert.Equal(24, line.Amount);

        var item = await InScopeAsync(s => s.Ipd.IndentItems.AsNoTracking()
            .SingleAsync(i => i.IndentId == indentId));
        Assert.Equal(3, item.QtyIssued);
        Assert.True(await InScopeAsync(s => s.Pharm.IssueAllocations.AsNoTracking()
            .AnyAsync(a => a.IndentId == indentId && a.ChargeLineId == line.Id)));
    }

    // ---- generic service post (first-ever coverage) ------------------------------------------

    [Fact]
    public async Task Posting_a_ward_service_resolves_the_rate_and_respects_the_gate()
    {
        var (_, folioId, _) = await AdmitAsync();

        var lineId = await InScopeAsync(s => IpdBilling.PostServiceAsync(
            s, _billing, _folios, _rates, TimeProvider.System, 1, folioId,
            _visitServiceId, qty: 2, doctorId: null, latePostApprovalId: null, actorId: 9));

        var line = await InScopeAsync(s => s.Bill.ChargeLines.AsNoTracking()
            .SingleAsync(c => c.Id == lineId));
        Assert.Equal(1600, line.Amount);                     // 2 × the rate plan's 800

        await InScopeAsync(s => s.Ipd.Database.ExecuteSqlAsync(
            $"UPDATE ipd.folio SET state = 'blocked' WHERE id = {folioId}"));
        await Assert.ThrowsAsync<IpdException>(() => InScopeAsync(s => IpdBilling.PostServiceAsync(
            s, _billing, _folios, _rates, TimeProvider.System, 1, folioId,
            _visitServiceId, 1, null, null, 9)));
    }

    // ---- ward write guards (F10) -------------------------------------------------------------

    [Fact]
    public async Task Ward_writes_require_a_live_admission_and_close_out_only_an_existing_one()
    {
        var (admissionId, _, _) = await AdmitAsync();
        await InScopeAsync(s => s.Ipd.Database.ExecuteSqlAsync(
            $"UPDATE ipd.admission SET state = 'discharged', discharged_at = {DateTimeOffset.UtcNow} WHERE id = {admissionId}"));

        // New clinical work on a closed admission is refused with a sentence…
        var refused = await Assert.ThrowsAsync<EmrException>(() =>
            InScopeAsync(s => WardGuard.RequireLiveAsync(s, admissionId)));
        Assert.Contains("closed", refused.Message, StringComparison.OrdinalIgnoreCase);

        // …but the record itself is still reachable for close-out actions.
        var admission = await InScopeAsync(s => WardGuard.RequireAsync(s, admissionId));
        Assert.Equal(AdmissionState.Discharged, admission.State);

        // A fabricated id is refused by both.
        await Assert.ThrowsAsync<EmrException>(() =>
            InScopeAsync(s => WardGuard.RequireAsync(s, 999_999_999)));
        await Assert.ThrowsAsync<EmrException>(() =>
            InScopeAsync(s => WardGuard.RequireLiveAsync(s, 999_999_999)));
    }

    [Fact]
    public async Task Open_clinical_work_survives_discharge_and_stays_closable()
    {
        // The 0042 audit's worst finding: at discharge every open dose and task became invisible
        // and un-closable. The service layer must keep them closable; the read-only screens ride
        // on WardGuard.RequireAsync above.
        var (admissionId, _, _) = await AdmitAsync();

        var (taskId, doseId) = await InScopeAsync(async s =>
        {
            var task = await _emr.CreateTaskAsync(s.Emr, s.Kernel, 1, admissionId,
                "Remove cannula", null, null, null, 9, "Nasrin");
            var dose = await _emr.ScheduleDoseAsync(s.Emr, 1, admissionId, "Ceftriaxone", "1 g",
                "IV", DateTimeOffset.UtcNow.AddHours(-2), 9);
            return (task.Id, dose.Id);
        });

        await InScopeAsync(s => s.Ipd.Database.ExecuteSqlAsync(
            $"UPDATE ipd.admission SET state = 'discharged', discharged_at = {DateTimeOffset.UtcNow} WHERE id = {admissionId}"));

        await InScopeAsync(async s =>
        {
            await _emr.AdministerAsync(s.Emr, s.Kernel, 1, doseId, DoseState.Missed,
                "Patient discharged before the evening dose", 9, "Nasrin");
            await _emr.CancelTaskAsync(s.Emr, s.Kernel, 1, taskId,
                "Cannula removed at discharge", 9, "Nasrin");
            return 0;
        });

        var dose = await InScopeAsync(s => s.Emr.MarDoses.AsNoTracking().SingleAsync(d => d.Id == doseId));
        var task = await InScopeAsync(s => s.Emr.CareTasks.AsNoTracking().SingleAsync(t => t.Id == taskId));
        Assert.Equal(DoseState.Missed, dose.State);
        Assert.Equal(TaskState.Cancelled, task.State);
        Assert.NotNull(dose.StateReason);                     // a person's reason, not a machine's
    }

    [Fact]
    public async Task Death_leaves_open_work_queryable_for_the_clearance_surfaces()
    {
        // First xunit coverage of RecordDeathAsync at all. Death is unannounced — the ward is
        // guaranteed to hold open work at that instant, and the surfacing screens read exactly
        // this query shape.
        var (admissionId, _, _) = await AdmitAsync();
        await InScopeAsync(async s =>
        {
            await _emr.CreateTaskAsync(s.Emr, s.Kernel, 1, admissionId, "Turn 2-hourly", null, null, null, 9, "Nasrin");
            await _emr.ScheduleDoseAsync(s.Emr, 1, admissionId, "Napa", "1 tab", null,
                DateTimeOffset.UtcNow.AddHours(1), 9);
            return 0;
        });

        await InScopeAsync(async s =>
        {
            await _ipd.RecordDeathAsync(s.Ipd, s.Kernel, admissionId, 7, "Dr Seam");
            return 0;
        });

        var admission = await InScopeAsync(s => s.Ipd.Admissions.AsNoTracking().SingleAsync(a => a.Id == admissionId));
        Assert.Equal(AdmissionState.Death, admission.State);
        Assert.NotNull(admission.DischargedAt);
        Assert.False(await InScopeAsync(s => s.Ipd.BedStays.AsNoTracking()
            .AnyAsync(st => st.AdmissionId == admissionId && st.ToAt == null)));

        Assert.Equal(1, await InScopeAsync(s => s.Emr.CareTasks.AsNoTracking()
            .CountAsync(t => t.AdmissionId == admissionId && t.State == TaskState.Open)));
        Assert.Equal(1, await InScopeAsync(s => s.Emr.MarDoses.AsNoTracking()
            .CountAsync(d => d.AdmissionId == admissionId && d.State == DoseState.Scheduled)));
    }
}
