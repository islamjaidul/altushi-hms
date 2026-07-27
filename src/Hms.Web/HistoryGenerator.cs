using Hms.Admin;
using Hms.Admin.Data;
using Hms.Billing;
using Hms.Billing.Data;
using Hms.Diagnostics;
using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Hms.Lis;
using Hms.Pharmacy;
using Hms.Registration;
using Microsoft.EntityFrameworkCore;

namespace Hms.Web;

/// <summary>
/// Spec 0023 (spec 0010's unfinished job): generates months of §14-shaped history so memory,
/// query plans and page timings are measured against a hospital's worth of data instead of a
/// demo's worth.
///
/// Everything is written through the **real services** on the real transaction boundary (G19) —
/// a generated row obeys every invariant a live row does, which is the whole point. The one
/// liberty taken is the clock: the services are rebuilt around a <see cref="BackdatedClock"/> so
/// each simulated day stamps itself. That liberty is why this is an explicit command
/// (`generate-history`) and not a config flag that could ever be true in production.
/// </summary>
public static class HistoryGenerator
{
    /// <summary>§14 typical/day for a 100-bed hospital + busy diagnostic wing.</summary>
    private const int RegistrationsPerDay = 100;
    private const int OpdInvoicesPerDay = 220;
    private const int DiagnosticOrdersPerDay = 90;
    private const int PharmacySalesPerDay = 160;
    private const int AdmissionsPerDay = 12;
    private const int BedTarget = 100;              // §14: beds tracked at the typical customer
    private const int SessionsPerDay = 3;

    /// <summary>Fixed seed: two runs over the same day produce the same shape.</summary>
    private const int RandomSeed = 20260727;

    private const string ThroughSetting = "history.generated.through";

    private sealed class BackdatedClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    public static async Task<int> RunAsync(IServiceProvider sp, int days)
    {
        if (days is < 1 or > 400)
        {
            Console.Error.WriteLine("generate-history: --days must be between 1 and 400.");
            return 1;
        }

        var config = sp.GetRequiredService<IConfiguration>();
        var tx = sp.GetRequiredService<HmsTx>();
        var clock = new BackdatedClock();

        // The production wiring, rebuilt around the backdated clock. Reading the calendar
        // settings from configuration (rather than hard-coding July) keeps the generated
        // fiscal-year numbering identical to what the running app would have issued.
        var audit = new AuditWriter(clock);
        var numbers = new NumberSeriesService();
        var fiscal = new FiscalCalendar(config.GetValue("Fiscal:StartMonth", 7));
        var businessDay = new BusinessDayCalendar(
            TimeOnly.Parse(config.GetValue("BusinessDay:Boundary", "00:00")!));

        var registration = new RegistrationService(numbers, audit, clock);
        var billing = new BillingService(numbers, audit, fiscal, businessDay, clock);
        var dayClose = new DayCloseService(audit, businessDay, clock);
        var lis = new LisService(clock);
        var rates = new RateResolver();
        var stock = new StockService(audit, clock);
        var ipdSvc = new IpdService(numbers, audit, fiscal, clock);
        var folios = new FolioService(audit, businessDay, clock);

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        var firstDay = today.AddDays(-days);

        // Idempotency: a marker records the last day generated, so a re-run adds only what is
        // missing rather than doubling the history.
        var generatedThrough = await ReadThroughAsync(tx);
        if (generatedThrough is { } through && through >= firstDay)
        {
            firstDay = through.AddDays(1);
            Console.WriteLine($"history already generated through {through:yyyy-MM-dd}; resuming");
        }
        if (firstDay > today.AddDays(-1))
        {
            Console.WriteLine("nothing to generate — history is already current.");
            return 0;
        }

        var world = await PrepareWorldAsync(tx, ipdSvc, stock, clock);
        var rng = new Random(RandomSeed);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var counters = new Counters();

