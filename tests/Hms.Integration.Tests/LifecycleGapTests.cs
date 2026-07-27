using Hms.Ipd;
using Hms.Ipd.Data;
using Hms.Kernel.Audit;
using Hms.Kernel.Data;
using Hms.Kernel.Numbering;
using Hms.Kernel.Time;
using Hms.Registration.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Hms.Integration.Tests;

/// <summary>
/// Spec 0020: the defects an end-to-end walk of one patient's life exposed, pinned so they
/// cannot come back. Each test is the failing observation from that walk, inverted.
/// </summary>
[Collection("postgres")]
public class LifecycleGapTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly IpdService _ipd = new(
        new NumberSeriesService(), new AuditWriter(TimeProvider.System),
        new FiscalCalendar(7), TimeProvider.System);

    public LifecycleGapTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        await using var reg = CreateReg(null);
        await reg.Database.MigrateAsync();
        await using var ipd = CreateIpd(null);
        await ipd.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private RegDbContext CreateReg(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<RegDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "reg"))
            : new DbContextOptionsBuilder<RegDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "reg")))
        .UseSnakeCaseNamingConvention().Options);

    private IpdDbContext CreateIpd(NpgsqlConnection? conn) => new(
        (conn is null
            ? new DbContextOptionsBuilder<IpdDbContext>().UseNpgsql(_pg.ConnectionString,
                o => o.MigrationsHistoryTable("__ef_migrations", "ipd"))
            : new DbContextOptionsBuilder<IpdDbContext>().UseNpgsql(conn,
                o => o.MigrationsHistoryTable("__ef_migrations", "ipd")))
        .UseSnakeCaseNamingConvention().Options);

    private KernelDbContext CreateKernel(NpgsqlConnection conn) => new(
        new DbContextOptionsBuilder<KernelDbContext>().UseNpgsql(conn,
            o => o.MigrationsHistoryTable("__ef_migrations", "kernel"))
        .UseSnakeCaseNamingConvention().Options);

    private async Task<T> InTxAsync<T>(Func<RegDbContext, IpdDbContext, KernelDbContext, Task<T>> body)
    {
        await using var conn = new NpgsqlConnection(_pg.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using var reg = CreateReg(conn);
        await using var ipd = CreateIpd(conn);
        await using var kernel = CreateKernel(conn);
        await reg.Database.UseTransactionAsync(tx);
        await ipd.Database.UseTransactionAsync(tx);
        await kernel.Database.UseTransactionAsync(tx);
        var result = await body(reg, ipd, kernel);
        await tx.CommitAsync();
        return result;
    }

    // ---- gap 1: a phone typed as digits must find the patient --------------

    [Theory]
    [InlineData("01712-345999")]   // exactly as registration stores it (§7 U13)
    [InlineData("01712345999")]    // as it is written on a prescription slip
    [InlineData("345999")]         // the tail an operator remembers
    [InlineData("8801712345999")]  // with the country code
    public async Task A_phone_finds_its_patient_however_it_is_typed(string typed)
    {
        var uhid = "PH-" + Guid.NewGuid().ToString("N")[..8];
        await InTxAsync(async (reg, _, __) =>
        {
            reg.Patients.Add(new Patient
            {
                BranchId = 1, Uhid = uhid, FullName = "Phone Probe", Sex = 'M', AgeYears = 40,
                Phone = "01712-345999",              // stored in the readable form
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            });
            await reg.SaveChangesAsync();
            return 0;
        });

        // The digits the operator typed, normalised the way PatientSearch does it.
        var digits = new string(typed.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("880")) digits = "0" + digits[3..];
        var like = $"%{digits}%";

        var found = await InTxAsync(async (reg, _, __) => await reg.Patients.AsNoTracking()
            .Where(p => p.Uhid == uhid && p.PhoneDigits != null
                        && EF.Functions.ILike(p.PhoneDigits, like))
            .CountAsync());

        Assert.Equal(1, found);
    }

    [Fact]
    public async Task Phone_digits_are_maintained_by_the_database_not_the_app()
    {
        var uhid = "PH-" + Guid.NewGuid().ToString("N")[..8];
        await InTxAsync(async (reg, _, __) =>
        {
            reg.Patients.Add(new Patient
            {
                BranchId = 1, Uhid = uhid, FullName = "Generated Column", Sex = 'F', AgeYears = 22,
                Phone = "+880 1811-999 888", CreatedAt = DateTimeOffset.UtcNow, CreatedBy = 1,
            });
            await reg.SaveChangesAsync();
            return 0;
        });
        var afterInsert = await InTxAsync(async (reg, _, __) => await reg.Patients.AsNoTracking()
            .Where(p => p.Uhid == uhid).Select(p => p.PhoneDigits).SingleAsync());
        Assert.Equal("8801811999888", afterInsert);

        // Changing the phone re-derives it — no second write path to forget.
        await InTxAsync(async (reg, _, __) =>
        {
            var p = await reg.Patients.SingleAsync(x => x.Uhid == uhid);
            p.Phone = "01999-000111";
            await reg.SaveChangesAsync();
            return 0;
        });
        var afterUpdate = await InTxAsync(async (reg, _, __) => await reg.Patients.AsNoTracking()
            .Where(p => p.Uhid == uhid).Select(p => p.PhoneDigits).SingleAsync());
        Assert.Equal("01999000111", afterUpdate);
    }

    // ---- gap 2: nobody leaves the gate owing money, silently ---------------

    private async Task<long> AdmitAndSettleAsync()
        => await InTxAsync(async (_, ipd, kernel) =>
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
            var admission = await _ipd.AdmitAsync(ipd, kernel, 1,
                Random.Shared.NextInt64(1_000_000, 9_999_999), null, "direct", null,
                bed.Id, null, 0, reserveOnly: false, 7, "tester");
            await _ipd.InitiateDischargeAsync(ipd, kernel, admission.Id, "well", 7, "tester");
            await _ipd.ClinicallyClearAsync(ipd, kernel, admission.Id, 7, "tester");
            await _ipd.MarkFinanciallySettledAsync(ipd, admission.Id);
            return admission.Id;
        });

    [Fact]
    public async Task Discharge_with_money_owed_is_refused_without_a_reason()
    {
        var admissionId = await AdmitAndSettleAsync();

        var error = await Assert.ThrowsAsync<IpdException>(() =>
            InTxAsync<int>(async (_, ipd, kernel) =>
            {
                await _ipd.DischargeAsync(ipd, kernel, admissionId, outstanding: 1700,
                    outstandingReason: null, 7, "tester");
                return 0;
            }));
        Assert.Contains("1,700", error.Message);       // the operator is told the number

        // Whitespace is not a reason.
        await Assert.ThrowsAsync<IpdException>(() =>
            InTxAsync<int>(async (_, ipd, kernel) =>
            {
                await _ipd.DischargeAsync(ipd, kernel, admissionId, 1700, "   ", 7, "tester");
                return 0;
            }));

        // The admission is untouched by the refusals.
        var state = await InTxAsync(async (_, ipd, __) =>
            (await ipd.Admissions.AsNoTracking().SingleAsync(a => a.Id == admissionId)).State);
        Assert.Equal(AdmissionState.FinanciallySettled, state);
    }

    [Fact]
    public async Task Discharge_with_a_stated_reason_is_allowed_and_audited_at_tier_2()
    {
        var admissionId = await AdmitAndSettleAsync();

        await InTxAsync(async (_, ipd, kernel) =>
        {
            await _ipd.DischargeAsync(ipd, kernel, admissionId, 1700,
                "corporate credit approved by MD", 7, "tester");
            return 0;
        });

        var (state, audit) = await InTxAsync(async (_, ipd, kernel) =>
        {
            var admission = await ipd.Admissions.AsNoTracking().SingleAsync(a => a.Id == admissionId);
            var evt = await kernel.AuditEvents.AsNoTracking()
                .Where(e => e.Entity == "ipd.admission" && e.EntityId == admissionId
                            && e.Action == "ipd.discharge")
                .OrderByDescending(e => e.Id).FirstAsync();
            return (admission.State, evt);
        });

        Assert.Equal(AdmissionState.Discharged, state);
        Assert.Equal(2, audit.Tier);                                   // money-relevant stream
        Assert.Contains("1700", audit.After);
        Assert.Contains("corporate credit approved by MD", audit.After);
    }

    [Fact]
    public async Task Discharge_with_nothing_owed_stays_a_one_click_action()
    {
        var admissionId = await AdmitAndSettleAsync();
        await InTxAsync(async (_, ipd, kernel) =>
        {
            await _ipd.DischargeAsync(ipd, kernel, admissionId, outstanding: 0,
                outstandingReason: null, 7, "tester");
            return 0;
        });
        var (state, tier) = await InTxAsync(async (_, ipd, kernel) =>
        {
            var admission = await ipd.Admissions.AsNoTracking().SingleAsync(a => a.Id == admissionId);
            var evt = await kernel.AuditEvents.AsNoTracking()
                .Where(e => e.Entity == "ipd.admission" && e.EntityId == admissionId
                            && e.Action == "ipd.discharge")
                .OrderByDescending(e => e.Id).FirstAsync();
            return (admission.State, evt.Tier);
        });
        Assert.Equal(AdmissionState.Discharged, state);
        Assert.Equal(1, tier);
    }
}