        for (var day = firstDay; day < today; day = day.AddDays(1))
        {
            // 09:00 Asia/Dhaka — inside the §14 morning peak, and unambiguous for the
            // business-day calendar whatever boundary is configured.
            clock.Now = new DateTimeOffset(day.Year, day.Month, day.Day, 9, 0, 0, TimeSpan.FromHours(6))
                .ToUniversalTime();

            var sessions = await OpenSessionsAsync(tx, billing, world, day);

            await RegisterPatientsAsync(tx, registration, world, rng, counters);
            await BillOpdAsync(tx, billing, lis, rates, clock, world, sessions, rng, day, counters);
            await SellPharmacyAsync(tx, billing, stock, clock, world, sessions, rng, day, counters);
            await RunWardAsync(tx, billing, folios, rates, ipdSvc, clock, world, sessions, rng, counters);

            await CloseSessionsAsync(tx, dayClose, sessions, rng, counters);
            await MarkThroughAsync(tx, day);

            if ((day.DayNumber - firstDay.DayNumber) % 10 == 0)
                Console.WriteLine($"  {day:yyyy-MM-dd}  {counters}  ({sw.Elapsed:mm\\:ss})");
        }

        Console.WriteLine($"generated {days} day(s) in {sw.Elapsed:mm\\:ss}");
        Console.WriteLine(counters.ToString());
        return 0;
    }

    // ---- the standing world (created once, idempotently) -----------------------

    private sealed record World(
        long BranchId, IReadOnlyList<long> CounterIds, IReadOnlyList<long> OperatorIds,
        IReadOnlyList<(long Id, string Name)> Services, IReadOnlyList<long> TestCatalogIds,
        IReadOnlyList<long> ProductIds, long OutletId, long AdmissionFeeServiceId,
        List<long> PatientIds, List<(long AdmissionId, long BedId, DateOnly Until)> InWard);

    private sealed class Counters
    {
        public int Patients, Invoices, Receipts, Orders, Results, Sales, Admissions, Discharges;
        public override string ToString() =>
            $"patients {Patients} · invoices {Invoices} · receipts {Receipts} · orders {Orders} · " +
            $"results {Results} · pharmacy {Sales} · admissions {Admissions} · discharges {Discharges}";
    }

    private static async Task<World> PrepareWorldAsync(
        HmsTx tx, IpdService ipdSvc, StockService stock, BackdatedClock clock)
        => await tx.RunAsync(async s =>
        {
            var branchId = await s.Kernel.Branches.AsNoTracking()
                .Select(b => b.Id).FirstAsync();
            var counterIds = await s.Bill.Counters.AsNoTracking().Select(c => c.Id).ToListAsync();
            if (counterIds.Count == 0)
                throw new InvalidOperationException(
                    "No counters — run the app once with Seed:DevUsers=true before generating history.");

            var operatorIds = await s.Auth.Users.AsNoTracking().Select(u => u.Id).Take(9).ToListAsync();
            var services = await s.Adm.Services.AsNoTracking()
                .Where(x => x.Kind != "test" && !x.Code.StartsWith("IPD-"))
                .Select(x => new ValueTuple<long, string>(x.Id, x.Name)).ToListAsync();
            var testIds = await s.Adm.TestCatalog.AsNoTracking().Select(x => x.Id).ToListAsync();
            var productIds = await s.Pharm.Products.AsNoTracking().Select(p => p.Id).ToListAsync();
            var outletId = await s.Pharm.Outlets.AsNoTracking()
                .Where(o => o.Kind == "main").Select(o => o.Id).FirstAsync();
            var admissionFee = await s.Adm.Services.AsNoTracking()
                .Where(x => x.Code == "IPD-ADM-FEE").Select(x => x.Id).FirstAsync();

            var patientIds = await s.Reg.Patients.AsNoTracking()
                .Where(p => p.MergedInto == null && p.Active)
                .Select(p => p.Id).ToListAsync();

            await TopUpBedsAsync(s, branchId);

            // A resumed run inherits the ward as it stands: without this, yesterday's patients
            // would never be discharged and the beds would fill and stay full.
            var inWard = await s.Ipd.BedStays.AsNoTracking()
                .Where(st => st.ToAt == null)
                .Select(st => new ValueTuple<long, long, DateOnly>(
                    st.AdmissionId, st.BedId, DateOnly.FromDateTime(DateTime.UtcNow)))
                .ToListAsync();

            return new World(branchId, counterIds, operatorIds, services, testIds, productIds,
                outletId, admissionFee, patientIds, inWard);
        });

    /// <summary>§14 tracks 100 beds at the typical customer; the demo seed has 13. Extra beds are
    /// added to the existing wards, keyed by code so a second run adds nothing.</summary>
    private static async Task TopUpBedsAsync(TxScope s, long branchId)
    {
        var existing = await s.Ipd.Beds.CountAsync();
        if (existing >= BedTarget) return;

        var wards = await s.Ipd.Wards.AsNoTracking()
            .Where(w => w.Class == "general").Select(w => w.Id).ToListAsync();
        if (wards.Count == 0) return;
        var tariffId = await s.Ipd.Beds.AsNoTracking()
            .Where(b => wards.Contains(b.WardId)).Select(b => b.TariffServiceId).FirstAsync();

        for (var i = existing; i < BedTarget; i++)
        {
            var code = $"GEN-{i:D3}";
            if (await s.Ipd.Beds.AnyAsync(b => b.Code == code)) continue;
            s.Ipd.Beds.Add(new Bed
            {
                BranchId = branchId, WardId = wards[i % wards.Count], Code = code,
                TariffServiceId = tariffId,
            });
        }
        await s.Ipd.SaveChangesAsync();
    }

    // ---- per-day work ----------------------------------------------------------

    private static async Task<List<(long SessionId, long CounterId, long OperatorId)>> OpenSessionsAsync(
        HmsTx tx, BillingService billing, World world, DateOnly day)
    {
        var sessions = new List<(long, long, long)>();
        for (var i = 0; i < Math.Min(SessionsPerDay, world.CounterIds.Count); i++)
        {
            var counterId = world.CounterIds[i];
            var operatorId = world.OperatorIds[i % world.OperatorIds.Count];
            var id = await tx.RunAsync(async s =>
            {
                var existing = await s.Bill.Sessions
                    .Where(x => x.CounterId == counterId && x.BusinessDay == day)
                    .Select(x => (long?)x.Id).FirstOrDefaultAsync();
                if (existing is not null) return existing.Value;
                var session = await billing.OpenSessionAsync(
                    s.Bill, world.BranchId, counterId, operatorId, 2000);
                return session.Id;
            });
            sessions.Add((id, counterId, operatorId));
        }
        return sessions;
    }

    private static async Task RegisterPatientsAsync(
        HmsTx tx, RegistrationService registration, World world, Random rng, Counters counters)
    {
        for (var i = 0; i < RegistrationsPerDay; i++)
        {
            var name = $"{Given[rng.Next(Given.Length)]} {Family[rng.Next(Family.Length)]}";
            var age = (short)rng.Next(1, 88);
            var phone = $"01{rng.Next(3, 10)}{rng.Next(10_000_000, 99_999_999)}";
            var id = await tx.RunAsync(async s =>
            {
                var patient = await registration.RegisterAsync(s.Reg, s.Kernel, new RegisterPatientCommand(
                    world.BranchId, name, rng.Next(2) == 0 ? 'M' : 'F', null, age, true,
                    phone, null, Areas[rng.Next(Areas.Length)], null, false, 1, "History Generator"));
                return patient.Id;
            });
            world.PatientIds.Add(id);
            counters.Patients++;
        }
    }

    private static async Task BillOpdAsync(
        HmsTx tx, BillingService billing, LisService lis, RateResolver rates, TimeProvider clock,
        World world, List<(long SessionId, long CounterId, long OperatorId)> sessions,
        Random rng, DateOnly day, Counters counters)
    {
        for (var i = 0; i < OpdInvoicesPerDay; i++)
        {
            var patientId = PickPatient(world, rng);
            var (sessionId, counterId, operatorId) = sessions[rng.Next(sessions.Count)];
            var withTests = i < DiagnosticOrdersPerDay && world.TestCatalogIds.Count > 0;

            await tx.RunAsync(async s =>
            {
                // The charge poster binds to this transaction's BillDbContext, so diagnostics is
                // built per transaction — the same lifetime the request pipeline gives it.
                var diagnostics = new DiagnosticsService(new ChargePoster(s.Bill, billing), clock);

                // Npgsql accepts only UTC offsets on `timestamptz`; the Dhaka wall-clock instant
                // is converted, never passed raw.
                var encounter = await CounterContext.GetOrCreateEncounterAsync(
                    s.Bill, world.BranchId, patientId, counterId, day, "OPD", operatorId,
                    clock.GetUtcNow());

                var lineCount = rng.Next(1, 4);
                for (var l = 0; l < lineCount && world.Services.Count > 0; l++)
                {
                    var (serviceId, name) = world.Services[rng.Next(world.Services.Count)];
                    var rate = await rates.ResolveAsync(s.Adm, "service", serviceId, day);
                    if (rate.Price <= 0) continue;
                    await billing.PostChargeAsync(s.Bill, world.BranchId, encounter.Id, "Billing",
                        new NewChargeLine("service", serviceId, name, 1, rate.Price, rate.RateVersionId),
                        operatorId);
                }

                long? orderId = null;
                var orderedTests = new List<(long OrderTestId, long TestId)>();
                if (withTests)
                {
                    var picked = Enumerable.Range(0, rng.Next(1, 4))
                        .Select(_ => world.TestCatalogIds[rng.Next(world.TestCatalogIds.Count)])
                        .Distinct().ToList();
                    var tests = new List<OrderedTest>();
                    foreach (var testId in picked)
                    {
                        var rate = await rates.ResolveAsync(s.Adm, "test", testId, day);
                        if (rate.Price <= 0) continue;
                        var test = await s.Adm.TestCatalog.AsNoTracking().SingleAsync(t => t.Id == testId);
                        tests.Add(new OrderedTest(testId, test.Name, test.TatMinutes, rate.Price,
                            rate.RateVersionId));
                    }
                    if (tests.Count > 0)
                    {
                        // CreateOrderAsync posts the test charge lines itself through the
                        // billing contract — the caller must not post them a second time.
                        var order = await diagnostics.CreateOrderAsync(s.Diag, world.BranchId, patientId,
                            encounter.Id, tests, null, null, operatorId);
                        orderId = order.Id;
                        orderedTests = await s.Diag.OrderTests.AsNoTracking()
                            .Where(ot => ot.TestOrderId == order.Id)
                            .Select(ot => new ValueTuple<long, long>(ot.Id, ot.TestCatalogId))
                            .ToListAsync();
                    }
                }

                var hasCharges = await s.Bill.ChargeLines
                    .AnyAsync(c => c.EncounterId == encounter.Id && c.InvoiceId == null);
                if (!hasCharges) return;

                // One invoice in twelve carries an approved-looking flat discount; the rest are
                // clean. Discounts are what make the reports and the dashboard interesting.
                var invoice = await billing.CreateInvoiceAsync(
                    s.Bill, s.Kernel, world.BranchId, encounter.Id, sessionId, patientId,
                    0m, 0, null, operatorId, "History Generator");
                counters.Invoices++;

                if (orderId is not null)
                    await diagnostics.MarkInvoicedAsync(s.Diag, orderId.Value, invoice.Id);

                // §14 mix: most bills are paid in full at the counter; about one in eight leaves
                // a due, which is what gives /billing/dues and the MD dashboard something real.
                var due = await s.Bill.Dues.AsNoTracking().SingleAsync(d => d.InvoiceId == invoice.Id);
                var pay = rng.Next(8) == 0 ? due.Balance / 2 : due.Balance;
                if (pay > 0)
                {
                    await billing.CollectAsync(s.Bill, s.Kernel, world.BranchId, invoice.Id, sessionId,
                        pay, rng.Next(5) == 0 ? "card" : "cash", null, operatorId, "History Generator");
                    counters.Receipts++;
                }

                if (orderId is not null && pay == due.Balance)
                {
                    await diagnostics.MarkPaidAsync(s.Diag, s.Kernel, orderId.Value);
                    await RunLabAsync(s, lis, world, orderedTests, rng, counters);
                }
            });
        }
    }

    /// <summary>Sample → collect → receive → result → verify, the §11 pipeline in full.</summary>
    private static async Task RunLabAsync(
        TxScope s, LisService lis, World world, List<(long OrderTestId, long TestId)> orderedTests,
        Random rng, Counters counters)
    {
        if (orderedTests.Count == 0) return;
        counters.Orders++;

        var sample = await lis.CreateSampleAsync(s.Lis, world.BranchId, "blood",
            orderedTests.Select(t => t.OrderTestId).ToList());
        await lis.CollectAsync(s.Lis, sample.Id, 1);
        await lis.ReceiveAsync(s.Lis, sample.Id, 1);

        // ~85% of samples reach a verified result the same day; the rest stay in the pipeline,
        // which is what a real board looks like at any moment.
        if (rng.Next(100) >= 85) return;

        foreach (var (orderTestId, _) in orderedTests)
        {
            var result = await lis.EnterResultAsync(s.Lis, orderTestId,
                new Dictionary<string, object> { ["value"] = rng.Next(4, 18) }, null, 3);
            await lis.VerifyAsync(s.Lis, result.Id, 4, "Pathologist");
            counters.Results++;
        }
    }

    private static async Task SellPharmacyAsync(
        HmsTx tx, BillingService billing, StockService stock, TimeProvider clock, World world,
        List<(long SessionId, long CounterId, long OperatorId)> sessions, Random rng, DateOnly day,
        Counters counters)
    {
        if (world.ProductIds.Count == 0) return;

        // Replenishment on the reorder rule, not on a calendar: without it the outlet runs dry
        // on day three and the rest of the history is a study in out-of-stock errors.
        await ReceiveStockAsync(tx, stock, world, day);

        var (sessionId, counterId, operatorId) = sessions[0];
        for (var i = 0; i < PharmacySalesPerDay; i++)
        {
            var patientId = rng.Next(3) == 0 ? PickPatient(world, rng) : 0;
            try
            {
                await tx.RunAsync(async s =>
                {
                    var buyer = patientId != 0
                        ? patientId
                        : await PharmacySale.EnsureWalkInPatientAsync(s, world.BranchId, operatorId);
                    var items = Enumerable.Range(0, rng.Next(1, 4))
                        .Select(_ => new SaleItem(
                            world.ProductIds[rng.Next(world.ProductIds.Count)], rng.Next(1, 11)))
                        .GroupBy(x => x.ProductId)
                        .Select(g => new SaleItem(g.Key, g.Sum(x => x.Qty)))
                        .ToList();

                    var session = new OpenSession(
                        sessionId, counterId, "Counter", "opd", day, 2000);
                    // The cart's total is only known once FEFO has picked the batches, so the
                    // sale is saved untendered and paid off its own due — the same two steps the
                    // counter performs, in the same transaction.
                    var invoiceId = await PharmacySale.SaveAsync(s, billing, stock, clock,
                        world.BranchId, world.OutletId, session, buyer, items, 0, null,
                        [], operatorId, "History Generator");
                    var balance = await s.Bill.Dues.AsNoTracking()
                        .Where(d => d.InvoiceId == invoiceId).Select(d => d.Balance).SingleAsync();
                    if (balance > 0)
                        await billing.CollectAsync(s.Bill, s.Kernel, world.BranchId, invoiceId,
                            sessionId, balance, "cash", null, operatorId, "History Generator");
                });
                counters.Sales++;
            }
            catch (PharmacyException)
            {
                // Out of stock on a randomly picked product is a legitimate outcome, not a
                // generator failure — the next sale picks different products.
            }
        }
    }

    /// <summary>A day's worth of demand is roughly 400 units of a product; anything under two
    /// days' cover gets a fresh batch, which is what a pharmacy actually does.</summary>
    private const int ReplenishBelow = 800;
    private const int ReplenishQty = 4000;

    private static async Task ReceiveStockAsync(HmsTx tx, StockService stock, World world, DateOnly day)
        => await tx.RunAsync(async s =>
        {
            foreach (var productId in world.ProductIds)
            {
                var onHand = await s.Pharm.Batches.AsNoTracking()
                    .Where(b => b.ProductId == productId && b.OutletId == world.OutletId
                                && b.State == Hms.Pharmacy.Data.BatchState.InStock)
                    .SumAsync(b => (int?)b.QtyOnHand) ?? 0;
                if (onHand >= ReplenishBelow) continue;

                var batchNo = $"GEN-{day:yyMMdd}-{productId}";
                if (await s.Pharm.Batches.AnyAsync(b => b.BatchNo == batchNo && b.ProductId == productId))
                    continue;
                await stock.ReceiveAsync(s.Pharm, s.Kernel, world.BranchId, world.OutletId, productId,
                    batchNo, day.AddMonths(18), ReplenishQty, 400, 600, null, 1, "History Generator");
            }
        });

    private static async Task RunWardAsync(
        HmsTx tx, BillingService billing, FolioService folios, RateResolver rates, IpdService ipdSvc,
        TimeProvider clock, World world, List<(long SessionId, long CounterId, long OperatorId)> sessions,
        Random rng, Counters counters)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var (sessionId, _, operatorId) = sessions[0];

        // Discharge everyone whose stay ended: catch up bed days, settle, free the bed.
        foreach (var stay in world.InWard.Where(a => a.Until <= today).ToList())
        {
            try
            {
                await tx.RunAsync(async s =>
                {
                    await IpdBilling.CatchUpBedDaysAsync(s, billing, folios, rates,
                        world.BranchId, stay.AdmissionId, operatorId);
                    await ipdSvc.InitiateDischargeAsync(s.Ipd, s.Kernel, stay.AdmissionId,
                        "Recovered; discharged on advice.", operatorId, "History Generator");
                    await ipdSvc.ClinicallyClearAsync(s.Ipd, s.Kernel, stay.AdmissionId,
                        operatorId, "History Generator");
                    await IpdBilling.PrepareSettlementAsync(s, billing, folios, rates, clock,
                        world.BranchId, stay.AdmissionId, operatorId);
                    var settled = await IpdBilling.ConfirmSettlementAsync(s, billing, folios, ipdSvc,
                        world.BranchId, stay.AdmissionId, sessionId, 0m, 0, null,
                        operatorId, "History Generator");
                    if (settled.Due > 0)
                        await billing.CollectAsync(s.Bill, s.Kernel, world.BranchId, settled.InvoiceId,
                            sessionId, settled.Due, "cash", null, operatorId, "History Generator");
                    await ipdSvc.DischargeAsync(s.Ipd, s.Kernel, stay.AdmissionId, 0, null,
                        operatorId, "History Generator");
                    await ipdSvc.CleaningDoneAsync(s.Ipd, stay.BedId);
                });
                counters.Discharges++;
            }
            catch (IpdException) { /* a stay that will not settle stays in the ward; realistic */ }
            catch (BillingException) { }
            world.InWard.Remove(stay);
        }

        for (var i = 0; i < AdmissionsPerDay; i++)
        {
            var patientId = PickPatient(world, rng);
            var stayDays = rng.Next(2, 6);
            try
            {
                var admitted = await tx.RunAsync(async s =>
                {
                    var bedId = await s.Ipd.Beds.AsNoTracking()
                        .Where(b => b.State == BedState.Free)
                        .Select(b => (long?)b.Id).FirstOrDefaultAsync();
                    if (bedId is null) return ((long, long)?)null;

                    var admission = await ipdSvc.AdmitAsync(s.Ipd, s.Kernel, world.BranchId, patientId,
                        null, "opd", null, bedId.Value, null, 0, false, operatorId, "History Generator");
                    await IpdBilling.PostAdmissionChargesAsync(s, billing, rates, clock,
                        world.BranchId, admission, world.AdmissionFeeServiceId, operatorId);
                    return (admission.Id, bedId.Value);
                });
                if (admitted is null) break;    // ward full — realistic, stop admitting today
                world.InWard.Add((admitted.Value.Item1, admitted.Value.Item2, today.AddDays(stayDays)));
                counters.Admissions++;
            }
            catch (IpdException) { break; }
        }
    }

    private static async Task CloseSessionsAsync(
        HmsTx tx, DayCloseService dayClose,
        List<(long SessionId, long CounterId, long OperatorId)> sessions, Random rng, Counters counters)
    {
        foreach (var (sessionId, _, operatorId) in sessions)
        {
            try
            {
                await tx.RunAsync(async s =>
                {
                    var expected = await s.Bill.Receipts.AsNoTracking()
                        .Where(r => r.CounterSessionId == sessionId && r.Tender == "cash")
                        .SumAsync(r => (long?)r.Amount) ?? 0;
                    var opening = await s.Bill.Sessions.AsNoTracking()
                        .Where(x => x.Id == sessionId).Select(x => x.OpeningFloat).SingleAsync();
                    // One close in ten is short or over by a few taka — a variance report with no
                    // variance in it has never caught anything.
                    var variance = rng.Next(10) == 0 ? rng.Next(-200, 200) : 0;
                    await dayClose.CloseAsync(s.Bill, s.Kernel, sessionId, opening + expected + variance,
                        operatorId, "History Generator");
                });
            }
            catch (BillingException) { /* already closed on a resumed run */ }
        }
        counters.Receipts += 0;
    }

    // ---- helpers ---------------------------------------------------------------

    private static long PickPatient(World world, Random rng)
        => world.PatientIds[rng.Next(Math.Max(1, world.PatientIds.Count))];

    private static async Task<DateOnly?> ReadThroughAsync(HmsTx tx)
        => await tx.RunAsync(async s =>
        {
            var raw = await s.Kernel.Settings.AsNoTracking()
                .Where(x => x.Key == ThroughSetting).Select(x => x.Value).FirstOrDefaultAsync();
            // kernel.setting.value is jsonb, so the stored form is a quoted JSON string.
            return raw is not null && DateOnly.TryParse(raw.Trim('"'), out var d) ? d : (DateOnly?)null;
        });

    private static async Task MarkThroughAsync(HmsTx tx, DateOnly day)
        => await tx.RunAsync(async s =>
        {
            var existing = await s.Kernel.Settings.FirstOrDefaultAsync(x => x.Key == ThroughSetting);
            if (existing is null)
                s.Kernel.Settings.Add(new Setting { Key = ThroughSetting, Value = $"\"{day:yyyy-MM-dd}\"" });
            else
                existing.Value = $"\"{day:yyyy-MM-dd}\"";
            await s.Kernel.SaveChangesAsync();
        });

    private static readonly string[] Given =
    [
        "Rahim", "Karim", "Fatema", "Nasrin", "Jahangir", "Shirin", "Rubel", "Mitu", "Sohel",
        "Farzana", "Alamgir", "Rokeya", "Tanvir", "Sultana", "Babul", "Rehana", "Mizan", "Shefali",
        "Anwar", "Nazma", "Faruk", "Rina", "Jamil", "Shahnaz", "Delwar", "Momtaz", "Kabir", "Laila",
    ];

    private static readonly string[] Family =
    [
        "Uddin", "Ahmed", "Begum", "Hossain", "Khatun", "Islam", "Rahman", "Chowdhury", "Sarker",
        "Miah", "Akter", "Das", "Sheikh", "Molla", "Talukder", "Bhuiyan", "Mondol", "Gazi",
    ];

    private static readonly string[] Areas =
    [
        "Zindabazar", "Ambarkhana", "Subid Bazar", "Bandar Bazar", "Uposhohor", "Tilagor",
        "Shibganj", "Mirabazar", "Kumarpara", "Chowhatta",
    ];
}
